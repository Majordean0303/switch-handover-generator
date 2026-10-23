using System.IO.Compression;
using ClosedXML.Excel;
using SwitchHandover.Core.Models;
using SwitchHandover.Core.Parsers;
using SwitchHandover.Core.Services;
namespace SwitchHandover.Tests;

public class HandoverMigrationTests
{
    static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "access.txt");
    [Fact]
    public void Detail_capture_populates_existing_port_mapping_and_states()
    {
        var parser = new SwitchHandoverParser(File.ReadAllText(Fixture));
        var ports = parser.ParsePortMapping();
        Assert.Equal(2, ports.Count);
        Assert.All(ports, p => { Assert.Equal("CORE-01", p.NeighbourHostname); Assert.Equal("192.0.2.1", p.NeighbourDeviceIp); Assert.Equal("up", p.State); });
    }
    [Fact]
    public void Analysis_retains_excel_and_portchannel_evidence_without_fabricated_neighbors()
    {
        var analysis = HandoverAnalysis.FromFiles([Fixture], new("Lab"));
        Assert.Single(analysis.Switches);
        Assert.Equal(2, analysis.Ports.Count);
        Assert.Equal(3, analysis.AllPorts.Count);
        Assert.Equal(2, analysis.Graph.Nodes.Count);
        var external = analysis.Graph.Nodes["CORE-01"];
        Assert.True(external.IsExternal);
        Assert.Equal("192.0.2.1", external.IpAddress);
        Assert.Equal(2, analysis.Layout.Links.Count);
        Assert.Contains(analysis.Graph.Edges, e => e.SourcePortChannelId == "10" || e.DestinationPortChannelId == "10");
    }
    [Fact]
    public void Workbook_preserves_sheet_names_and_column_meanings()
    {
        var p = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
        try
        {
            var analysis = HandoverAnalysis.FromFiles([Fixture], new("Lab"));
            analysis.ExportExcel(p);
            using var book = new XLWorkbook(p);
            Assert.Equal(new[] { "Switch Details", "Port Mapping", "Complete Port Config" }, book.Worksheets.Select(s => s.Name));
            Assert.Equal("Hostname", book.Worksheet("Switch Details").Cell(1, 3).GetString());
            Assert.Equal("ACCESS-01", book.Worksheet("Switch Details").Cell(2, 3).GetString());
            Assert.Equal("CORE-01", book.Worksheet("Port Mapping").Cell(2, 11).GetString());
        }
        finally { File.Delete(p); }
    }
    [Fact]
    public void Visio_export_uses_current_dragged_layout()
    {
        var p = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vsdx");
        try
        {
            var a = HandoverAnalysis.FromFiles([Fixture], new("Lab"));
            a.Layout.Nodes["ACCESS-01"].X = 40;
            VisioCreator.Core.Layout.DiagramLayoutEngine.Reroute(a.Layout);
            a.ExportVisio(p);
            using var zip = ZipFile.OpenRead(p);
            Assert.NotNull(zip.GetEntry("visio/pages/page1.xml"));
            using var r = new StreamReader(zip.GetEntry("visio/pages/page1.xml")!.Open());
            var xml = r.ReadToEnd();
            Assert.Contains("ACCESS-01", xml);
            Assert.Contains("CORE-01", xml);
            Assert.Contains("StyleSheet", new StreamReader(zip.GetEntry("visio/document.xml")!.Open()).ReadToEnd());
        }
        finally { File.Delete(p); }
    }
    [Fact]
    public void Missing_neighbors_do_not_invent_edges()
    {
        var a = HandoverAnalysis.Build([new SwitchDetails { Hostname = "SW1" }, new SwitchDetails { Hostname = "SW2" }], []);
        Assert.Equal(2, a.Graph.Nodes.Count);
        Assert.Empty(a.Layout.Links);
        Assert.All(a.Graph.Nodes.Values, d => Assert.Null(d.IpAddress));
    }
    [Fact]
    public void Conflicting_stack_ips_remain_unresolved()
    {
        var a = HandoverAnalysis.Build([new SwitchDetails { Hostname = "SW1", IpAddress = "192.0.2.1" }, new SwitchDetails { Hostname = "SW1", IpAddress = "192.0.2.2" }], []);
        Assert.Null(Assert.Single(a.Graph.Nodes).Value.IpAddress);
        Assert.Contains(a.Notes, n => n.Contains("Conflicting"));
    }
    [Theory][InlineData("192.0.2.3", "192.0.2.0/24", true)][InlineData("192.0.3.3", "192.0.2.0/24", false)][InlineData("192.0.2.3", "0.0.0.0/0", true)][InlineData("192.0.2.3", "192.0.2.3/32", true)][InlineData("192.0.2.4", "192.0.2.3/32", false)] public void Cidr_filter_checks_correct_bits(string ip, string filter, bool result) => Assert.Equal(result, BoundedLiveCapture.InScope(ip, filter));
    [Theory]
    [InlineData("192.0.2.0/33")]
    [InlineData("192.0.2.0/-1")]
    [InlineData("bad")]
    public void Invalid_filters_rejected_before_connection(string filter) => Assert.Throws<ArgumentException>(() => BoundedLiveCapture.InScope("192.0.2.1", filter));
    [Fact]
    public void Archive_budget_fails_before_any_extraction()
    {
        var p = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        var d = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            using (var zip = ZipFile.Open(p, ZipArchiveMode.Create))
            {
                using var w = new StreamWriter(zip.CreateEntry("large.txt").Open());
                w.Write(new string('x', 100));
            }
            Assert.Throws<InvalidDataException>(() => ZipArchiveService.ExtractConfigsFromZip(p, d, limits: new(MaxEntryBytes: 50)));
            Assert.Empty(Directory.GetFiles(d));
        }
        finally { File.Delete(p); if (Directory.Exists(d)) Directory.Delete(d, true); }
    }
}
