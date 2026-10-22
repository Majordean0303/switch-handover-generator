using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VisioCreator.Core.Graph;
using VisioCreator.Core.Layout;
using VisioCreator.Core.Models;
using VisioCreator.Export.Abstractions;
using VisioCreator.Export.Configuration;
using VisioCreator.Export.Stencils;

namespace VisioCreator.Export.OpenXml;

public class OpenXmlVisioExporter : IVisioExporter
{
    private readonly ILogger<OpenXmlVisioExporter> _logger;
    private readonly StencilManager _stencilManager;

    public OpenXmlVisioExporter(ILogger<OpenXmlVisioExporter>? logger = null, StencilManager? stencilManager = null)
    {
        _logger = logger ?? NullLogger<OpenXmlVisioExporter>.Instance;
        _stencilManager = stencilManager ?? new StencilManager();
    }

    public void Export(TopologyGraph graph, LayoutResult layout, string outputPath, VisioExportOptions? options = null)
    {
        options ??= VisioExportOptions.Default;

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var zipStream = new FileStream(outputPath, FileMode.Create);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        // 1. Divide into pages (by cluster / site if enabled)
        var pageGroups = new List<(string PageName, List<NodeCoordinate> Nodes, List<RoutedLink> Links)>();

        if (options.PagePerSite && layout.Clusters.Any())
        {
            foreach (var cluster in layout.Clusters)
            {
                var clusterNodes = layout.Nodes.Values
                    .Where(n => (n.Device.Site != null && cluster.Location.Contains(n.Device.Site, StringComparison.OrdinalIgnoreCase)) ||
                                (n.Device.Location != null && cluster.Location.Equals(n.Device.Location, StringComparison.OrdinalIgnoreCase)) ||
                                (cluster.Location.Contains("Bangalore", StringComparison.OrdinalIgnoreCase) && n.Device.Hostname.Contains("BLR", StringComparison.OrdinalIgnoreCase)) ||
                                (cluster.Location.Contains("Nagaur", StringComparison.OrdinalIgnoreCase) && (n.Device.Hostname.Contains("NAGAUR", StringComparison.OrdinalIgnoreCase) || n.Device.Hostname.Contains("NGR", StringComparison.OrdinalIgnoreCase))))
                    .ToList();

                if (!clusterNodes.Any()) continue;

                var clusterHostnames = clusterNodes.Select(n => n.Device.Hostname).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var clusterLinks = layout.RoutedLinks
                    .Where(l => clusterHostnames.Contains(l.Link.Source) && clusterHostnames.Contains(l.Link.Destination))
                    .ToList();

                pageGroups.Add((cluster.Location, clusterNodes, clusterLinks));
            }

            // Collect any unassigned nodes
            var assigned = pageGroups.SelectMany(p => p.Nodes.Select(n => n.Device.Hostname)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var remaining = layout.Nodes.Values.Where(n => !assigned.Contains(n.Device.Hostname)).ToList();
            if (remaining.Any())
            {
                var remHostnames = remaining.Select(n => n.Device.Hostname).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var remLinks = layout.RoutedLinks.Where(l => remHostnames.Contains(l.Link.Source) && remHostnames.Contains(l.Link.Destination)).ToList();
                pageGroups.Add(("Other Devices", remaining, remLinks));
            }
        }
        else
        {
            pageGroups.Add(("Network Topology", layout.Nodes.Values.ToList(), layout.RoutedLinks));
        }

        if (!pageGroups.Any())
        {
            pageGroups.Add(("Network Topology", layout.Nodes.Values.ToList(), layout.RoutedLinks));
        }

        var masters = _stencilManager.Masters.ToList();

        // 2. Write [Content_Types].xml
        WriteContentTypes(archive, pageGroups.Count, masters.Count);

        // 3. Write _rels/.rels
        WriteRootRels(archive);

        // 4. Write visio/_rels/document.xml.rels
        WriteDocumentRels(archive);

        // 5. Write visio/document.xml
        WriteDocumentXml(archive);

        // 6. Write visio/windows.xml
        WriteWindowsXml(archive);

        // 7. Write Masters
        WriteMasters(archive, masters);

        // 8. Write Pages & Page Contents
        WritePages(archive, pageGroups, masters, options);

        _logger.LogInformation("Exported Visio diagram to {Path} ({PageCount} pages)", outputPath, pageGroups.Count);
    }

    private void WriteContentTypes(ZipArchive archive, int pageCount, int masterCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
        sb.AppendLine("<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>");
        sb.AppendLine("  <Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml' />");
        sb.AppendLine("  <Default Extension='xml' ContentType='application/xml' />");
        sb.AppendLine("  <Override PartName='/visio/document.xml' ContentType='application/vnd.ms-visio.drawing.main+xml' />");
        sb.AppendLine("  <Override PartName='/visio/windows.xml' ContentType='application/vnd.ms-visio.windows+xml' />");
        sb.AppendLine("  <Override PartName='/visio/masters/masters.xml' ContentType='application/vnd.ms-visio.masters+xml' />");
        sb.AppendLine("  <Override PartName='/visio/pages/pages.xml' ContentType='application/vnd.ms-visio.pages+xml' />");

        for (int i = 1; i <= masterCount; i++)
        {
            sb.AppendLine($"  <Override PartName='/visio/masters/master{i}.xml' ContentType='application/vnd.ms-visio.master+xml' />");
        }

        for (int i = 1; i <= pageCount; i++)
        {
            sb.AppendLine($"  <Override PartName='/visio/pages/page{i}.xml' ContentType='application/vnd.ms-visio.page+xml' />");
        }

        sb.AppendLine("</Types>");
        AddZipEntry(archive, "[Content_Types].xml", sb.ToString());
    }

    private void WriteRootRels(ZipArchive archive)
    {
        var xml = "<?xml version='1.0' encoding='utf-8'?>\n" +
                  "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>\n" +
                  "  <Relationship Id='rId1' Type='http://schemas.microsoft.com/visio/2010/relationships/document' Target='visio/document.xml' />\n" +
                  "</Relationships>";
        AddZipEntry(archive, "_rels/.rels", xml);
    }

    private void WriteDocumentRels(ZipArchive archive)
    {
        var xml = "<?xml version='1.0' encoding='utf-8'?>\n" +
                  "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>\n" +
                  "  <Relationship Id='rId1' Type='http://schemas.microsoft.com/visio/2010/relationships/pages' Target='pages/pages.xml' />\n" +
                  "  <Relationship Id='rId2' Type='http://schemas.microsoft.com/visio/2010/relationships/masters' Target='masters/masters.xml' />\n" +
                  "  <Relationship Id='rId3' Type='http://schemas.microsoft.com/visio/2010/relationships/windows' Target='windows.xml' />\n" +
                  "</Relationships>";
        AddZipEntry(archive, "visio/_rels/document.xml.rels", xml);
    }

    private void WriteDocumentXml(ZipArchive archive)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
        sb.AppendLine("<VisioDocument xmlns='http://schemas.microsoft.com/office/visio/2012/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships' xml:space='preserve'>");
        sb.AppendLine("  <DocumentSettings>");
        sb.AppendLine("    <GlueSettings>9</GlueSettings>");
        sb.AppendLine("    <SnapSettings>65535</SnapSettings>");
        sb.AppendLine("    <SnapExtensions>34</SnapExtensions>");
        sb.AppendLine("  </DocumentSettings>");
        sb.AppendLine("  <Colors/>");
        sb.AppendLine("  <FaceNames/>");
        sb.AppendLine("  <StyleSheets>");
        sb.AppendLine("    <StyleSheet ID='0' NameU='No Style' Name='No Style'>");
        sb.AppendLine("      <Cell N='EnableFillProps' V='1'/>");
        sb.AppendLine("      <Cell N='EnableLineProps' V='1'/>");
        sb.AppendLine("      <Cell N='EnableTextProps' V='1'/>");
        sb.AppendLine("    </StyleSheet>");
        sb.AppendLine("    <StyleSheet ID='3' NameU='Normal' Name='Normal'>");
        sb.AppendLine("      <Cell N='EnableFillProps' V='1'/>");
        sb.AppendLine("      <Cell N='EnableLineProps' V='1'/>");
        sb.AppendLine("      <Cell N='EnableTextProps' V='1'/>");
        sb.AppendLine("      <Cell N='LineWeight' V='0.01'/>");
        sb.AppendLine("      <Cell N='LineColor' V='#333333'/>");
        sb.AppendLine("      <Cell N='FillForegnd' V='#FFFFFF'/>");
        sb.AppendLine("      <Cell N='Char.Size' V='0.11'/>");
        sb.AppendLine("      <Cell N='Char.Color' V='#1A1A1A'/>");
        sb.AppendLine("      <Cell N='VerticalAlign' V='1'/>");
        sb.AppendLine("      <Section N='Paragraph'>");
        sb.AppendLine("        <Row IX='0'><Cell N='HorzAlign' V='1'/></Row>");
        sb.AppendLine("      </Section>");
        sb.AppendLine("    </StyleSheet>");
        sb.AppendLine("  </StyleSheets>");
        sb.AppendLine("  <DocumentSheet NameU='TheDoc' Name='TheDoc'>");
        sb.AppendLine("    <Cell N='OutputFormat' V='0'/>");
        sb.AppendLine("  </DocumentSheet>");
        sb.AppendLine("</VisioDocument>");
        AddZipEntry(archive, "visio/document.xml", sb.ToString());
    }

    private void WriteWindowsXml(ZipArchive archive)
    {
        var xml = "<?xml version='1.0' encoding='utf-8'?>\n" +
                  "<Windows xmlns='http://schemas.microsoft.com/office/visio/2012/main'>\n" +
                  "  <Window ID='0' WindowType='Drawing' WindowState='1073741824'>\n" +
                  "    <ShowRulers>1</ShowRulers>\n" +
                  "    <ShowGrid>1</ShowGrid>\n" +
                  "    <ShowPageBreaks>0</ShowPageBreaks>\n" +
                  "    <ShowGuides>1</ShowGuides>\n" +
                  "    <ShowConnectionPoints>1</ShowConnectionPoints>\n" +
                  "    <GlueOn>1</GlueOn>\n" +
                  "    <SnapOn>1</SnapOn>\n" +
                  "  </Window>\n" +
                  "</Windows>";
        AddZipEntry(archive, "visio/windows.xml", xml);
    }

    private void CopyCustomMasters(ZipArchive archive, string vssxPath)
    {
        using var srcStream = new FileStream(vssxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var srcZip = new ZipArchive(srcStream, ZipArchiveMode.Read);
        bool hasDynamicConnector = false;

        foreach (var entry in srcZip.Entries)
        {
            if (entry.FullName.StartsWith("visio/masters/", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.StartsWith("visio/media/", StringComparison.OrdinalIgnoreCase))
            {
                if (entry.FullName.Equals("visio/masters/master10.xml", StringComparison.OrdinalIgnoreCase))
                {
                    hasDynamicConnector = true;
                }
                var target = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var inStream = entry.Open();
                using var outStream = target.Open();
                inStream.CopyTo(outStream);
            }
        }

        if (!hasDynamicConnector)
        {
            var mSb = new StringBuilder();
            mSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
            mSb.AppendLine("<MasterContents xmlns='http://schemas.microsoft.com/office/visio/2012/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships' xml:space='preserve'>");
            mSb.AppendLine("  <Shapes>");
            mSb.AppendLine("    <Shape ID='5' Type='Shape' NameU='Dynamic connector' LineStyle='3' FillStyle='3' TextStyle='3'>");
            mSb.AppendLine("      <Cell N='PinX' V='0' F='GUARD((BeginX+EndX)/2)'/>");
            mSb.AppendLine("      <Cell N='PinY' V='0' F='GUARD((BeginY+EndY)/2)'/>");
            mSb.AppendLine("      <Cell N='Width' V='1' F='GUARD(EndX-BeginX)'/>");
            mSb.AppendLine("      <Cell N='Height' V='1' F='GUARD(EndY-BeginY)'/>");
            mSb.AppendLine("      <Cell N='LocPinX' V='0' F='GUARD(Width*0.5)'/>");
            mSb.AppendLine("      <Cell N='LocPinY' V='0' F='GUARD(Height*0.5)'/>");
            mSb.AppendLine("      <Cell N='Angle' V='0' F='GUARD(0DA)'/>");
            mSb.AppendLine("      <Cell N='ObjType' V='2'/>");
            mSb.AppendLine("      <Cell N='ShapeRouteStyle' V='1'/>");
            mSb.AppendLine("      <Cell N='ConFixedCode' V='6'/>");
            mSb.AppendLine("    </Shape>");
            mSb.AppendLine("  </Shapes>");
            mSb.AppendLine("</MasterContents>");
            AddZipEntry(archive, "visio/masters/master10.xml", mSb.ToString());
        }
    }

    private void WriteMasters(ZipArchive archive, List<StencilMasterDefinition> masters)
    {
        if (_stencilManager.HasCustomStencil)
        {
            CopyCustomMasters(archive, _stencilManager.CustomStencilPath!);
            return;
        }

        // 1. visio/masters/_rels/masters.xml.rels
        var relsSb = new StringBuilder();
        relsSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
        relsSb.AppendLine("<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>");
        for (int i = 0; i < masters.Count; i++)
        {
            relsSb.AppendLine($"  <Relationship Id='rId{i + 1}' Type='http://schemas.microsoft.com/visio/2010/relationships/master' Target='master{i + 1}.xml' />");
        }
        relsSb.AppendLine("</Relationships>");
        AddZipEntry(archive, "visio/masters/_rels/masters.xml.rels", relsSb.ToString());

        // 2. visio/masters/masters.xml
        var mastersSb = new StringBuilder();
        mastersSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
        mastersSb.AppendLine("<Masters xmlns='http://schemas.microsoft.com/office/visio/2012/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'>");
        for (int i = 0; i < masters.Count; i++)
        {
            var m = masters[i];
            mastersSb.AppendLine($"  <Master ID='{m.Id}' NameU='{Escape(m.NameU)}' Name='{Escape(m.Name)}' BaseID='{{{Guid.NewGuid()}}}' PatternFlags='0' Hidden='0'>");
            mastersSb.AppendLine("    <PageSheet LineStyle='0' FillStyle='0' TextStyle='0'>");
            mastersSb.AppendLine($"      <Cell N='PageWidth' V='{m.DefaultWidth}'/>");
            mastersSb.AppendLine($"      <Cell N='PageHeight' V='{m.DefaultHeight}'/>");
            mastersSb.AppendLine("    </PageSheet>");
            mastersSb.AppendLine($"    <Rel r:id='rId{i + 1}'/>");
            mastersSb.AppendLine("  </Master>");
        }
        mastersSb.AppendLine("</Masters>");
        AddZipEntry(archive, "visio/masters/masters.xml", mastersSb.ToString());

        // 3. Each visio/masters/master{i}.xml
        for (int i = 0; i < masters.Count; i++)
        {
            var m = masters[i];
            var mSb = new StringBuilder();
            mSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
            mSb.AppendLine("<MasterContents xmlns='http://schemas.microsoft.com/office/visio/2012/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships' xml:space='preserve'>");
            mSb.AppendLine("  <Shapes>");

            if (m.Id == 10) // Dynamic connector master
            {
                mSb.AppendLine($"    <Shape ID='5' Type='Shape' NameU='Dynamic connector' LineStyle='3' FillStyle='3' TextStyle='3'>");
                mSb.AppendLine("      <Cell N='PinX' V='0' F='GUARD((BeginX+EndX)/2)'/>");
                mSb.AppendLine("      <Cell N='PinY' V='0' F='GUARD((BeginY+EndY)/2)'/>");
                mSb.AppendLine("      <Cell N='Width' V='1' F='GUARD(EndX-BeginX)'/>");
                mSb.AppendLine("      <Cell N='Height' V='1' F='GUARD(EndY-BeginY)'/>");
                mSb.AppendLine("      <Cell N='LocPinX' V='0' F='GUARD(Width*0.5)'/>");
                mSb.AppendLine("      <Cell N='LocPinY' V='0' F='GUARD(Height*0.5)'/>");
                mSb.AppendLine("      <Cell N='Angle' V='0' F='GUARD(0DA)'/>");
                mSb.AppendLine("      <Cell N='ObjType' V='2'/>");
                mSb.AppendLine("      <Cell N='GlueType' V='2'/>");
                mSb.AppendLine("      <Cell N='ConFixedCode' V='6'/>");
                mSb.AppendLine("      <Cell N='ShapeRouteStyle' V='1'/>");
                mSb.AppendLine($"      <Cell N='LineColor' V='{m.LineColor}'/>");
                mSb.AppendLine("      <Cell N='LineWeight' V='0.02'/>");
                mSb.AppendLine("      <Section N='Geometry' IX='0'>");
                mSb.AppendLine("        <Cell N='NoFill' V='1'/>");
                mSb.AppendLine("        <Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>");
                mSb.AppendLine("        <Row T='LineTo' IX='2'><Cell N='X' V='1' F='Width*1'/><Cell N='Y' V='0'/></Row>");
                mSb.AppendLine("      </Section>");
                mSb.AppendLine("    </Shape>");
            }
            else // Regular node master
            {
                mSb.AppendLine($"    <Shape ID='5' Type='Shape' NameU='{Escape(m.NameU)}' LineStyle='3' FillStyle='3' TextStyle='3'>");
                mSb.AppendLine($"      <Cell N='PinX' V='{m.DefaultWidth * 0.5}'/>");
                mSb.AppendLine($"      <Cell N='PinY' V='{m.DefaultHeight * 0.5}'/>");
                mSb.AppendLine($"      <Cell N='Width' V='{m.DefaultWidth}'/>");
                mSb.AppendLine($"      <Cell N='Height' V='{m.DefaultHeight}'/>");
                mSb.AppendLine($"      <Cell N='LocPinX' V='{m.DefaultWidth * 0.5}' F='Width*0.5'/>");
                mSb.AppendLine($"      <Cell N='LocPinY' V='{m.DefaultHeight * 0.5}' F='Height*0.5'/>");
                mSb.AppendLine($"      <Cell N='FillForegnd' V='{m.FillColor}'/>");
                mSb.AppendLine($"      <Cell N='LineColor' V='{m.LineColor}'/>");
                mSb.AppendLine("      <Cell N='LineWeight' V='0.02'/>");
                mSb.AppendLine("      <Cell N='Rounding' V='0.08'/>");

                // Connection points
                mSb.AppendLine("      <Section N='Connection'>");
                for (int cIdx = 0; cIdx < m.ConnectionPoints.Count; cIdx++)
                {
                    var cp = m.ConnectionPoints[cIdx];
                    mSb.AppendLine($"        <Row N='{cIdx + 1}' IX='{cIdx}'>");
                    mSb.AppendLine($"          <Cell N='X' V='{cp.X}' F='{cp.FormulaX}'/>");
                    mSb.AppendLine($"          <Cell N='Y' V='{cp.Y}' F='{cp.FormulaY}'/>");
                    mSb.AppendLine($"          <Cell N='DirX' V='{cp.DirX}'/>");
                    mSb.AppendLine($"          <Cell N='DirY' V='{cp.DirY}'/>");
                    mSb.AppendLine("        </Row>");
                }
                mSb.AppendLine("      </Section>");

                // Geometry
                mSb.AppendLine("      <Section N='Geometry' IX='0'>");
                mSb.AppendLine("        <Cell N='NoFill' V='0'/>");
                mSb.AppendLine("        <Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>");
                mSb.AppendLine($"        <Row T='LineTo' IX='2'><Cell N='X' V='{m.DefaultWidth:F3}' F='Width*1'/><Cell N='Y' V='0'/></Row>");
                mSb.AppendLine($"        <Row T='LineTo' IX='3'><Cell N='X' V='{m.DefaultWidth:F3}' F='Width*1'/><Cell N='Y' V='{m.DefaultHeight:F3}' F='Height*1'/></Row>");
                mSb.AppendLine($"        <Row T='LineTo' IX='4'><Cell N='X' V='0'/><Cell N='Y' V='{m.DefaultHeight:F3}' F='Height*1'/></Row>");
                mSb.AppendLine("        <Row T='LineTo' IX='5'><Cell N='X' V='0' F='Geometry1.X1'/><Cell N='Y' V='0' F='Geometry1.Y1'/></Row>");
                mSb.AppendLine("      </Section>");
                mSb.AppendLine("      <Cell N='VerticalAlign' V='1'/>");
                mSb.AppendLine($"      <Cell N='TxtWidth' V='{m.DefaultWidth:F3}' F='Width*1'/>");
                mSb.AppendLine($"      <Cell N='TxtHeight' V='{m.DefaultHeight:F3}' F='Height*1'/>");
                mSb.AppendLine($"      <Cell N='TxtPinX' V='{m.DefaultWidth * 0.5:F3}' F='Width*0.5'/>");
                mSb.AppendLine($"      <Cell N='TxtPinY' V='{m.DefaultHeight * 0.5:F3}' F='Height*0.5'/>");
                mSb.AppendLine($"      <Cell N='TxtLocPinX' V='{m.DefaultWidth * 0.5:F3}' F='TxtWidth*0.5'/>");
                mSb.AppendLine($"      <Cell N='TxtLocPinY' V='{m.DefaultHeight * 0.5:F3}' F='TxtHeight*0.5'/>");
                mSb.AppendLine("      <Cell N='TopMargin' V='0.04'/>");
                mSb.AppendLine("      <Cell N='BottomMargin' V='0.04'/>");
                mSb.AppendLine("      <Cell N='LeftMargin' V='0.05'/>");
                mSb.AppendLine("      <Cell N='RightMargin' V='0.05'/>");
                mSb.AppendLine("      <Cell N='Char.Size' V='0.11'/>");
                mSb.AppendLine("      <Cell N='Char.Color' V='#FFFFFF'/>");
                mSb.AppendLine("      <Section N='Paragraph'>");
                mSb.AppendLine("        <Row IX='0'><Cell N='HorzAlign' V='1'/></Row>");
                mSb.AppendLine("      </Section>");
                mSb.AppendLine("    </Shape>");
            }

            mSb.AppendLine("  </Shapes>");
            mSb.AppendLine("</MasterContents>");
            AddZipEntry(archive, $"visio/masters/master{i + 1}.xml", mSb.ToString());
        }
    }

    private void WritePages(ZipArchive archive, List<(string PageName, List<NodeCoordinate> Nodes, List<RoutedLink> Links)> pageGroups, List<StencilMasterDefinition> masters, VisioExportOptions options)
    {
        // 1. visio/pages/_rels/pages.xml.rels
        var relsSb = new StringBuilder();
        relsSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
        relsSb.AppendLine("<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>");
        for (int i = 0; i < pageGroups.Count; i++)
        {
            relsSb.AppendLine($"  <Relationship Id='rId{i + 1}' Type='http://schemas.microsoft.com/visio/2010/relationships/page' Target='page{i + 1}.xml' />");
        }
        relsSb.AppendLine("</Relationships>");
        AddZipEntry(archive, "visio/pages/_rels/pages.xml.rels", relsSb.ToString());

        // 2. visio/pages/pages.xml
        var pagesSb = new StringBuilder();
        pagesSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
        pagesSb.AppendLine("<Pages xmlns='http://schemas.microsoft.com/office/visio/2012/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'>");
        for (int i = 0; i < pageGroups.Count; i++)
        {
            var p = pageGroups[i];
            pagesSb.AppendLine($"  <Page ID='{i}' NameU='{Escape(p.PageName)}' Name='{Escape(p.PageName)}' IsCustomName='1'>");
            pagesSb.AppendLine("    <PageSheet LineStyle='0' FillStyle='0' TextStyle='0'>");
            pagesSb.AppendLine("      <Cell N='DrawingResizeType' V='1'/>");
            pagesSb.AppendLine("    </PageSheet>");
            pagesSb.AppendLine($"    <Rel r:id='rId{i + 1}'/>");
            pagesSb.AppendLine("  </Page>");
        }
        pagesSb.AppendLine("</Pages>");
        AddZipEntry(archive, "visio/pages/pages.xml", pagesSb.ToString());

        // 3. Each visio/pages/page{i}.xml
        var layers = new[] { "Core", "Distribution", "Access", "AP", "Printer", "External", "Uplink", "PortChannel", "Leaf" };

        for (int pIdx = 0; pIdx < pageGroups.Count; pIdx++)
        {
            var page = pageGroups[pIdx];
            var nodes = page.Nodes;
            var links = page.Links;

            double minX = nodes.Any() ? nodes.Min(n => n.X) : 0;
            double minY = nodes.Any() ? nodes.Min(n => n.Y) : 0;
            double maxX = nodes.Any() ? nodes.Max(n => n.X + n.Width) : 10;
            double maxY = nodes.Any() ? nodes.Max(n => n.Y + n.Height) : 10;

            double pageWidth = Math.Max(14.0, (maxX - minX) + 5.0);
            double pageHeight = Math.Max(10.0, (maxY - minY) + 5.0);

            var pageSb = new StringBuilder();
            pageSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
            pageSb.AppendLine("<PageContents xmlns='http://schemas.microsoft.com/office/visio/2012/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships' xml:space='preserve'>");

            // PageSheet with Layers
            pageSb.AppendLine("  <PageSheet LineStyle='0' FillStyle='0' TextStyle='0'>");
            pageSb.AppendLine($"    <Cell N='PageWidth' V='{pageWidth}'/>");
            pageSb.AppendLine($"    <Cell N='PageHeight' V='{pageHeight}'/>");
            pageSb.AppendLine("    <Section N='Layer'>");
            for (int l = 0; l < layers.Length; l++)
            {
                pageSb.AppendLine($"      <Row IX='{l}'>");
                pageSb.AppendLine($"        <Cell N='Name' V='{layers[l]}'/>");
                pageSb.AppendLine("        <Cell N='Status' V='0'/>");
                pageSb.AppendLine("        <Cell N='Visible' V='1'/>");
                pageSb.AppendLine("      </Row>");
            }
            pageSb.AppendLine("    </Section>");
            pageSb.AppendLine("  </PageSheet>");

            pageSb.AppendLine("  <Shapes>");

            int nextShapeId = 1;
            var hostToShapeId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Output Device Shapes
            foreach (var n in nodes)
            {
                int shapeId = nextShapeId++;
                hostToShapeId[n.Device.Hostname] = shapeId;

                var master = _stencilManager.ResolveMaster(n.Device.StencilKey, options.StencilKeyToMasterMap, n.Device.Role);

                // Convert top-left (X, Y) to Visio bottom-left inches
                double pinX = (n.X - minX) + 2.5 + n.Width / 2.0;
                double pinY = pageHeight - ((n.Y - minY) + 2.5 + n.Height / 2.0);

                int layerIndex = GetRoleLayerIndex(n.Device.Role);

                string label = FormatLabel(options.LabelTemplate, n.Device);

                pageSb.AppendLine($"    <Shape ID='{shapeId}' Master='{master.Id}' Type='Shape' LineStyle='3' FillStyle='3' TextStyle='3'>");
                pageSb.AppendLine($"      <Cell N='PinX' V='{pinX:F3}'/>");
                pageSb.AppendLine($"      <Cell N='PinY' V='{pinY:F3}'/>");
                pageSb.AppendLine($"      <Cell N='Width' V='{n.Width:F3}'/>");
                pageSb.AppendLine($"      <Cell N='Height' V='{n.Height:F3}'/>");
                pageSb.AppendLine($"      <Cell N='LocPinX' V='{n.Width * 0.5:F3}' F='Width*0.5'/>");
                pageSb.AppendLine($"      <Cell N='LocPinY' V='{n.Height * 0.5:F3}' F='Height*0.5'/>");
                pageSb.AppendLine($"      <Cell N='FillForegnd' V='{master.FillColor}'/>");
                pageSb.AppendLine($"      <Cell N='LineColor' V='{master.LineColor}'/>");
                pageSb.AppendLine($"      <Cell N='LayerMember' V='{layerIndex}'/>");
                pageSb.AppendLine("      <Cell N='Rounding' V='0.08'/>");
                pageSb.AppendLine("      <Cell N='VerticalAlign' V='1'/>");
                pageSb.AppendLine($"      <Cell N='TxtWidth' V='{n.Width:F3}' F='Width*1'/>");
                pageSb.AppendLine($"      <Cell N='TxtHeight' V='{n.Height:F3}' F='Height*1'/>");
                pageSb.AppendLine($"      <Cell N='TxtPinX' V='{n.Width * 0.5:F3}' F='Width*0.5'/>");
                pageSb.AppendLine($"      <Cell N='TxtPinY' V='{n.Height * 0.5:F3}' F='Height*0.5'/>");
                pageSb.AppendLine($"      <Cell N='TxtLocPinX' V='{n.Width * 0.5:F3}' F='TxtWidth*0.5'/>");
                pageSb.AppendLine($"      <Cell N='TxtLocPinY' V='{n.Height * 0.5:F3}' F='TxtHeight*0.5'/>");
                pageSb.AppendLine("      <Cell N='TopMargin' V='0.04'/>");
                pageSb.AppendLine("      <Cell N='BottomMargin' V='0.04'/>");
                pageSb.AppendLine("      <Cell N='LeftMargin' V='0.05'/>");
                pageSb.AppendLine("      <Cell N='RightMargin' V='0.05'/>");
                pageSb.AppendLine("      <Cell N='Char.Size' V='0.11'/>");
                pageSb.AppendLine("      <Cell N='Char.Color' V='#FFFFFF'/>");

                // Explicit Geometry so shape is never drawn 1x1
                pageSb.AppendLine("      <Section N='Geometry' IX='0'>");
                pageSb.AppendLine("        <Cell N='NoFill' V='0'/>");
                pageSb.AppendLine("        <Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>");
                pageSb.AppendLine($"        <Row T='LineTo' IX='2'><Cell N='X' V='{n.Width:F3}' F='Width*1'/><Cell N='Y' V='0'/></Row>");
                pageSb.AppendLine($"        <Row T='LineTo' IX='3'><Cell N='X' V='{n.Width:F3}' F='Width*1'/><Cell N='Y' V='{n.Height:F3}' F='Height*1'/></Row>");
                pageSb.AppendLine($"        <Row T='LineTo' IX='4'><Cell N='X' V='0'/><Cell N='Y' V='{n.Height:F3}' F='Height*1'/></Row>");
                pageSb.AppendLine("        <Row T='LineTo' IX='5'><Cell N='X' V='0' F='Geometry1.X1'/><Cell N='Y' V='0' F='Geometry1.Y1'/></Row>");
                pageSb.AppendLine("      </Section>");
                pageSb.AppendLine("      <Section N='Paragraph'>");
                pageSb.AppendLine("        <Row IX='0'>");
                pageSb.AppendLine("          <Cell N='HorzAlign' V='1'/>");
                pageSb.AppendLine("          <Cell N='SpAfter' V='0.02'/>");
                pageSb.AppendLine("        </Row>");
                pageSb.AppendLine("      </Section>");

                // Connection points on the shape for glue
                pageSb.AppendLine("      <Section N='Connection'>");
                pageSb.AppendLine($"        <Row N='1' IX='0'><Cell N='X' V='{n.Width * 0.5:F3}' F='Width*0.5'/><Cell N='Y' V='0' F='Height*0'/><Cell N='DirX' V='0'/><Cell N='DirY' V='-1'/></Row>");
                pageSb.AppendLine($"        <Row N='2' IX='1'><Cell N='X' V='{n.Width * 0.5:F3}' F='Width*0.5'/><Cell N='Y' V='{n.Height:F3}' F='Height*1'/><Cell N='DirX' V='0'/><Cell N='DirY' V='1'/></Row>");
                pageSb.AppendLine($"        <Row N='3' IX='2'><Cell N='X' V='0' F='Width*0'/><Cell N='Y' V='{n.Height * 0.5:F3}' F='Height*0.5'/><Cell N='DirX' V='-1'/><Cell N='DirY' V='0'/></Row>");
                pageSb.AppendLine($"        <Row N='4' IX='3'><Cell N='X' V='{n.Width:F3}' F='Width*1'/><Cell N='Y' V='{n.Height * 0.5:F3}' F='Height*0.5'/><Cell N='DirX' V='1'/><Cell N='DirY' V='0'/></Row>");
                pageSb.AppendLine("      </Section>");

                // Shape Data (Properties) with explicit String Formulas so Visio preserves exact strings
                pageSb.AppendLine("      <Section N='Property'>");
                pageSb.AppendLine($"        <Row N='Hostname'><Cell N='Value' V='{Escape(n.Device.Hostname)}' F='&quot;{Escape(n.Device.Hostname)}&quot;'/><Cell N='Label' V='Hostname' F='&quot;Hostname&quot;'/><Cell N='Type' V='0'/></Row>");
                pageSb.AppendLine($"        <Row N='IP'><Cell N='Value' V='{Escape(n.Device.IpAddress ?? "")}' F='&quot;{Escape(n.Device.IpAddress ?? "")}&quot;'/><Cell N='Label' V='IP Address' F='&quot;IP Address&quot;'/><Cell N='Type' V='0'/></Row>");
                pageSb.AppendLine($"        <Row N='Model'><Cell N='Value' V='{Escape(n.Device.Model ?? "")}' F='&quot;{Escape(n.Device.Model ?? "")}&quot;'/><Cell N='Label' V='Model' F='&quot;Model&quot;'/><Cell N='Type' V='0'/></Row>");
                pageSb.AppendLine($"        <Row N='Role'><Cell N='Value' V='{Escape(n.Device.Role)}' F='&quot;{Escape(n.Device.Role)}&quot;'/><Cell N='Label' V='Role' F='&quot;Role&quot;'/><Cell N='Type' V='0'/></Row>");
                pageSb.AppendLine($"        <Row N='Location'><Cell N='Value' V='{Escape(n.Device.Location ?? "")}' F='&quot;{Escape(n.Device.Location ?? "")}&quot;'/><Cell N='Label' V='Location' F='&quot;Location&quot;'/><Cell N='Type' V='0'/></Row>");
                pageSb.AppendLine($"        <Row N='Serial'><Cell N='Value' V='{Escape(n.Device.SerialNumber ?? "")}' F='&quot;{Escape(n.Device.SerialNumber ?? "")}&quot;'/><Cell N='Label' V='Serial Number' F='&quot;Serial Number&quot;'/><Cell N='Type' V='0'/></Row>");
                pageSb.AppendLine("      </Section>");

                // Label Text
                pageSb.AppendLine($"      <Text>{Escape(label)}</Text>");
                pageSb.AppendLine("    </Shape>");
            }

            var connectsList = new List<(int ConnId, int SrcId, int DstId)>();

            // Output Glued Dynamic Connectors
            foreach (var rl in links)
            {
                if (!hostToShapeId.TryGetValue(rl.Link.Source, out int srcId) ||
                    !hostToShapeId.TryGetValue(rl.Link.Destination, out int dstId))
                    continue;

                int connId = nextShapeId++;
                connectsList.Add((connId, srcId, dstId));

                var srcNode = nodes.FirstOrDefault(n => n.Device.Hostname.Equals(rl.Link.Source, StringComparison.OrdinalIgnoreCase));
                var dstNode = nodes.FirstOrDefault(n => n.Device.Hostname.Equals(rl.Link.Destination, StringComparison.OrdinalIgnoreCase));

                double beginX = srcNode != null ? (srcNode.X - minX) + 2.5 + srcNode.Width / 2.0 : 0;
                double beginY = srcNode != null ? pageHeight - ((srcNode.Y - minY) + 2.5 + srcNode.Height / 2.0) : 0;
                double endX = dstNode != null ? (dstNode.X - minX) + 2.5 + dstNode.Width / 2.0 : 0;
                double endY = dstNode != null ? pageHeight - ((dstNode.Y - minY) + 2.5 + dstNode.Height / 2.0) : 0;

                string lineColor = rl.Link.IsFiber ? "#D4A800" : "#1A1A1A";
                string lineWeight = rl.Link.Type == LinkType.PortChannel ? "0.035" : "0.02";
                string linePattern = (rl.Link.Type == LinkType.AP || rl.Link.Type == LinkType.Printer) ? "2" : "1";
                int layerIdx = rl.Link.Type == LinkType.PortChannel ? 7 : (rl.Link.Type == LinkType.Uplink ? 6 : 8);

                pageSb.AppendLine($"    <Shape ID='{connId}' Master='10' Type='Shape' LineStyle='3' FillStyle='3' TextStyle='3'>");
                pageSb.AppendLine($"      <Cell N='BeginX' V='{beginX:F3}' F='PAR(PNT(Sheet.{srcId}!PinX,Sheet.{srcId}!PinY))'/>");
                pageSb.AppendLine($"      <Cell N='BeginY' V='{beginY:F3}' F='PAR(PNT(Sheet.{srcId}!PinX,Sheet.{srcId}!PinY))'/>");
                pageSb.AppendLine($"      <Cell N='EndX' V='{endX:F3}' F='PAR(PNT(Sheet.{dstId}!PinX,Sheet.{dstId}!PinY))'/>");
                pageSb.AppendLine($"      <Cell N='EndY' V='{endY:F3}' F='PAR(PNT(Sheet.{dstId}!PinX,Sheet.{dstId}!PinY))'/>");
                pageSb.AppendLine($"      <Cell N='BegTrigger' V='2' F='_XFTRIGGER(Sheet.{srcId}!EventXFMod)'/>");
                pageSb.AppendLine($"      <Cell N='EndTrigger' V='2' F='_XFTRIGGER(Sheet.{dstId}!EventXFMod)'/>");
                pageSb.AppendLine("      <Cell N='ConFixedCode' V='3'/>"); // Right-angle routing
                pageSb.AppendLine("      <Cell N='ShapeRouteStyle' V='1'/>");
                pageSb.AppendLine("      <Cell N='ObjType' V='2'/>"); // 1D connector
                pageSb.AppendLine($"      <Cell N='LineColor' V='{lineColor}'/>");
                pageSb.AppendLine($"      <Cell N='LineWeight' V='{lineWeight}'/>");
                pageSb.AppendLine($"      <Cell N='LinePattern' V='{linePattern}'/>");
                pageSb.AppendLine($"      <Cell N='LayerMember' V='{layerIdx}'/>");

                if (!string.IsNullOrEmpty(rl.MiddleLabel))
                {
                    pageSb.AppendLine($"      <Text>{Escape(rl.MiddleLabel)}</Text>");
                }
                else if (rl.Link.Type == LinkType.PortChannel)
                {
                    pageSb.AppendLine($"      <Text>Po{rl.Link.SourcePortChannelId ?? "1"}</Text>");
                }

                pageSb.AppendLine("    </Shape>");

                // Add source port label shape
                if (!string.IsNullOrEmpty(rl.SourcePort))
                {
                    int sPortId = nextShapeId++;
                    bool isVert = rl.Points.Count >= 2 && Math.Abs(rl.Points[1].X - rl.Points[0].X) < 0.05 && Math.Abs(rl.Points[1].Y - rl.Points[0].Y) >= 0.05;
                    double sPx = (rl.SourcePortPoint.X - minX) + 2.5;
                    double sPy = pageHeight - ((rl.SourcePortPoint.Y - minY) + 2.5);
                    double offX = sPx - beginX;
                    double offY = sPy - beginY;
                    string fPinX = FormatOffsetFormula(srcId, "PinX", offX);
                    string fPinY = FormatOffsetFormula(srcId, "PinY", offY);

                    string sAngleCell = "";
                    if (isVert)
                    {
                        if (rl.Points[1].Y < rl.Points[0].Y)
                            sAngleCell = "      <Cell N='Angle' V='1.5707963267948966' F='90 deg'/>\n";
                        else
                            sAngleCell = "      <Cell N='Angle' V='-1.5707963267948966' F='-90 deg'/>\n";
                    }

                    pageSb.AppendLine($"    <Shape ID='{sPortId}' Type='Shape' LineStyle='3' FillStyle='3' TextStyle='3'>");
                    pageSb.AppendLine($"      <Cell N='PinX' V='{Inv(sPx)}' F='{fPinX}'/>");
                    pageSb.AppendLine($"      <Cell N='PinY' V='{Inv(sPy)}' F='{fPinY}'/>");
                    if (!string.IsNullOrEmpty(sAngleCell))
                        pageSb.Append(sAngleCell);
                    pageSb.AppendLine("      <Cell N='Width' V='0.8'/>");
                    pageSb.AppendLine("      <Cell N='Height' V='0.17'/>");
                    pageSb.AppendLine("      <Cell N='LocPinX' V='0.4' F='Width*0.5'/>");
                    pageSb.AppendLine("      <Cell N='LocPinY' V='0.085' F='Height*0.5'/>");
                    pageSb.AppendLine("      <Cell N='LineColor' V='#B0BEC5'/>");
                    pageSb.AppendLine("      <Cell N='LineWeight' V='0.01'/>");
                    pageSb.AppendLine("      <Cell N='LinePattern' V='1'/>");
                    pageSb.AppendLine("      <Cell N='FillForegnd' V='#FFFFFF'/>");
                    pageSb.AppendLine("      <Cell N='FillPattern' V='1'/>");
                    pageSb.AppendLine("      <Cell N='Rounding' V='0.03'/>");
                    pageSb.AppendLine("      <Cell N='VerticalAlign' V='1'/>");
                    pageSb.AppendLine("      <Cell N='TxtWidth' V='0.8' F='Width*1'/>");
                    pageSb.AppendLine("      <Cell N='TxtHeight' V='0.17' F='Height*1'/>");
                    pageSb.AppendLine("      <Cell N='TxtPinX' V='0.4' F='Width*0.5'/>");
                    pageSb.AppendLine("      <Cell N='TxtPinY' V='0.085' F='Height*0.5'/>");
                    pageSb.AppendLine("      <Cell N='TxtLocPinX' V='0.4' F='TxtWidth*0.5'/>");
                    pageSb.AppendLine("      <Cell N='TxtLocPinY' V='0.085' F='TxtHeight*0.5'/>");
                    pageSb.AppendLine("      <Cell N='TopMargin' V='0'/>");
                    pageSb.AppendLine("      <Cell N='BottomMargin' V='0'/>");
                    pageSb.AppendLine("      <Cell N='LeftMargin' V='0.02'/>");
                    pageSb.AppendLine("      <Cell N='RightMargin' V='0.02'/>");
                    pageSb.AppendLine("      <Cell N='Char.Size' V='0.09'/>");
                    pageSb.AppendLine("      <Cell N='Char.Color' V='#263238'/>");
                    pageSb.AppendLine("      <Section N='Geometry' IX='0'>");
                    pageSb.AppendLine("        <Cell N='NoFill' V='0'/>");
                    pageSb.AppendLine("        <Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>");
                    pageSb.AppendLine("        <Row T='LineTo' IX='2'><Cell N='X' V='0.8' F='Width*1'/><Cell N='Y' V='0'/></Row>");
                    pageSb.AppendLine("        <Row T='LineTo' IX='3'><Cell N='X' V='0.8' F='Width*1'/><Cell N='Y' V='0.17' F='Height*1'/></Row>");
                    pageSb.AppendLine("        <Row T='LineTo' IX='4'><Cell N='X' V='0'/><Cell N='Y' V='0.17' F='Height*1'/></Row>");
                    pageSb.AppendLine("        <Row T='LineTo' IX='5'><Cell N='X' V='0' F='Geometry1.X1'/><Cell N='Y' V='0' F='Geometry1.Y1'/></Row>");
                    pageSb.AppendLine("      </Section>");
                    pageSb.AppendLine("      <Section N='Paragraph'>");
                    pageSb.AppendLine("        <Row IX='0'><Cell N='HorzAlign' V='1'/></Row>");
                    pageSb.AppendLine("      </Section>");
                    pageSb.AppendLine($"      <Text>{Escape(rl.SourcePort)}</Text>");
                    pageSb.AppendLine("    </Shape>");
                }

                // Add destination port label shape
                if (!string.IsNullOrEmpty(rl.DestinationPort))
                {
                    int dPortId = nextShapeId++;
                    bool isVert = rl.Points.Count >= 2 && Math.Abs(rl.Points[^1].X - rl.Points[^2].X) < 0.05 && Math.Abs(rl.Points[^1].Y - rl.Points[^2].Y) >= 0.05;
                    double dPx = (rl.DestinationPortPoint.X - minX) + 2.5;
                    double dPy = pageHeight - ((rl.DestinationPortPoint.Y - minY) + 2.5);
                    double dOffX = dPx - endX;
                    double dOffY = dPy - endY;
                    string fPinX = FormatOffsetFormula(dstId, "PinX", dOffX);
                    string fPinY = FormatOffsetFormula(dstId, "PinY", dOffY);

                    string dAngleCell = "";
                    if (isVert)
                    {
                        if (rl.Points[^1].Y < rl.Points[^2].Y)
                            dAngleCell = "      <Cell N='Angle' V='1.5707963267948966' F='90 deg'/>\n";
                        else
                            dAngleCell = "      <Cell N='Angle' V='-1.5707963267948966' F='-90 deg'/>\n";
                    }

                    pageSb.AppendLine($"    <Shape ID='{dPortId}' Type='Shape' LineStyle='3' FillStyle='3' TextStyle='3'>");
                    pageSb.AppendLine($"      <Cell N='PinX' V='{Inv(dPx)}' F='{fPinX}'/>");
                    pageSb.AppendLine($"      <Cell N='PinY' V='{Inv(dPy)}' F='{fPinY}'/>");
                    if (!string.IsNullOrEmpty(dAngleCell))
                        pageSb.Append(dAngleCell);
                    pageSb.AppendLine("      <Cell N='Width' V='0.8'/>");
                    pageSb.AppendLine("      <Cell N='Height' V='0.17'/>");
                    pageSb.AppendLine("      <Cell N='LocPinX' V='0.4' F='Width*0.5'/>");
                    pageSb.AppendLine("      <Cell N='LocPinY' V='0.085' F='Height*0.5'/>");
                    pageSb.AppendLine("      <Cell N='LineColor' V='#B0BEC5'/>");
                    pageSb.AppendLine("      <Cell N='LineWeight' V='0.01'/>");
                    pageSb.AppendLine("      <Cell N='LinePattern' V='1'/>");
                    pageSb.AppendLine("      <Cell N='FillForegnd' V='#FFFFFF'/>");
                    pageSb.AppendLine("      <Cell N='FillPattern' V='1'/>");
                    pageSb.AppendLine("      <Cell N='Rounding' V='0.03'/>");
                    pageSb.AppendLine("      <Cell N='VerticalAlign' V='1'/>");
                    pageSb.AppendLine("      <Cell N='TxtWidth' V='0.8' F='Width*1'/>");
                    pageSb.AppendLine("      <Cell N='TxtHeight' V='0.17' F='Height*1'/>");
                    pageSb.AppendLine("      <Cell N='TxtPinX' V='0.4' F='Width*0.5'/>");
                    pageSb.AppendLine("      <Cell N='TxtPinY' V='0.085' F='Height*0.5'/>");
                    pageSb.AppendLine("      <Cell N='TxtLocPinX' V='0.4' F='TxtWidth*0.5'/>");
                    pageSb.AppendLine("      <Cell N='TxtLocPinY' V='0.085' F='TxtHeight*0.5'/>");
                    pageSb.AppendLine("      <Cell N='TopMargin' V='0'/>");
                    pageSb.AppendLine("      <Cell N='BottomMargin' V='0'/>");
                    pageSb.AppendLine("      <Cell N='LeftMargin' V='0.02'/>");
                    pageSb.AppendLine("      <Cell N='RightMargin' V='0.02'/>");
                    pageSb.AppendLine("      <Cell N='Char.Size' V='0.09'/>");
                    pageSb.AppendLine("      <Cell N='Char.Color' V='#263238'/>");
                    pageSb.AppendLine("      <Section N='Geometry' IX='0'>");
                    pageSb.AppendLine("        <Cell N='NoFill' V='0'/>");
                    pageSb.AppendLine("        <Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>");
                    pageSb.AppendLine("        <Row T='LineTo' IX='2'><Cell N='X' V='0.8' F='Width*1'/><Cell N='Y' V='0'/></Row>");
                    pageSb.AppendLine("        <Row T='LineTo' IX='3'><Cell N='X' V='0.8' F='Width*1'/><Cell N='Y' V='0.17' F='Height*1'/></Row>");
                    pageSb.AppendLine("        <Row T='LineTo' IX='4'><Cell N='X' V='0'/><Cell N='Y' V='0.17' F='Height*1'/></Row>");
                    pageSb.AppendLine("        <Row T='LineTo' IX='5'><Cell N='X' V='0' F='Geometry1.X1'/><Cell N='Y' V='0' F='Geometry1.Y1'/></Row>");
                    pageSb.AppendLine("      </Section>");
                    pageSb.AppendLine("      <Section N='Paragraph'>");
                    pageSb.AppendLine("        <Row IX='0'><Cell N='HorzAlign' V='1'/></Row>");
                    pageSb.AppendLine("      </Section>");
                    pageSb.AppendLine($"      <Text>{Escape(rl.DestinationPort)}</Text>");
                    pageSb.AppendLine("    </Shape>");
                }
            }

            pageSb.AppendLine("  </Shapes>");

            // Output Glued Connects table
            pageSb.AppendLine("  <Connects>");
            foreach (var conn in connectsList)
            {
                pageSb.AppendLine($"    <Connect FromSheet='{conn.ConnId}' FromCell='BeginX' FromPart='9' ToSheet='{conn.SrcId}' ToCell='PinX' ToPart='3'/>");
                pageSb.AppendLine($"    <Connect FromSheet='{conn.ConnId}' FromCell='EndX' FromPart='12' ToSheet='{conn.DstId}' ToCell='PinX' ToPart='3'/>");
            }
            pageSb.AppendLine("  </Connects>");

            pageSb.AppendLine("</PageContents>");
            AddZipEntry(archive, $"visio/pages/page{pIdx + 1}.xml", pageSb.ToString());
        }
    }

    private int GetRoleLayerIndex(string role)
    {
        return role switch
        {
            "Core" => 0,
            "Distribution" => 1,
            "Access" => 2,
            "AP" => 3,
            "Printer" => 4,
            _ => 5
        };
    }

    private static string Inv(double val, string format = "F3") =>
        val.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatOffsetFormula(int shapeId, string cellName, double offset)
    {
        if (Math.Abs(offset) < 0.0001)
            return $"Sheet.{shapeId}!{cellName}";
        string val = Math.Abs(offset).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        if (offset > 0)
            return $"Sheet.{shapeId}!{cellName}+({val})";
        return $"Sheet.{shapeId}!{cellName}-({val})";
    }

    private string FormatLabel(string template, Device d)
    {
        var result = template
            .Replace("{hostname}", d.Hostname)
            .Replace("{mgmt_ip}", d.IpAddress ?? "")
            .Replace("{model}", d.Model ?? "")
            .Replace("{role}", d.Role)
            .Replace("{location}", d.Location ?? "");

        return result.Trim();
    }

    private string Escape(string val)
    {
        if (string.IsNullOrEmpty(val)) return "";
        return SecurityElement.Escape(val) ?? "";
    }

    private void AddZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    public void Export(TopologyGraph graph, DiagramLayout layout, string outputPath, VisioExportOptions? options = null)
    {
        options ??= VisioExportOptions.Default;

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var zipStream = new FileStream(outputPath, FileMode.Create);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        var masters = _stencilManager.Masters.ToList();

        // 1 page
        int pageCount = 1;

        // 2. Write [Content_Types].xml
        WriteContentTypes(archive, pageCount, masters.Count);

        // 3. Write _rels/.rels
        WriteRootRels(archive);

        // 4. Write visio/_rels/document.xml.rels
        WriteDocumentRels(archive);

        // 5. Write visio/document.xml
        WriteDocumentXml(archive);

        // 6. Write visio/windows.xml
        WriteWindowsXml(archive);

        // 7. Write Masters
        WriteMasters(archive, masters);

        // 8. Write Pages & Page Contents
        WriteDiagramPages(archive, graph, layout, masters, options);

        _logger.LogInformation("Exported Visio diagram to {Path} (1 page)", outputPath);
    }

    private void WriteDiagramPages(ZipArchive archive, TopologyGraph graph, DiagramLayout layout, List<StencilMasterDefinition> masters, VisioExportOptions options)
    {
        // 1. visio/pages/_rels/pages.xml.rels
        var relsSb = new StringBuilder();
        relsSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
        relsSb.AppendLine("<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>");
        relsSb.AppendLine("  <Relationship Id='rId1' Type='http://schemas.microsoft.com/visio/2010/relationships/page' Target='page1.xml' />");
        relsSb.AppendLine("</Relationships>");
        AddZipEntry(archive, "visio/pages/_rels/pages.xml.rels", relsSb.ToString());

        // 2. visio/pages/pages.xml
        var pagesSb = new StringBuilder();
        pagesSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
        pagesSb.AppendLine("<Pages xmlns='http://schemas.microsoft.com/office/visio/2012/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'>");
        pagesSb.AppendLine("  <Page ID='0' NameU='Network Topology' Name='Network Topology' IsCustomName='1'>");
        pagesSb.AppendLine("    <PageSheet LineStyle='0' FillStyle='0' TextStyle='0'>");
        pagesSb.AppendLine("      <Cell N='DrawingResizeType' V='1'/>");
        pagesSb.AppendLine("    </PageSheet>");
        pagesSb.AppendLine("    <Rel r:id='rId1'/>");
        pagesSb.AppendLine("  </Page>");
        pagesSb.AppendLine("</Pages>");
        AddZipEntry(archive, "visio/pages/pages.xml", pagesSb.ToString());

        // 3. visio/pages/page1.xml
        var layers = new[] { "Core", "Distribution", "Access", "AP", "Printer", "External", "Uplink", "PortChannel", "Leaf" };
        double marginX = 1.0;
        double marginY = 1.0;

        // Calculate diagram bounds directly from nodes and links without site containers
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        if (layout.Nodes.Values.Any())
        {
            foreach (var n in layout.Nodes.Values)
            {
                minX = Math.Min(minX, n.X);
                minY = Math.Min(minY, n.Y);
                maxX = Math.Max(maxX, n.Right);
                maxY = Math.Max(maxY, n.Bottom);
            }
        }
        else
        {
            minX = 0; minY = 0; maxX = 10; maxY = 10;
        }

        foreach (var l in layout.Links)
        {
            foreach (var pt in l.Points)
            {
                minX = Math.Min(minX, pt.X);
                minY = Math.Min(minY, pt.Y);
                maxX = Math.Max(maxX, pt.X);
                maxY = Math.Max(maxY, pt.Y);
            }
            if (!string.IsNullOrEmpty(l.APort))
            {
                double aW = l.AIsVertical ? 0.17 : 0.8;
                double aH = l.AIsVertical ? 0.8 : 0.17;
                minX = Math.Min(minX, l.ALabel.X);
                minY = Math.Min(minY, l.ALabel.Y);
                maxX = Math.Max(maxX, l.ALabel.X + aW);
                maxY = Math.Max(maxY, l.ALabel.Y + aH);
            }
            if (!string.IsNullOrEmpty(l.BPort))
            {
                double bW = l.BIsVertical ? 0.17 : 0.8;
                double bH = l.BIsVertical ? 0.8 : 0.17;
                minX = Math.Min(minX, l.BLabel.X);
                minY = Math.Min(minY, l.BLabel.Y);
                maxX = Math.Max(maxX, l.BLabel.X + bW);
                maxY = Math.Max(maxY, l.BLabel.Y + bH);
            }
        }

        foreach (var h in layout.Highlights)
        {
            minX = Math.Min(minX, h.X);
            minY = Math.Min(minY, h.Y);
            maxX = Math.Max(maxX, h.X + h.W);
            maxY = Math.Max(maxY, h.Y + h.H);
        }

        double pageWidth = Math.Max(17.0, (maxX - minX) + 2 * marginX);
        double pageHeight = Math.Max(11.0, (maxY - minY) + 2 * marginY);

        var pageSb = new StringBuilder();
        pageSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
        pageSb.AppendLine("<PageContents xmlns='http://schemas.microsoft.com/office/visio/2012/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships' xml:space='preserve'>");

        // PageSheet
        pageSb.AppendLine("  <PageSheet LineStyle='0' FillStyle='0' TextStyle='0'>");
        pageSb.AppendLine($"    <Cell N='PageWidth' V='{Inv(pageWidth)}'/>");
        pageSb.AppendLine($"    <Cell N='PageHeight' V='{Inv(pageHeight)}'/>");
        pageSb.AppendLine("    <Section N='Layer'>");
        for (int l = 0; l < layers.Length; l++)
        {
            pageSb.AppendLine($"      <Row IX='{l}'>");
            pageSb.AppendLine($"        <Cell N='Name' V='{layers[l]}'/>");
            pageSb.AppendLine("        <Cell N='Status' V='0'/>");
            pageSb.AppendLine("        <Cell N='Visible' V='1'/>");
            pageSb.AppendLine("      </Row>");
        }
        pageSb.AppendLine("    </Section>");
        pageSb.AppendLine("  </PageSheet>");

        pageSb.AppendLine("  <Shapes>");

        int nextShapeId = 1;
        var devicesByKey = graph.Nodes.Values
            .GroupBy(d => HostKey.Of(d.Hostname))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var nodeShapeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nodeCenterMap = new Dictionary<int, (double PinX, double PinY)>();

        // 1. Devices (Nodes) - emitted first to establish Shape IDs for glue & formula attachments
        foreach (var n in layout.Nodes.Values)
        {
            int shapeId = nextShapeId++;
            devicesByKey.TryGetValue(HostKey.Of(n.Id), out var dev);
            var master = _stencilManager.ResolveMaster(dev?.StencilKey, options?.StencilKeyToMasterMap, n.Role);

            double switchH = Math.Min(n.H * 0.65, 0.85);
            double pinX = marginX + (n.X - minX) + n.W / 2.0;
            double pinY = pageHeight - (marginY + (n.Y - minY) + switchH / 2.0);

            nodeShapeMap[n.Id] = shapeId;
            nodeShapeMap[HostKey.Of(n.Id)] = shapeId;
            if (dev != null)
            {
                nodeShapeMap[dev.Hostname] = shapeId;
            }

            nodeCenterMap[shapeId] = (pinX, pinY);

            int layerIdx = GetRoleLayerIndex(n.Role);
            string modelText = !string.IsNullOrWhiteSpace(dev?.Model) ? dev.Model : (!string.IsNullOrWhiteSpace(n.Role) ? n.Role : n.Id);

            pageSb.AppendLine($"    <Shape ID='{shapeId}' Master='{master.Id}' Type='Shape' LineStyle='3' FillStyle='3' TextStyle='3'>");
            pageSb.AppendLine($"      <Cell N='PinX' V='{Inv(pinX)}'/>");
            pageSb.AppendLine($"      <Cell N='PinY' V='{Inv(pinY)}'/>");
            pageSb.AppendLine($"      <Cell N='Width' V='{Inv(n.W)}'/>");
            pageSb.AppendLine($"      <Cell N='Height' V='{Inv(switchH)}'/>");
            pageSb.AppendLine($"      <Cell N='LocPinX' V='{Inv(n.W * 0.5)}' F='Width*0.5'/>");
            pageSb.AppendLine($"      <Cell N='LocPinY' V='{Inv(switchH * 0.5)}' F='Height*0.5'/>");
            pageSb.AppendLine($"      <Cell N='FillForegnd' V='{master.FillColor}'/>");
            pageSb.AppendLine($"      <Cell N='LineColor' V='{master.LineColor}'/>");
            pageSb.AppendLine($"      <Cell N='LayerMember' V='{layerIdx}'/>");
            pageSb.AppendLine("      <Cell N='Rounding' V='0.08'/>");
            pageSb.AppendLine("      <Cell N='VerticalAlign' V='1'/>");
            pageSb.AppendLine($"      <Cell N='TxtWidth' V='{Inv(n.W)}' F='Width*1'/>");
            pageSb.AppendLine($"      <Cell N='TxtHeight' V='{Inv(switchH)}' F='Height*1'/>");
            pageSb.AppendLine($"      <Cell N='TxtPinX' V='{Inv(n.W * 0.5)}' F='Width*0.5'/>");
            pageSb.AppendLine($"      <Cell N='TxtPinY' V='{Inv(switchH * 0.5)}' F='Height*0.5'/>");
            pageSb.AppendLine($"      <Cell N='TxtLocPinX' V='{Inv(n.W * 0.5)}' F='TxtWidth*0.5'/>");
            pageSb.AppendLine($"      <Cell N='TxtLocPinY' V='{Inv(switchH * 0.5)}' F='TxtHeight*0.5'/>");
            pageSb.AppendLine("      <Cell N='TopMargin' V='0.04'/>");
            pageSb.AppendLine("      <Cell N='BottomMargin' V='0.04'/>");
            pageSb.AppendLine("      <Cell N='LeftMargin' V='0.05'/>");
            pageSb.AppendLine("      <Cell N='RightMargin' V='0.05'/>");
            pageSb.AppendLine("      <Cell N='Char.Size' V='0.12'/>");
            pageSb.AppendLine("      <Cell N='Char.Style' V='1'/>");
            pageSb.AppendLine("      <Cell N='Char.Color' V='#FFFFFF'/>");
            pageSb.AppendLine("      <Section N='Geometry' IX='0'>");
            pageSb.AppendLine("        <Cell N='NoFill' V='0'/>");
            pageSb.AppendLine("        <Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>");
            pageSb.AppendLine($"        <Row T='LineTo' IX='2'><Cell N='X' V='{Inv(n.W)}' F='Width*1'/><Cell N='Y' V='0'/></Row>");
            pageSb.AppendLine($"        <Row T='LineTo' IX='3'><Cell N='X' V='{Inv(n.W)}' F='Width*1'/><Cell N='Y' V='{Inv(switchH)}' F='Height*1'/></Row>");
            pageSb.AppendLine($"        <Row T='LineTo' IX='4'><Cell N='X' V='0'/><Cell N='Y' V='{Inv(switchH)}' F='Height*1'/></Row>");
            pageSb.AppendLine("        <Row T='LineTo' IX='5'><Cell N='X' V='0' F='Geometry1.X1'/><Cell N='Y' V='0' F='Geometry1.Y1'/></Row>");
            pageSb.AppendLine("      </Section>");
            pageSb.AppendLine("      <Section N='Paragraph'>");
            pageSb.AppendLine("        <Row IX='0'>");
            pageSb.AppendLine("          <Cell N='HorzAlign' V='1'/>");
            pageSb.AppendLine("        </Row>");
            pageSb.AppendLine("      </Section>");

            // Connection points on the shape for glue
            pageSb.AppendLine("      <Section N='Connection'>");
            pageSb.AppendLine($"        <Row N='1' IX='0'><Cell N='X' V='{Inv(n.W * 0.5)}' F='Width*0.5'/><Cell N='Y' V='0' F='Height*0'/><Cell N='DirX' V='0'/><Cell N='DirY' V='-1'/></Row>");
            pageSb.AppendLine($"        <Row N='2' IX='1'><Cell N='X' V='{Inv(n.W * 0.5)}' F='Width*0.5'/><Cell N='Y' V='{Inv(switchH)}' F='Height*1'/><Cell N='DirX' V='0'/><Cell N='DirY' V='1'/></Row>");
            pageSb.AppendLine($"        <Row N='3' IX='2'><Cell N='X' V='0' F='Width*0'/><Cell N='Y' V='{Inv(switchH * 0.5)}' F='Height*0.5'/><Cell N='DirX' V='-1'/><Cell N='DirY' V='0'/></Row>");
            pageSb.AppendLine($"        <Row N='4' IX='3'><Cell N='X' V='{Inv(n.W)}' F='Width*1'/><Cell N='Y' V='{Inv(switchH * 0.5)}' F='Height*0.5'/><Cell N='DirX' V='1'/><Cell N='DirY' V='0'/></Row>");
            pageSb.AppendLine("      </Section>");

            pageSb.AppendLine("      <Section N='Property'>");
            pageSb.AppendLine($"        <Row N='Hostname'><Cell N='Value' V='{Escape(dev?.Hostname ?? n.Id)}' F='&quot;{Escape(dev?.Hostname ?? n.Id)}&quot;'/><Cell N='Label' V='Hostname' F='&quot;Hostname&quot;'/><Cell N='Type' V='0'/></Row>");
            pageSb.AppendLine($"        <Row N='IP'><Cell N='Value' V='{Escape(dev?.IpAddress ?? "")}' F='&quot;{Escape(dev?.IpAddress ?? "")}&quot;'/><Cell N='Label' V='IP Address' F='&quot;IP Address&quot;'/><Cell N='Type' V='0'/></Row>");
            pageSb.AppendLine($"        <Row N='Model'><Cell N='Value' V='{Escape(dev?.Model ?? "")}' F='&quot;{Escape(dev?.Model ?? "")}&quot;'/><Cell N='Label' V='Model' F='&quot;Model&quot;'/><Cell N='Type' V='0'/></Row>");
            pageSb.AppendLine($"        <Row N='Role'><Cell N='Value' V='{Escape(n.Role)}' F='&quot;{Escape(n.Role)}&quot;'/><Cell N='Label' V='Role' F='&quot;Role&quot;'/><Cell N='Type' V='0'/></Row>");
            pageSb.AppendLine($"        <Row N='Location'><Cell N='Value' V='{Escape(dev?.Location ?? n.Location ?? "")}' F='&quot;{Escape(dev?.Location ?? n.Location ?? "")}&quot;'/><Cell N='Label' V='Location' F='&quot;Location&quot;'/><Cell N='Type' V='0'/></Row>");
            pageSb.AppendLine($"        <Row N='Serial'><Cell N='Value' V='{Escape(dev?.SerialNumber ?? "")}' F='&quot;{Escape(dev?.SerialNumber ?? "")}&quot;'/><Cell N='Label' V='Serial Number' F='&quot;Serial Number&quot;'/><Cell N='Type' V='0'/></Row>");
            pageSb.AppendLine("      </Section>");

            pageSb.AppendLine($"      <Text>{Escape(modelText)}</Text>");
            pageSb.AppendLine("    </Shape>");

            // Companion Under-Shape Label: Hostname and IP in canvas space below switch
            int labelShapeId = nextShapeId++;
            double nodeLabelH = 0.40;
            double nodeLabelW = n.W;
            double labelOffsetY = (switchH * 0.5) + (nodeLabelH * 0.5) + 0.05;
            double labelPinY = pinY - labelOffsetY;
            string fLabelPinX = $"Sheet.{shapeId}!PinX";
            string fLabelPinY = $"Sheet.{shapeId}!PinY-({Inv(labelOffsetY)})";

            pageSb.AppendLine($"    <Shape ID='{labelShapeId}' Type='Shape' LineStyle='3' FillStyle='3' TextStyle='3'>");
            pageSb.AppendLine($"      <Cell N='PinX' V='{Inv(pinX)}' F='{fLabelPinX}'/>");
            pageSb.AppendLine($"      <Cell N='PinY' V='{Inv(labelPinY)}' F='{fLabelPinY}'/>");
            pageSb.AppendLine($"      <Cell N='Width' V='{Inv(nodeLabelW)}'/>");
            pageSb.AppendLine($"      <Cell N='Height' V='{Inv(nodeLabelH)}'/>");
            pageSb.AppendLine($"      <Cell N='LocPinX' V='{Inv(nodeLabelW * 0.5)}' F='Width*0.5'/>");
            pageSb.AppendLine($"      <Cell N='LocPinY' V='{Inv(nodeLabelH * 0.5)}' F='Height*0.5'/>");
            pageSb.AppendLine("      <Cell N='LinePattern' V='0'/>");
            pageSb.AppendLine("      <Cell N='FillPattern' V='0'/>");
            pageSb.AppendLine($"      <Cell N='LayerMember' V='{layerIdx}'/>");
            pageSb.AppendLine("      <Cell N='VerticalAlign' V='1'/>");
            pageSb.AppendLine($"      <Cell N='TxtWidth' V='{Inv(nodeLabelW)}' F='Width*1'/>");
            pageSb.AppendLine($"      <Cell N='TxtHeight' V='{Inv(nodeLabelH)}' F='Height*1'/>");
            pageSb.AppendLine($"      <Cell N='TxtPinX' V='{Inv(nodeLabelW * 0.5)}' F='Width*0.5'/>");
            pageSb.AppendLine($"      <Cell N='TxtPinY' V='{Inv(nodeLabelH * 0.5)}' F='Height*0.5'/>");
            pageSb.AppendLine($"      <Cell N='TxtLocPinX' V='{Inv(nodeLabelW * 0.5)}' F='TxtWidth*0.5'/>");
            pageSb.AppendLine($"      <Cell N='TxtLocPinY' V='{Inv(nodeLabelH * 0.5)}' F='TxtHeight*0.5'/>");
            pageSb.AppendLine("      <Cell N='TopMargin' V='0'/>");
            pageSb.AppendLine("      <Cell N='BottomMargin' V='0'/>");
            pageSb.AppendLine("      <Cell N='LeftMargin' V='0.02'/>");
            pageSb.AppendLine("      <Cell N='RightMargin' V='0.02'/>");
            pageSb.AppendLine("      <Section N='Paragraph'>");
            pageSb.AppendLine("        <Row IX='0'><Cell N='HorzAlign' V='1'/><Cell N='SpAfter' V='0.01'/></Row>");
            pageSb.AppendLine("        <Row IX='1'><Cell N='HorzAlign' V='1'/></Row>");
            pageSb.AppendLine("      </Section>");
            pageSb.AppendLine("      <Section N='Character'>");
            pageSb.AppendLine("        <Row IX='0'><Cell N='Char.Size' V='0.11'/><Cell N='Char.Style' V='1'/><Cell N='Char.Color' V='#000000'/></Row>");
            pageSb.AppendLine("        <Row IX='1'><Cell N='Char.Size' V='0.095'/><Cell N='Char.Style' V='1'/><Cell N='Char.Color' V='#222222'/></Row>");
            pageSb.AppendLine("      </Section>");
            pageSb.AppendLine("      <Section N='Geometry' IX='0'>");
            pageSb.AppendLine("        <Cell N='NoFill' V='1'/>");
            pageSb.AppendLine("        <Cell N='NoLine' V='1'/>");
            pageSb.AppendLine("        <Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>");
            pageSb.AppendLine($"        <Row T='LineTo' IX='2'><Cell N='X' V='{Inv(nodeLabelW)}' F='Width*1'/><Cell N='Y' V='0'/></Row>");
            pageSb.AppendLine($"        <Row T='LineTo' IX='3'><Cell N='X' V='{Inv(nodeLabelW)}' F='Width*1'/><Cell N='Y' V='{Inv(nodeLabelH)}' F='Height*1'/></Row>");
            pageSb.AppendLine($"        <Row T='LineTo' IX='4'><Cell N='X' V='0'/><Cell N='Y' V='{Inv(nodeLabelH)}' F='Height*1'/></Row>");
            pageSb.AppendLine("        <Row T='LineTo' IX='5'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>");
            pageSb.AppendLine("      </Section>");
            pageSb.AppendLine($"      <Text><cp IX='0'/>{Escape(dev?.Hostname ?? n.Id)}&#xA;<cp IX='1'/>{Escape(dev?.IpAddress ?? "")}</Text>");
            pageSb.AppendLine("    </Shape>");
        }

        // 2. Native Editable Dynamic Connectors
        var connectsList = new List<(int ConnId, int SrcId, int DstId)>();
        foreach (var l in layout.Links)
        {
            if (l.Points.Count < 2) continue;

            int connId = nextShapeId++;
            nodeShapeMap.TryGetValue(l.A, out int srcId);
            nodeShapeMap.TryGetValue(l.B, out int dstId);
            if (srcId > 0 && dstId > 0)
            {
                connectsList.Add((connId, srcId, dstId));
            }

            double beginX = marginX + (l.Points[0].X - minX);
            double beginY = pageHeight - (marginY + (l.Points[0].Y - minY));
            double endX = marginX + (l.Points[^1].X - minX);
            double endY = pageHeight - (marginY + (l.Points[^1].Y - minY));

            double widthVal = endX - beginX;
            double heightVal = endY - beginY;
            double pinXVal = (beginX + endX) / 2.0;
            double pinYVal = (beginY + endY) / 2.0;

            string lineColor = l.IsFiber ? "#D4A800" : "#1A1A1A";
            string lineWeight = l.Kind == "PortChannel" ? "0.035" : "0.02";
            string linePattern = (l.Kind is "AP" or "Printer") ? "2" : "1";
            int layerIdx = l.Kind == "PortChannel" ? 7 : (l.Kind == "Uplink" ? 6 : 8);

            string triggerBeg = srcId > 0 ? $" F='_XFTRIGGER(Sheet.{srcId}!EventXFMod)'" : "";
            string triggerEnd = dstId > 0 ? $" F='_XFTRIGGER(Sheet.{dstId}!EventXFMod)'" : "";

            pageSb.AppendLine($"    <Shape ID='{connId}' NameU='Dynamic connector' Name='Dynamic connector' Type='Shape' Master='10' LineStyle='3' FillStyle='3' TextStyle='3'>");
            pageSb.AppendLine($"      <Cell N='PinX' V='{Inv(pinXVal)}' F='Inh'/>");
            pageSb.AppendLine($"      <Cell N='PinY' V='{Inv(pinYVal)}' F='Inh'/>");
            pageSb.AppendLine($"      <Cell N='Width' V='{Inv(widthVal)}' F='GUARD(EndX-BeginX)'/>");
            pageSb.AppendLine($"      <Cell N='Height' V='{Inv(heightVal)}' F='GUARD(EndY-BeginY)'/>");
            pageSb.AppendLine($"      <Cell N='LocPinX' V='{Inv(widthVal * 0.5)}' F='Inh'/>");
            pageSb.AppendLine($"      <Cell N='LocPinY' V='{Inv(heightVal * 0.5)}' F='Inh'/>");
            pageSb.AppendLine($"      <Cell N='BeginX' V='{Inv(beginX)}' F='_WALKGLUE(BegTrigger,EndTrigger,WalkPreference)'/>");
            pageSb.AppendLine($"      <Cell N='BeginY' V='{Inv(beginY)}' F='_WALKGLUE(BegTrigger,EndTrigger,WalkPreference)'/>");
            pageSb.AppendLine($"      <Cell N='EndX' V='{Inv(endX)}' F='_WALKGLUE(EndTrigger,BegTrigger,WalkPreference)'/>");
            pageSb.AppendLine($"      <Cell N='EndY' V='{Inv(endY)}' F='_WALKGLUE(EndTrigger,BegTrigger,WalkPreference)'/>");
            pageSb.AppendLine($"      <Cell N='BegTrigger' V='2'{triggerBeg}/>");
            pageSb.AppendLine($"      <Cell N='EndTrigger' V='2'{triggerEnd}/>");
            pageSb.AppendLine("      <Cell N='ConFixedCode' V='6'/>");
            pageSb.AppendLine("      <Cell N='ShapeRouteStyle' V='1'/>");
            pageSb.AppendLine("      <Cell N='ObjType' V='2'/>");
            pageSb.AppendLine($"      <Cell N='LineColor' V='{lineColor}'/>");
            pageSb.AppendLine($"      <Cell N='LineWeight' V='{lineWeight}'/>");
            pageSb.AppendLine($"      <Cell N='LinePattern' V='{linePattern}'/>");
            pageSb.AppendLine($"      <Cell N='LayerMember' V='{layerIdx}'/>");

            // Geometry section representing the right-angle dogleg routing
            pageSb.AppendLine("      <Section N='Geometry' IX='0'>");
            pageSb.AppendLine("        <Cell N='NoFill' V='1'/>");
            pageSb.AppendLine("        <Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>");
            for (int pIdx = 1; pIdx < l.Points.Count; pIdx++)
            {
                double pPageX = marginX + (l.Points[pIdx].X - minX);
                double pPageY = pageHeight - (marginY + (l.Points[pIdx].Y - minY));
                double relX = pPageX - beginX;
                double relY = pPageY - beginY;
                pageSb.AppendLine($"        <Row T='LineTo' IX='{pIdx + 1}'><Cell N='X' V='{Inv(relX)}'/><Cell N='Y' V='{Inv(relY)}'/></Row>");
            }
            pageSb.AppendLine("      </Section>");

            if (!string.IsNullOrEmpty(l.PortChannelId))
            {
                pageSb.AppendLine($"      <Text>Po{Escape(l.PortChannelId)}</Text>");
            }

            pageSb.AppendLine("    </Shape>");
        }

        // 3. Highlights (PortChannel ovals)
        foreach (var h in layout.Highlights)
        {
            int hlShapeId = nextShapeId++;
            double hlPinX = marginX + (h.X - minX) + h.W / 2.0;
            double hlPinY = pageHeight - (marginY + (h.Y - minY) + h.H / 2.0);

            pageSb.AppendLine($"    <Shape ID='{hlShapeId}' Type='Shape' LineStyle='3' FillStyle='3' TextStyle='3'>");
            pageSb.AppendLine($"      <Cell N='PinX' V='{Inv(hlPinX)}'/>");
            pageSb.AppendLine($"      <Cell N='PinY' V='{Inv(hlPinY)}'/>");
            pageSb.AppendLine($"      <Cell N='Width' V='{Inv(h.W)}'/>");
            pageSb.AppendLine($"      <Cell N='Height' V='{Inv(h.H)}'/>");
            pageSb.AppendLine($"      <Cell N='LocPinX' V='{Inv(h.W * 0.5)}' F='Width*0.5'/>");
            pageSb.AppendLine($"      <Cell N='LocPinY' V='{Inv(h.H * 0.5)}' F='Height*0.5'/>");
            pageSb.AppendLine("      <Cell N='LineColor' V='#DC6464'/>");
            pageSb.AppendLine("      <Cell N='LineWeight' V='0.015'/>");
            pageSb.AppendLine("      <Cell N='FillForegnd' V='#F09696'/>");
            pageSb.AppendLine("      <Cell N='FillPattern' V='1'/>");
            pageSb.AppendLine("      <Cell N='FillBkgndTrans' V='0.7'/>");
            pageSb.AppendLine("      <Cell N='Rounding' V='0.16'/>");
            pageSb.AppendLine("      <Section N='Geometry' IX='0'>");
            pageSb.AppendLine("        <Cell N='NoFill' V='0'/>");
            pageSb.AppendLine("        <Row T='Ellipse' IX='1'>");
            pageSb.AppendLine($"          <Cell N='X' V='{Inv(h.W * 0.5)}' F='Width*0.5'/>");
            pageSb.AppendLine($"          <Cell N='Y' V='{Inv(h.H * 0.5)}' F='Height*0.5'/>");
            pageSb.AppendLine($"          <Cell N='A' V='{Inv(h.W)}' F='Width*1'/>");
            pageSb.AppendLine($"          <Cell N='B' V='{Inv(h.H * 0.5)}' F='Height*0.5'/>");
            pageSb.AppendLine($"          <Cell N='C' V='{Inv(h.W * 0.5)}' F='Width*0.5'/>");
            pageSb.AppendLine($"          <Cell N='D' V='{Inv(h.H)}' F='Height*1'/>");
            pageSb.AppendLine("        </Row>");
            pageSb.AppendLine("      </Section>");
            pageSb.AppendLine("      <Cell N='Char.Size' V='0.08'/>");
            pageSb.AppendLine("      <Cell N='Char.Color' V='#C0392B'/>");
            pageSb.AppendLine("      <Cell N='VerticalAlign' V='1'/>");
            pageSb.AppendLine("      <Cell N='TopMargin' V='0'/><Cell N='BottomMargin' V='0'/>");
            pageSb.AppendLine("      <Section N='Paragraph'><Row IX='0'><Cell N='HorzAlign' V='1'/></Row></Section>");
            pageSb.AppendLine($"      <Text>{Escape(h.Text)}</Text>");
            pageSb.AppendLine("    </Shape>");
        }

        // 4. Port labels (formula-linked to parent device so dragging a node moves its port tags)
        double labelW = 0.8;
        double labelH = 0.17;
        foreach (var l in layout.Links)
        {
            if (!string.IsNullOrWhiteSpace(l.APort))
            {
                int aId = nextShapeId++;
                double curAW = l.AIsVertical ? labelH : labelW;
                double curAH = l.AIsVertical ? labelW : labelH;
                double aPinX = marginX + (l.ALabel.X - minX) + curAW / 2.0;
                double aPinY = pageHeight - (marginY + (l.ALabel.Y - minY) + curAH / 2.0);

                string fPinX = "";
                string fPinY = "";
                if (nodeShapeMap.TryGetValue(l.A, out int srcId) && nodeCenterMap.TryGetValue(srcId, out var srcPos))
                {
                    double offX = aPinX - srcPos.PinX;
                    double offY = aPinY - srcPos.PinY;
                    fPinX = $" F='{FormatOffsetFormula(srcId, "PinX", offX)}'";
                    fPinY = $" F='{FormatOffsetFormula(srcId, "PinY", offY)}'";
                }

                string aAngleCell = "";
                if (l.AIsVertical)
                {
                    if (l.ASide == "top")
                        aAngleCell = "      <Cell N='Angle' V='1.5707963267948966' F='90 deg'/>\n";
                    else
                        aAngleCell = "      <Cell N='Angle' V='-1.5707963267948966' F='-90 deg'/>\n";
                }

                pageSb.AppendLine($"    <Shape ID='{aId}' Type='Shape' LineStyle='3' FillStyle='3' TextStyle='3'>");
                pageSb.AppendLine($"      <Cell N='PinX' V='{Inv(aPinX)}'{fPinX}/>");
                pageSb.AppendLine($"      <Cell N='PinY' V='{Inv(aPinY)}'{fPinY}/>");
                if (!string.IsNullOrEmpty(aAngleCell))
                    pageSb.Append(aAngleCell);
                pageSb.AppendLine($"      <Cell N='Width' V='{Inv(labelW)}'/>");
                pageSb.AppendLine($"      <Cell N='Height' V='{Inv(labelH)}'/>");
                pageSb.AppendLine($"      <Cell N='LocPinX' V='{Inv(labelW * 0.5)}' F='Width*0.5'/>");
                pageSb.AppendLine($"      <Cell N='LocPinY' V='{Inv(labelH * 0.5)}' F='Height*0.5'/>");
                pageSb.AppendLine("      <Cell N='LineColor' V='#B0BEC5'/>");
                pageSb.AppendLine("      <Cell N='LineWeight' V='0.01'/>");
                pageSb.AppendLine("      <Cell N='LinePattern' V='1'/>");
                pageSb.AppendLine("      <Cell N='FillForegnd' V='#FFFFFF'/>");
                pageSb.AppendLine("      <Cell N='FillPattern' V='1'/>");
                pageSb.AppendLine("      <Cell N='Rounding' V='0.03'/>");
                pageSb.AppendLine("      <Cell N='VerticalAlign' V='1'/>");
                pageSb.AppendLine($"      <Cell N='TxtWidth' V='{Inv(labelW)}' F='Width*1'/>");
                pageSb.AppendLine($"      <Cell N='TxtHeight' V='{Inv(labelH)}' F='Height*1'/>");
                pageSb.AppendLine($"      <Cell N='TxtPinX' V='{Inv(labelW * 0.5)}' F='Width*0.5'/>");
                pageSb.AppendLine($"      <Cell N='TxtPinY' V='{Inv(labelH * 0.5)}' F='Height*0.5'/>");
                pageSb.AppendLine($"      <Cell N='TxtLocPinX' V='{Inv(labelW * 0.5)}' F='TxtWidth*0.5'/>");
                pageSb.AppendLine($"      <Cell N='TxtLocPinY' V='{Inv(labelH * 0.5)}' F='TxtHeight*0.5'/>");
                pageSb.AppendLine("      <Cell N='TopMargin' V='0'/>");
                pageSb.AppendLine("      <Cell N='BottomMargin' V='0'/>");
                pageSb.AppendLine("      <Cell N='LeftMargin' V='0.02'/>");
                pageSb.AppendLine("      <Cell N='RightMargin' V='0.02'/>");
                pageSb.AppendLine("      <Cell N='Char.Size' V='0.09'/>");
                pageSb.AppendLine("      <Cell N='Char.Color' V='#263238'/>");
                pageSb.AppendLine("      <Section N='Geometry' IX='0'>");
                pageSb.AppendLine("        <Cell N='NoFill' V='0'/>");
                pageSb.AppendLine("        <Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>");
                pageSb.AppendLine($"        <Row T='LineTo' IX='2'><Cell N='X' V='{Inv(labelW)}' F='Width*1'/><Cell N='Y' V='0'/></Row>");
                pageSb.AppendLine($"        <Row T='LineTo' IX='3'><Cell N='X' V='{Inv(labelW)}' F='Width*1'/><Cell N='Y' V='{Inv(labelH)}' F='Height*1'/></Row>");
                pageSb.AppendLine($"        <Row T='LineTo' IX='4'><Cell N='X' V='0'/><Cell N='Y' V='{Inv(labelH)}' F='Height*1'/></Row>");
                pageSb.AppendLine("        <Row T='LineTo' IX='5'><Cell N='X' V='0' F='Geometry1.X1'/><Cell N='Y' V='0' F='Geometry1.Y1'/></Row>");
                pageSb.AppendLine("      </Section>");
                pageSb.AppendLine("      <Section N='Paragraph'>");
                pageSb.AppendLine("        <Row IX='0'><Cell N='HorzAlign' V='1'/></Row>");
                pageSb.AppendLine("      </Section>");
                pageSb.AppendLine($"      <Text>{Escape(l.APort)}</Text>");
                pageSb.AppendLine("    </Shape>");
            }

            if (!string.IsNullOrWhiteSpace(l.BPort))
            {
                int bId = nextShapeId++;
                double curBW = l.BIsVertical ? labelH : labelW;
                double curBH = l.BIsVertical ? labelW : labelH;
                double bPinX = marginX + (l.BLabel.X - minX) + curBW / 2.0;
                double bPinY = pageHeight - (marginY + (l.BLabel.Y - minY) + curBH / 2.0);

                string fPinX = "";
                string fPinY = "";
                if (nodeShapeMap.TryGetValue(l.B, out int dstId) && nodeCenterMap.TryGetValue(dstId, out var dstPos))
                {
                    double offX = bPinX - dstPos.PinX;
                    double offY = bPinY - dstPos.PinY;
                    fPinX = $" F='{FormatOffsetFormula(dstId, "PinX", offX)}'";
                    fPinY = $" F='{FormatOffsetFormula(dstId, "PinY", offY)}'";
                }

                string bAngleCell = "";
                if (l.BIsVertical)
                {
                    if (l.BSide == "top")
                        bAngleCell = "      <Cell N='Angle' V='1.5707963267948966' F='90 deg'/>\n";
                    else
                        bAngleCell = "      <Cell N='Angle' V='-1.5707963267948966' F='-90 deg'/>\n";
                }

                pageSb.AppendLine($"    <Shape ID='{bId}' Type='Shape' LineStyle='3' FillStyle='3' TextStyle='3'>");
                pageSb.AppendLine($"      <Cell N='PinX' V='{Inv(bPinX)}'{fPinX}/>");
                pageSb.AppendLine($"      <Cell N='PinY' V='{Inv(bPinY)}'{fPinY}/>");
                if (!string.IsNullOrEmpty(bAngleCell))
                    pageSb.Append(bAngleCell);
                pageSb.AppendLine($"      <Cell N='Width' V='{Inv(labelW)}'/>");
                pageSb.AppendLine($"      <Cell N='Height' V='{Inv(labelH)}'/>");
                pageSb.AppendLine($"      <Cell N='LocPinX' V='{Inv(labelW * 0.5)}' F='Width*0.5'/>");
                pageSb.AppendLine($"      <Cell N='LocPinY' V='{Inv(labelH * 0.5)}' F='Height*0.5'/>");
                pageSb.AppendLine("      <Cell N='LineColor' V='#B0BEC5'/>");
                pageSb.AppendLine("      <Cell N='LineWeight' V='0.01'/>");
                pageSb.AppendLine("      <Cell N='LinePattern' V='1'/>");
                pageSb.AppendLine("      <Cell N='FillForegnd' V='#FFFFFF'/>");
                pageSb.AppendLine("      <Cell N='FillPattern' V='1'/>");
                pageSb.AppendLine("      <Cell N='Rounding' V='0.03'/>");
                pageSb.AppendLine("      <Cell N='VerticalAlign' V='1'/>");
                pageSb.AppendLine($"      <Cell N='TxtWidth' V='{Inv(labelW)}' F='Width*1'/>");
                pageSb.AppendLine($"      <Cell N='TxtHeight' V='{Inv(labelH)}' F='Height*1'/>");
                pageSb.AppendLine($"      <Cell N='TxtPinX' V='{Inv(labelW * 0.5)}' F='Width*0.5'/>");
                pageSb.AppendLine($"      <Cell N='TxtPinY' V='{Inv(labelH * 0.5)}' F='Height*0.5'/>");
                pageSb.AppendLine($"      <Cell N='TxtLocPinX' V='{Inv(labelW * 0.5)}' F='TxtWidth*0.5'/>");
                pageSb.AppendLine($"      <Cell N='TxtLocPinY' V='{Inv(labelH * 0.5)}' F='TxtHeight*0.5'/>");
                pageSb.AppendLine("      <Cell N='TopMargin' V='0'/>");
                pageSb.AppendLine("      <Cell N='BottomMargin' V='0'/>");
                pageSb.AppendLine("      <Cell N='LeftMargin' V='0.02'/>");
                pageSb.AppendLine("      <Cell N='RightMargin' V='0.02'/>");
                pageSb.AppendLine("      <Cell N='Char.Size' V='0.09'/>");
                pageSb.AppendLine("      <Cell N='Char.Color' V='#263238'/>");
                pageSb.AppendLine("      <Section N='Geometry' IX='0'>");
                pageSb.AppendLine("        <Cell N='NoFill' V='0'/>");
                pageSb.AppendLine("        <Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>");
                pageSb.AppendLine($"        <Row T='LineTo' IX='2'><Cell N='X' V='{Inv(labelW)}' F='Width*1'/><Cell N='Y' V='0'/></Row>");
                pageSb.AppendLine($"        <Row T='LineTo' IX='3'><Cell N='X' V='{Inv(labelW)}' F='Width*1'/><Cell N='Y' V='{Inv(labelH)}' F='Height*1'/></Row>");
                pageSb.AppendLine($"        <Row T='LineTo' IX='4'><Cell N='X' V='0'/><Cell N='Y' V='{Inv(labelH)}' F='Height*1'/></Row>");
                pageSb.AppendLine("        <Row T='LineTo' IX='5'><Cell N='X' V='0' F='Geometry1.X1'/><Cell N='Y' V='0' F='Geometry1.Y1'/></Row>");
                pageSb.AppendLine("      </Section>");
                pageSb.AppendLine("      <Section N='Paragraph'>");
                pageSb.AppendLine("        <Row IX='0'><Cell N='HorzAlign' V='1'/></Row>");
                pageSb.AppendLine("      </Section>");
                pageSb.AppendLine($"      <Text>{Escape(l.BPort)}</Text>");
                pageSb.AppendLine("    </Shape>");
            }
        }

        pageSb.AppendLine("  </Shapes>");

        // 5. Glued Connects Table for interactive Visio dragging
        if (connectsList.Any())
        {
            pageSb.AppendLine("  <Connects>");
            foreach (var conn in connectsList)
            {
                pageSb.AppendLine($"    <Connect FromSheet='{conn.ConnId}' FromCell='BeginX' FromPart='9' ToSheet='{conn.SrcId}' ToCell='PinX' ToPart='3'/>");
                pageSb.AppendLine($"    <Connect FromSheet='{conn.ConnId}' FromCell='EndX' FromPart='12' ToSheet='{conn.DstId}' ToCell='PinX' ToPart='3'/>");
            }
            pageSb.AppendLine("  </Connects>");
        }

        pageSb.AppendLine("</PageContents>");
        AddZipEntry(archive, "visio/pages/page1.xml", pageSb.ToString());
    }
}
