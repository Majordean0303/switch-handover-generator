using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using SwitchHandover.Core.Models;
using SwitchHandover.Core.Parsers;
using VisioCreator.Core.Graph;
using VisioCreator.Core.Layout;
using VisioCreator.Core.Models;
using VisioCreator.Core.Parsing;
using VisioCreator.Export;
using VisioCreator.Export.Configuration;
namespace SwitchHandover.Core.Services;

public sealed record AnalysisOptions(string Location, string LocationType = "Branch", string? ManagementVlan = null, bool IncludeSfps = true, bool IncludeAllPorts = true);
public sealed record HandoverAnalysis(List<SwitchDetails> Switches, List<PortMapping> Ports, List<SfpInventory> Sfps, List<PortMapping> AllPorts, TopologyGraph Graph, DiagramLayout Layout, List<string> Notes)
{
    public void ExportExcel(string path, string type = "both") => WriteAtomic(path, p => HandoverExcelExporter.ExportExcel(p, type, Switches, Ports, Sfps, AllPorts));
    public void ExportCsv(string path, string type) => WriteAtomic(path, p => HandoverExcelExporter.ExportCsv(p, type, Switches, Ports, Sfps, AllPorts));
    public void ExportVisio(string path) => WriteAtomic(path, p => new VisioExporter().Export(Graph, Layout.CreateSnapshot(), p, new VisioExportOptions { PagePerSite = false }));
    public void ExportBundle(string path)
    {
        var excel = Path.Combine(Path.GetTempPath(), "Complete_Handover_" + Guid.NewGuid() + ".xlsx");
        try
        {
            ExportExcel(excel);
            var reports = new Dictionary<string, string>();
            foreach (var type in new[] { ("switches", "Switch_Details.csv"), ("ports", "Port_Mapping.csv"), ("sfp", "SFP_Inventory.csv"), ("allports", "Complete_Port_Config.csv") })
                reports[type.Item2] = HandoverExcelExporter.GenerateCsvString(type.Item1, Switches, Ports, Sfps, AllPorts);
            WriteAtomic(path, p => ZipArchiveService.CreateReportsBundleZip(p, excel, reports));
        }
        finally { File.Delete(excel); }
    }
    static void WriteAtomic(string path, Action<string> writer)
    {
        var target = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = Path.Combine(Path.GetDirectoryName(target)!, Guid.NewGuid() + Path.GetExtension(target));
        try
        {
            writer(temp);
            File.Move(temp, target, true);
        }
        finally { File.Delete(temp); }
    }
    public static HandoverAnalysis FromFiles(IEnumerable<string> paths, AnalysisOptions options, Action<string>? log = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(options.Location))
            throw new ArgumentException("A site location is required.");
        var source = paths.ToArray();
        if (source.Length == 0 || source.Any(p => !File.Exists(p) && !Directory.Exists(p)))
            throw new FileNotFoundException("Select existing captures, archives or folders.");
        var temporary = Path.Combine(Path.GetTempPath(), "SwitchHandover", Guid.NewGuid().ToString("N"));
        try
        {
            var notes = new List<string>();
            var files = ZipArchiveService.ResolveAllFiles(source, temporary, (category, message) => { log?.Invoke(category + ": " + message); if (category == "Error") notes.Add(message); });
            if (files.Count > 1000)
                throw new InvalidDataException("Capture limit is 1,000 files.");
            if (files.Count == 0)
                throw new InvalidDataException("No readable capture files found.");
            var switches = new List<SwitchDetails>();
            var ports = new List<PortMapping>();
            var sfps = new List<SfpInventory>();
            var allPorts = new List<PortMapping>();
            var backups = new List<ParsedBackup>();
            long bytes = 0;
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var info = new FileInfo(file);
                bytes += info.Length;
                if (info.Length > 128L * 1024 * 1024 || bytes > 512L * 1024 * 1024)
                    throw new InvalidDataException("Capture size exceeds 128 MiB per file / 512 MiB total.");
                var text = File.ReadAllText(file);
                var isAp = Regex.IsMatch(text, @"Device Type:\s*AP\b|^\s*AP Name\s*:", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (isAp)
                {
                    switches.AddRange(new APHandoverParser(text, options.Location, options.LocationType).ParseDeviceDetails());
                }
                else
                {
                    var parser = new SwitchHandoverParser(text, options.Location, options.LocationType, options.ManagementVlan);
                    switches.AddRange(parser.ParseSwitchDetails());
                    ports.AddRange(parser.ParsePortMapping());
                    if (options.IncludeSfps)
                        sfps.AddRange(parser.ParseSfpInventory());
                    if (options.IncludeAllPorts)
                        allPorts.AddRange(parser.ParsePortMapping(true));
                    backups.Add(new ConfigParser().ParseText(text, Path.GetFileName(file)));
                }
                log?.Invoke("Parsed " + Path.GetFileName(file));
            }
            var ipDirectory = switches.Where(s => Known(s.Hostname) && ValidIp(s.IpAddress)).GroupBy(s => HostKey.Of(s.Hostname)).Where(g => g.Select(s => s.IpAddress).Distinct().Count() == 1).ToDictionary(g => g.Key, g => g.First().IpAddress);
            foreach (var p in ports.Concat(allPorts))
            if (!ValidIp(p.NeighbourDeviceIp) && ipDirectory.TryGetValue(HostKey.Of(p.NeighbourHostname), out var ip))
                p.NeighbourDeviceIp = ip;
            return Build(switches, ports, sfps, allPorts, backups, notes);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
    public static HandoverAnalysis Build(List<SwitchDetails> switches, List<PortMapping> ports, List<SfpInventory>? sfps = null, List<PortMapping>? allPorts = null, IEnumerable<ParsedBackup>? backups = null, List<string>? notes = null)
    {
        notes ??= [];
        var devices = new List<Device>();
        foreach (var group in switches.Where(s => Known(s.Hostname)).GroupBy(s => HostKey.Of(s.Hostname)))
        {
            var first = group.First();
            var ips = group.Select(s => s.IpAddress).Where(ValidIp).Distinct().ToArray();
            if (ips.Length > 1)
                notes.Add("Conflicting management addresses: " + first.Hostname + "; topology leaves address unresolved.");
            var role = first.DeviceType.Contains("AP", StringComparison.OrdinalIgnoreCase) ? "AP" : first.DeviceType.Contains("Core", StringComparison.OrdinalIgnoreCase) || first.DeviceType.Contains("L3", StringComparison.OrdinalIgnoreCase) ? "Core" : "Access";
            devices.Add(new()
            {
                Hostname = first.Hostname,
                IpAddress = ips.Length == 1 ? ips[0] : null,
                Model = first.Model,
                Location = first.Location,
                Role = role,
                IsAp = role == "AP",
                StencilKey = role == "AP" ? "ap" : role == "Core" ? "core_switch" : "access_switch"
            });
        }
        var wires = new List<Link>();
        foreach (var p in ports.Where(p => Known(p.Hostname) && Known(p.NeighbourHostname)))
        {
            wires.Add(new()
            {
                Source = p.Hostname,
                SourceInterface = Known(p.PortNo) ? p.PortNo : "",
                Destination = p.NeighbourHostname,
                DestinationInterface = Known(p.NeighbourPortNo) ? p.NeighbourPortNo : ""
            });
        }
        foreach (var backup in backups ?? [])
        {
            var device = devices.FirstOrDefault(d => HostKey.Same(d.Hostname, backup.Hostname));
            if (device is not null)
            {
                device.PortChannels = backup.PortChannels.Select(p => new PortChannel { Id = p.Id, Mode = p.Mode, MemberPorts = p.MemberPorts.ToList() }).ToList();
            }
            foreach (var neighbor in backup.Neighbors.Where(n => Known(n.RemoteHostname) && Known(backup.Hostname)))
            {
                wires.Add(new()
                {
                    Source = backup.Hostname,
                    SourceInterface = neighbor.LocalPort,
                    Destination = neighbor.RemoteHostname,
                    DestinationInterface = neighbor.RemotePort
                });
            }
        }
        var neighborEvidence = ports.Where(p => Known(p.NeighbourHostname)).Select(p => (Host: p.NeighbourHostname, Ip: p.NeighbourDeviceIp)).Concat((backups ?? []).SelectMany(b => b.Neighbors).Where(n => Known(n.RemoteHostname)).Select(n => (Host: n.RemoteHostname, Ip: n.RemoteIp)));
        foreach (var group in neighborEvidence.GroupBy(n => HostKey.Of(n.Host)))
        {
            if (devices.Any(d => HostKey.Same(d.Hostname, group.Key)))
                continue;
            var ips = group.Select(n => n.Ip).Where(ValidIp).Distinct().ToArray();
            devices.Add(new()
            {
                Hostname = group.First().Host,
                IpAddress = ips.Length == 1 ? ips[0] : null,
                Role = "External",
                IsExternal = true,
                Tier = 5
            });
            if (ips.Length > 1)
                notes.Add("Conflicting neighbor addresses: " + group.First().Host);
        }
        var graph = new GraphEngine().BuildGraph(devices, wires);
        notes.AddRange(graph.Warnings);
        var nodes = graph.Nodes.Values.GroupBy(d => HostKey.Of(d.Hostname)).Select(g => new DiagramNode(g.Key, g.First().Role, g.First().Location));
        var links = new List<DiagramLink>();
        foreach (var edge in graph.Edges)
        foreach (var wire in edge.MemberLinks)
        {
            var forward = HostKey.Same(wire.Source, edge.Source);
            links.Add(new("L" + links.Count, HostKey.Of(edge.Source), HostKey.Of(edge.Destination), PortKey.FormatShort(forward ? wire.SourceInterface : wire.DestinationInterface), PortKey.FormatShort(forward ? wire.DestinationInterface : wire.SourceInterface), edge.Type.ToString(), edge.SourcePortChannelId ?? edge.DestinationPortChannelId, wire.IsFiber || edge.IsFiber, wire.MediaType ?? edge.MediaType));
        }
        var layout = DiagramLayoutEngine.Build(nodes, links);
        return new(switches, ports, sfps ?? [], allPorts ?? [], graph, layout, notes);
    }
    static bool Known(string? s) => !string.IsNullOrWhiteSpace(s) && s is not "-" and not "N/A" and not "Unknown";
    static bool ValidIp(string s) => IPAddress.TryParse(s, out var ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.None);
}
