using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VisioCreator.Export.Stencils;

public class StencilManager
{
    private readonly ILogger<StencilManager> _logger;
    private readonly List<StencilMasterDefinition> _masters;
    private readonly Dictionary<string, StencilMasterDefinition> _keyMap;
    private List<StencilMasterDefinition>? _customMasters;

    public string? CustomStencilPath { get; set; }
    public bool HasCustomStencil => !string.IsNullOrEmpty(CustomStencilPath) && System.IO.File.Exists(CustomStencilPath) && _customMasters != null && _customMasters.Count > 0;

    public StencilManager(ILogger<StencilManager>? logger = null)
    {
        _logger = logger ?? NullLogger<StencilManager>.Instance;
        _masters = StencilMasterDefinition.GetStarterMasters();
        _keyMap = new Dictionary<string, StencilMasterDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in _masters)
        {
            if (!string.IsNullOrEmpty(m.StencilKey))
            {
                _keyMap[m.StencilKey] = m;
            }
        }
    }

    public IReadOnlyList<StencilMasterDefinition> Masters => _customMasters ?? _masters;

    public bool LoadFromVssx(string vssxPath)
    {
        if (string.IsNullOrWhiteSpace(vssxPath) || !System.IO.File.Exists(vssxPath))
        {
            _logger.LogWarning("Custom stencil file not found: {Path}", vssxPath);
            return false;
        }

        try
        {
            var mastersList = new List<StencilMasterDefinition>();
            using (var zipStream = new System.IO.FileStream(vssxPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
            using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read))
            {
                var mastersEntry = archive.Entries.FirstOrDefault(e => e.FullName.TrimStart('/').Replace('\\', '/').Equals("visio/masters/masters.xml", StringComparison.OrdinalIgnoreCase));
                if (mastersEntry == null)
                {
                    var entriesStr = string.Join(", ", archive.Entries.Select(e => e.FullName));
                    _logger.LogWarning("No visio/masters/masters.xml found in {Path}. Entries: {Entries}", vssxPath, entriesStr);
                    return false;
                }

                using var reader = new System.IO.StreamReader(mastersEntry.Open(), System.Text.Encoding.UTF8);
                var xml = reader.ReadToEnd();
                var nt = new System.Xml.NameTable();
                var nsMgr = new System.Xml.XmlNamespaceManager(nt);
                nsMgr.AddNamespace("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                var context = new System.Xml.XmlParserContext(nt, nsMgr, null, System.Xml.XmlSpace.None);
                using var xmlReader = System.Xml.XmlReader.Create(new System.IO.StringReader(xml), new System.Xml.XmlReaderSettings { NameTable = nt }, context);
                var doc = System.Xml.Linq.XDocument.Load(xmlReader);

                int idx = 1;
                foreach (var mElem in doc.Descendants().Where(e => e.Name.LocalName.Equals("Master", StringComparison.OrdinalIgnoreCase)))
                {
                    var idAttr = mElem.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("ID", StringComparison.OrdinalIgnoreCase))?.Value;
                    int id = int.TryParse(idAttr, out var parsedId) ? parsedId : idx++;

                    string name = mElem.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase))?.Value ?? $"Master {id}";
                    string nameU = mElem.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("NameU", StringComparison.OrdinalIgnoreCase))?.Value ?? name;

                    double w = 2.4, h = 1.0;
                    var pwCell = mElem.Descendants().FirstOrDefault(c => c.Name.LocalName == "Cell" && (string?)c.Attributes().FirstOrDefault(a => a.Name.LocalName == "N")?.Value == "PageWidth");
                    if (pwCell != null)
                    {
                        var valStr = pwCell.Attributes().FirstOrDefault(a => a.Name.LocalName == "V")?.Value;
                        if (double.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pw))
                            w = pw;
                    }

                    var phCell = mElem.Descendants().FirstOrDefault(c => c.Name.LocalName == "Cell" && (string?)c.Attributes().FirstOrDefault(a => a.Name.LocalName == "N")?.Value == "PageHeight");
                    if (phCell != null)
                    {
                        var valStr = phCell.Attributes().FirstOrDefault(a => a.Name.LocalName == "V")?.Value;
                        if (double.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ph))
                            h = ph;
                    }

                    string key = name.ToLowerInvariant().Replace(" ", "_").Replace("-", "_");

                    mastersList.Add(new StencilMasterDefinition
                    {
                        Id = id,
                        Name = name,
                        NameU = nameU,
                        StencilKey = key,
                        DefaultWidth = w,
                        DefaultHeight = h,
                        FillColor = "#2563EB",
                        LineColor = "#1D4ED8"
                    });
                }
            }

            if (mastersList.Count > 0)
            {
                _customMasters = mastersList;
                CustomStencilPath = vssxPath;
                _logger.LogInformation("Loaded {Count} masters from custom stencil {Path}", mastersList.Count, vssxPath);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load custom stencil from {Path}", vssxPath);
        }
        return false;
    }

    public StencilMasterDefinition ResolveMaster(string? stencilKey, Dictionary<string, string>? customMap = null, string? roleFallback = null)
    {
        var currentMasters = Masters;

        // 1. Check custom map override first if provided (by stencilKey or by role)
        if (!string.IsNullOrWhiteSpace(stencilKey) && customMap != null && customMap.TryGetValue(stencilKey, out var masterName))
        {
            var matchByName = currentMasters.FirstOrDefault(m => m.Name.Equals(masterName, StringComparison.OrdinalIgnoreCase) || m.NameU.Equals(masterName, StringComparison.OrdinalIgnoreCase));
            if (matchByName != null) return matchByName;
        }

        if (!string.IsNullOrWhiteSpace(roleFallback) && customMap != null && customMap.TryGetValue(roleFallback, out var roleMasterName))
        {
            var matchByName = currentMasters.FirstOrDefault(m => m.Name.Equals(roleMasterName, StringComparison.OrdinalIgnoreCase) || m.NameU.Equals(roleMasterName, StringComparison.OrdinalIgnoreCase));
            if (matchByName != null) return matchByName;
        }

        // 2. Direct stencil key lookup
        if (!string.IsNullOrWhiteSpace(stencilKey))
        {
            if (_customMasters != null)
            {
                var match = _customMasters.FirstOrDefault(m => m.StencilKey.Equals(stencilKey, StringComparison.OrdinalIgnoreCase) || m.Name.Equals(stencilKey, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            else if (_keyMap.TryGetValue(stencilKey, out var master))
            {
                return master;
            }
        }

        // 3. Fallback based on Role
        if (!string.IsNullOrWhiteSpace(roleFallback))
        {
            if (_customMasters != null)
            {
                var roleMatch = _customMasters.FirstOrDefault(m => m.Name.Contains(roleFallback, StringComparison.OrdinalIgnoreCase));
                if (roleMatch != null) return roleMatch;
            }

            var roleKey = roleFallback.Trim().ToLowerInvariant() switch
            {
                "core" => "core_switch",
                "distribution" => "dist_switch",
                "access" => "access_switch",
                "ap" => "ap",
                "printer" => "printer",
                "router" => "router",
                "firewall" => "firewall",
                "external" => "external",
                _ => null
            };

            if (roleKey != null && _keyMap.TryGetValue(roleKey, out var roleMaster))
            {
                return roleMaster;
            }
        }

        if (_customMasters != null && _customMasters.Count > 0)
        {
            return _customMasters[0];
        }

        _logger.LogWarning("Unknown or missing stencil key '{Key}'. Falling back to 'generic' shape.", stencilKey ?? "<null>");
        return _keyMap["generic"];
    }

    public void GenerateStarterStencil(string outputPath)
    {
        var outputDir = System.IO.Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !System.IO.Directory.Exists(outputDir))
        {
            System.IO.Directory.CreateDirectory(outputDir);
        }

        if (System.IO.File.Exists(outputPath))
        {
            System.IO.File.Delete(outputPath);
        }

        using var zipStream = new System.IO.FileStream(outputPath, System.IO.FileMode.Create);
        using var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create);

        // 1. [Content_Types].xml for Stencil (.vssx)
        var ctSb = new System.Text.StringBuilder();
        ctSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>");
        ctSb.AppendLine("<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>");
        ctSb.AppendLine("  <Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml' />");
        ctSb.AppendLine("  <Default Extension='xml' ContentType='application/xml' />");
        ctSb.AppendLine("  <Override PartName='/visio/document.xml' ContentType='application/vnd.ms-visio.stencil.main+xml' />");
        ctSb.AppendLine("  <Override PartName='/visio/masters/masters.xml' ContentType='application/vnd.ms-visio.masters+xml' />");
        for (int i = 1; i <= _masters.Count; i++)
        {
            ctSb.AppendLine($"  <Override PartName='/visio/masters/master{i}.xml' ContentType='application/vnd.ms-visio.master+xml' />");
        }
        ctSb.AppendLine("</Types>");
        AddEntry(archive, "[Content_Types].xml", ctSb.ToString());

        // 2. _rels/.rels
        AddEntry(archive, "_rels/.rels", "<?xml version='1.0' encoding='utf-8'?>\n<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>\n  <Relationship Id='rId1' Type='http://schemas.microsoft.com/visio/2010/relationships/document' Target='visio/document.xml' />\n</Relationships>");

        // 3. visio/_rels/document.xml.rels
        AddEntry(archive, "visio/_rels/document.xml.rels", "<?xml version='1.0' encoding='utf-8'?>\n<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>\n  <Relationship Id='rId1' Type='http://schemas.microsoft.com/visio/2010/relationships/masters' Target='masters/masters.xml' />\n</Relationships>");

        // 4. visio/document.xml
        AddEntry(archive, "visio/document.xml", "<?xml version='1.0' encoding='utf-8'?>\n<VisioDocument xmlns='http://schemas.microsoft.com/office/visio/2012/main'>\n  <DocumentSheet NameU='TheDoc' Name='TheDoc'><Cell N='OutputFormat' V='0'/></DocumentSheet>\n</VisioDocument>");

        // 5. visio/masters/_rels/masters.xml.rels
        var relsSb = new System.Text.StringBuilder();
        relsSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>\n<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>");
        for (int i = 0; i < _masters.Count; i++)
        {
            relsSb.AppendLine($"  <Relationship Id='rId{i + 1}' Type='http://schemas.microsoft.com/visio/2010/relationships/master' Target='master{i + 1}.xml' />");
        }
        relsSb.AppendLine("</Relationships>");
        AddEntry(archive, "visio/masters/_rels/masters.xml.rels", relsSb.ToString());

        // 6. visio/masters/masters.xml
        var mastersSb = new System.Text.StringBuilder();
        mastersSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>\n<Masters xmlns='http://schemas.microsoft.com/office/visio/2012/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'>");
        for (int i = 0; i < _masters.Count; i++)
        {
            var m = _masters[i];
            mastersSb.AppendLine($"  <Master ID='{m.Id}' NameU='{System.Security.SecurityElement.Escape(m.NameU)}' Name='{System.Security.SecurityElement.Escape(m.Name)}'>");
            mastersSb.AppendLine("    <PageSheet LineStyle='0' FillStyle='0' TextStyle='0'>");
            mastersSb.AppendLine($"      <Cell N='PageWidth' V='{m.DefaultWidth}'/>");
            mastersSb.AppendLine($"      <Cell N='PageHeight' V='{m.DefaultHeight}'/>");
            mastersSb.AppendLine("    </PageSheet>");
            mastersSb.AppendLine($"    <Rel r:id='rId{i + 1}'/>");
            mastersSb.AppendLine("  </Master>");
        }
        mastersSb.AppendLine("</Masters>");
        AddEntry(archive, "visio/masters/masters.xml", mastersSb.ToString());

        // 7. Each master{i}.xml
        for (int i = 0; i < _masters.Count; i++)
        {
            var m = _masters[i];
            var mSb = new System.Text.StringBuilder();
            mSb.AppendLine("<?xml version='1.0' encoding='utf-8'?>\n<MasterContents xmlns='http://schemas.microsoft.com/office/visio/2012/main'>\n  <Shapes>");
            mSb.AppendLine($"    <Shape ID='5' Type='Shape' NameU='{System.Security.SecurityElement.Escape(m.NameU)}'>");
            mSb.AppendLine($"      <Cell N='PinX' V='{m.DefaultWidth * 0.5}'/><Cell N='PinY' V='{m.DefaultHeight * 0.5}'/>");
            mSb.AppendLine($"      <Cell N='Width' V='{m.DefaultWidth}'/><Cell N='Height' V='{m.DefaultHeight}'/>");
            mSb.AppendLine($"      <Cell N='FillForegnd' V='{m.FillColor}'/><Cell N='LineColor' V='{m.LineColor}'/>");
            mSb.AppendLine("      <Section N='Geometry' IX='0'><Cell N='NoFill' V='0'/><Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row><Row T='LineTo' IX='2'><Cell N='X' V='1' F='Width*1'/><Cell N='Y' V='0'/></Row><Row T='LineTo' IX='3'><Cell N='X' V='1' F='Width*1'/><Cell N='Y' V='1' F='Height*1'/></Row><Row T='LineTo' IX='4'><Cell N='X' V='0'/><Cell N='Y' V='1' F='Height*1'/></Row><Row T='LineTo' IX='5'><Cell N='X' V='0' F='Geometry1.X1'/><Cell N='Y' V='0' F='Geometry1.Y1'/></Row></Section>");
            mSb.AppendLine("    </Shape>\n  </Shapes>\n</MasterContents>");
            AddEntry(archive, $"visio/masters/master{i + 1}.xml", mSb.ToString());
        }

        _logger.LogInformation("Generated starter stencil at {Path}", outputPath);
    }

    private void AddEntry(System.IO.Compression.ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
        using var writer = new System.IO.StreamWriter(entry.Open(), System.Text.Encoding.UTF8);
        writer.Write(content);
    }
}
