// NetHealth.Core/Topology/GraphEngine.cs
using System;
using System.Collections.Generic;
using System.Linq;

using VisioCreator.Core.Models;
namespace VisioCreator.Core.Graph;

public class TopologyGraph
{
    public Dictionary<string, Device> Nodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<LogicalLink> Edges { get; } = new();
    public List<string> Warnings { get; } = new();

    public void AddNode(Device device)
    {
        if (!string.IsNullOrWhiteSpace(device.Hostname))
            Nodes[device.Hostname] = device;
    }

    public void AddEdge(LogicalLink link)
    {
        Edges.Add(link);
    }
}

public class GraphEngine
{
    public TopologyGraph BuildGraph(IEnumerable<Device> devices, IEnumerable<Link> rawLinks)
    {
        var graph = new TopologyGraph();

        // Add nodes
        foreach (var d in devices)
        {
            graph.AddNode(d);
        }

        // Dedupe links A->B and B->A
        var linkPairs = new Dictionary<string, List<Link>>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in rawLinks)
        {
            if (string.IsNullOrWhiteSpace(link.Source) || string.IsNullOrWhiteSpace(link.Destination)) { graph.Warnings.Add("Link omitted: missing endpoint hostname."); continue; }
            var srcNorm = ResolveNeighborName(link.Source, graph.Nodes.Keys);
            var dstNorm = ResolveNeighborName(link.Destination, graph.Nodes.Keys);

            // Invalid endpoints must not create phantom devices or reach dictionary indexing.
            if (string.IsNullOrWhiteSpace(srcNorm) || string.IsNullOrWhiteSpace(dstNorm) ||
                srcNorm.Equals("N/A", StringComparison.OrdinalIgnoreCase) || dstNorm.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
                HostKey.Same(srcNorm, dstNorm)) { graph.Warnings.Add($"Link omitted: invalid/self endpoints {link.Source} → {link.Destination}."); continue; }

            // If the neighbor is unresolved, create an external node for it
            if (!graph.Nodes.ContainsKey(srcNorm) && !srcNorm.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            {
                var extNode = new Device { Hostname = srcNorm, Role = "External", IsExternal = true, Tier = 5 };
                graph.AddNode(extNode);
            }
            if (!graph.Nodes.ContainsKey(dstNorm) && !dstNorm.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            {
                var extNode = new Device { Hostname = dstNorm, Role = "External", IsExternal = true, Tier = 5 };
                graph.AddNode(extNode);
            }

            // Normalize pair key (e.g., SW1-SW2-Gi1/0/1-Gi2/0/1)
            var pairId = string.Compare(srcNorm, dstNorm, StringComparison.OrdinalIgnoreCase) < 0
                ? System.Text.Json.JsonSerializer.Serialize(new[]{srcNorm,dstNorm})
                : System.Text.Json.JsonSerializer.Serialize(new[]{dstNorm,srcNorm});

            if (!linkPairs.ContainsKey(pairId))
                linkPairs[pairId] = new List<Link>();

            // Before adding, check if it's an exact physical duplicate (e.g. from both sides of CDP)
            bool isDuplicate = false;
            foreach (var existing in linkPairs[pairId])
            {
                bool sameDir = HostKey.Same(existing.Source, srcNorm) &&
                               PortKey.Same(existing.SourceInterface, link.SourceInterface) &&
                               HostKey.Same(existing.Destination, dstNorm) &&
                               PortKey.Same(existing.DestinationInterface, link.DestinationInterface);

                bool revDir = HostKey.Same(existing.Source, dstNorm) &&
                              PortKey.Same(existing.SourceInterface, link.DestinationInterface) &&
                              HostKey.Same(existing.Destination, srcNorm) &&
                              PortKey.Same(existing.DestinationInterface, link.SourceInterface);

                if (sameDir || revDir)
                {
                    isDuplicate = true;
                    // Prefer "Port 1" over "1"
                    if (sameDir && existing.DestinationInterface == "1" && PortKey.Same(link.DestinationInterface, "Port 1"))
                        existing.DestinationInterface = "Port 1";
                    else if (revDir && existing.SourceInterface == "1" && PortKey.Same(link.DestinationInterface, "Port 1"))
                        existing.SourceInterface = "Port 1";
                    break;
                }

                // IP Phone / leaf endpoint deduplication:
                // If one of the endpoints is an IP phone, only a single physical link can exist from that switch port.
                bool isPhoneLink = srcNorm.StartsWith("SEP", StringComparison.OrdinalIgnoreCase) ||
                                   dstNorm.StartsWith("SEP", StringComparison.OrdinalIgnoreCase) ||
                                   (graph.Nodes.TryGetValue(srcNorm, out var sn) && sn.IsPhoneDevice) ||
                                   (graph.Nodes.TryGetValue(dstNorm, out var dn) && dn.IsPhoneDevice);
                if (isPhoneLink)
                {
                    bool sameSwitchPort = (HostKey.Same(existing.Source, srcNorm) && PortKey.Same(existing.SourceInterface, link.SourceInterface)) ||
                                          (HostKey.Same(existing.Destination, dstNorm) && PortKey.Same(existing.DestinationInterface, link.DestinationInterface)) ||
                                          (HostKey.Same(existing.Source, dstNorm) && PortKey.Same(existing.SourceInterface, link.DestinationInterface)) ||
                                          (HostKey.Same(existing.Destination, srcNorm) && PortKey.Same(existing.DestinationInterface, link.SourceInterface));
                    if (sameSwitchPort)
                    {
                        isDuplicate = true;
                        if (existing.DestinationInterface == "1" || PortKey.Same(link.DestinationInterface, "Port 1"))
                            existing.DestinationInterface = "Port 1";
                        if (existing.SourceInterface == "1" || PortKey.Same(link.SourceInterface, "Port 1"))
                            existing.SourceInterface = "Port 1";
                        break;
                    }
                }
            }

            if (!isDuplicate)
            {
                // Assign correct names and normalized interfaces
                link.Source = srcNorm;
                link.Destination = dstNorm;
                link.SourceInterface = PortKey.Normalize(link.SourceInterface);
                link.DestinationInterface = PortKey.Normalize(link.DestinationInterface);

                // Ensure IP Phone endpoint interface is canonical "Port 1"
                if (dstNorm.StartsWith("SEP", StringComparison.OrdinalIgnoreCase) &&
                    (link.DestinationInterface == "1" || link.DestinationInterface.StartsWith("P1", StringComparison.OrdinalIgnoreCase)))
                {
                    link.DestinationInterface = "Port 1";
                }
                if (srcNorm.StartsWith("SEP", StringComparison.OrdinalIgnoreCase) &&
                    (link.SourceInterface == "1" || link.SourceInterface.StartsWith("P1", StringComparison.OrdinalIgnoreCase)))
                {
                    link.SourceInterface = "Port 1";
                }

                linkPairs[pairId].Add(link);
            }
        }

        // Now build LogicalLinks from the deduped physical wires
        foreach (var kvp in linkPairs)
        {
            var wires = kvp.Value;
            var src = wires.First().Source;
            var dst = wires.First().Destination;

            var srcDevice = graph.Nodes[src];
            var dstDevice = graph.Nodes[dst];

            var logicalLinks = MergeIntoLogicalLinks(srcDevice, dstDevice, wires);
            foreach (var ll in logicalLinks)
            {
                // Classify Link Type
                ll.Type = DetermineLinkType(srcDevice, dstDevice, ll);
                graph.AddEdge(ll);
            }
        }

        return graph;
    }

    private string ResolveNeighborName(string name, IEnumerable<string> knownHosts)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Unknown";

        var nameNorm = NormalizeName(name);

        // Exact match
        var exact = knownHosts.FirstOrDefault(h => NormalizeName(h) == nameNorm);
        if (exact != null)
            return exact;

        // Truncated match (CDP often truncates long hostnames to ~20 chars)
        var matches = knownHosts.Where(h =>
        {
            var hNorm = NormalizeName(h);
            return hNorm.StartsWith(nameNorm, StringComparison.OrdinalIgnoreCase) ||
                   nameNorm.StartsWith(hNorm, StringComparison.OrdinalIgnoreCase);
        }).Take(2).ToArray();

        // Ambiguous abbreviated names must remain unresolved, not attach to the first device.
        if (matches.Length == 1)
            return matches[0];

        return name;
    }

    private string NormalizeName(string name)
    {
        var lower = HostKey.Of(name).ToLowerInvariant();
        if (lower.EndsWith(".jsw.in"))
            lower = lower.Substring(0, lower.Length - 7);
        if (lower.EndsWith(".corp.local"))
            lower = lower.Substring(0, lower.Length - 11);
        return lower.Replace("-", "_");
    }

    private List<LogicalLink> MergeIntoLogicalLinks(Device srcDevice, Device dstDevice, List<Link> physicalWires)
    {
        var result = new List<LogicalLink>();
        var bundledWires = new HashSet<Link>();

        // Find Port-Channels
        foreach (var po in srcDevice.PortChannels)
        {
            var matchingWires = physicalWires.Where(w =>
                (HostKey.Same(w.Source, srcDevice.Hostname) && po.MemberPorts.Any(p => PortKey.Same(p, w.SourceInterface))) ||
                (HostKey.Same(w.Destination, srcDevice.Hostname) && po.MemberPorts.Any(p => PortKey.Same(p, w.DestinationInterface)))
            ).ToList();

            if (matchingWires.Any())
            {
                var isFiber = matchingWires.Any(w => w.IsFiber);
                var mediaType = matchingWires.FirstOrDefault(w => !string.IsNullOrEmpty(w.MediaType))?.MediaType;

                var ll = new LogicalLink
                {
                    Source = srcDevice.Hostname,
                    Destination = dstDevice.Hostname,
                    SourcePortChannelId = po.Id,
                    Mode = po.Mode,
                    MemberLinks = matchingWires,
                    IsFiber = isFiber,
                    MediaType = mediaType
                };

                // Try to find matching PO on dstDevice
                var firstWire = matchingWires.First();
                var dstPort = HostKey.Same(firstWire.Destination, dstDevice.Hostname)
                    ? firstWire.DestinationInterface
                    : firstWire.SourceInterface;

                var dstPo = dstDevice.PortChannels.FirstOrDefault(p => p.MemberPorts.Any(mp => PortKey.Same(mp, dstPort)));
                if (dstPo != null)
                {
                    ll.DestinationPortChannelId = dstPo.Id;
                }

                result.Add(ll);
                foreach (var w in matchingWires)
                    bundledWires.Add(w);
            }
        }

        // Remaining standalone links
        foreach (var wire in physicalWires)
        {
            if (!bundledWires.Contains(wire))
            {
                result.Add(new LogicalLink
                {
                    Source = srcDevice.Hostname,
                    Destination = dstDevice.Hostname,
                    MemberLinks = new List<Link> { wire },
                    IsFiber = wire.IsFiber,
                    MediaType = wire.MediaType
                });
            }
        }

        return result;
    }

    private LinkType DetermineLinkType(Device a, Device b, LogicalLink link)
    {
        if (a.Role == "AP" || b.Role == "AP" || a.Role == "Campus AP" || b.Role == "Campus AP")
        {
            link.IsFiber = false;
            return LinkType.AP;
        }
        if (a.Role == "Printer" || b.Role == "Printer")
        {
            link.IsFiber = false;
            return LinkType.Printer;
        }
        if (a.Role is "IP Phones" or "Phone" or "IP Phone" || b.Role is "IP Phones" or "Phone" or "IP Phone" || a.StencilKey == "ip_phone" || b.StencilKey == "ip_phone")
        {
            link.IsFiber = false;
            return LinkType.Phone;
        }
        if (link.SourcePortChannelId != null || link.DestinationPortChannelId != null)
            return LinkType.PortChannel;
        if (a.Role == "Access" && b.Role == "External")
            return LinkType.Access;

        return LinkType.Uplink;
    }

    public IEnumerable<Device> GetDevicesByRole(TopologyGraph graph, string role)
    {
        return graph.Nodes.Values.Where(d => d.Role.Equals(role, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<Device> GetOrphanDevices(TopologyGraph graph)
    {
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges)
        {
            connected.Add(edge.Source);
            connected.Add(edge.Destination);
        }

        return graph.Nodes.Values.Where(d => !connected.Contains(d.Hostname));
    }
}
