using System.Collections.Generic;
using System.Linq;
using VisioCreator.Core.Graph;
using VisioCreator.Core.Models;
using Xunit;

namespace VisioCreator.Tests;

public class GraphEngineTests
{
    [Fact]
    public void Deduplicates_Asymmetric_CDP_Links()
    {
        var devices = new List<Device>
        {
            new Device { Hostname = "SW1" },
            new Device { Hostname = "SW2" }
        };

        var rawLinks = new List<Link>
        {
            new Link { Source = "SW1", SourceInterface = "Gi1/0/1", Destination = "SW2", DestinationInterface = "Gi2/0/1" },
            new Link { Source = "SW2", SourceInterface = "Gi2/0/1", Destination = "SW1", DestinationInterface = "Gi1/0/1" } // exact reverse CDP
        };

        var engine = new GraphEngine();
        var graph = engine.BuildGraph(devices, rawLinks);

        // Should merge into 1 logical link with 1 physical wire
        Assert.Single(graph.Edges);
        Assert.Single(graph.Edges.First().MemberLinks);
    }

    [Fact]
    public void Resolves_Truncated_Neighbors()
    {
        var devices = new List<Device>
        {
            new Device { Hostname = "LONG-CORE-SWITCH-NAME-123" }
        };

        var rawLinks = new List<Link>
        {
            new Link { Source = "SW1", SourceInterface = "Gi1/0/1", Destination = "LONG-CORE-SWITCH-NA" } // truncated CDP
        };

        var engine = new GraphEngine();
        var graph = engine.BuildGraph(devices, rawLinks);

        Assert.Equal("LONG-CORE-SWITCH-NAME-123", graph.Edges.First().Destination);
    }

    [Fact]
    public void Merges_PortChannels()
    {
        var devices = new List<Device>
        {
            new Device { 
                Hostname = "SW1", 
                PortChannels = new List<PortChannel> { new PortChannel { Id = "Po1", Mode = "LACP", MemberPorts = new List<string> { "Gi1/0/1", "Gi1/0/2" } } } 
            },
            new Device { 
                Hostname = "SW2",
                PortChannels = new List<PortChannel> { new PortChannel { Id = "Po2", MemberPorts = new List<string> { "Gi2/0/1", "Gi2/0/2" } } }
            }
        };

        var rawLinks = new List<Link>
        {
            new Link { Source = "SW1", SourceInterface = "Gi1/0/1", Destination = "SW2", DestinationInterface = "Gi2/0/1" },
            new Link { Source = "SW1", SourceInterface = "Gi1/0/2", Destination = "SW2", DestinationInterface = "Gi2/0/2" }
        };

        var engine = new GraphEngine();
        var graph = engine.BuildGraph(devices, rawLinks);

        Assert.Single(graph.Edges); // 1 logical PortChannel link
        
        var pcLink = graph.Edges.First();
        Assert.Equal(LinkType.PortChannel, pcLink.Type);
        Assert.Equal("Po1", pcLink.SourcePortChannelId);
        Assert.Equal("Po2", pcLink.DestinationPortChannelId);
        Assert.Equal(2, pcLink.MemberLinks.Count);
    }
    
    [Fact]
    public void Detects_Orphans()
    {
        var devices = new List<Device>
        {
            new Device { Hostname = "SW1" },
            new Device { Hostname = "SW2" },
            new Device { Hostname = "ORPHAN" }
        };

        var rawLinks = new List<Link>
        {
            new Link { Source = "SW1", SourceInterface = "Gi1/0/1", Destination = "SW2", DestinationInterface = "Gi1/0/1" }
        };

        var engine = new GraphEngine();
        var graph = engine.BuildGraph(devices, rawLinks);

        var orphans = engine.GetOrphanDevices(graph).ToList();
        Assert.Single(orphans);
        Assert.Equal("ORPHAN", orphans.First().Hostname);
    }

    [Fact]
    public void Deduplicates_Links_With_Gig_And_Gigabit_Variants()
    {
        var devices = new List<Device>
        {
            new Device { Hostname = "SW1" },
            new Device { Hostname = "SW2" }
        };

        // Link 1 from Excel has "Gig 1/0/1" <-> "Gig 2/0/1"
        // Link 2 from Backup has "GigabitEthernet2/0/1" <-> "GigabitEthernet1/0/1" (reverse direction)
        var rawLinks = new List<Link>
        {
            new Link { Source = "SW1", SourceInterface = "Gig 1/0/1", Destination = "SW2", DestinationInterface = "Gig 2/0/1" },
            new Link { Source = "SW2", SourceInterface = "GigabitEthernet2/0/1", Destination = "SW1", DestinationInterface = "GigabitEthernet1/0/1" }
        };

        var engine = new GraphEngine();
        var graph = engine.BuildGraph(devices, rawLinks);

        // Must merge into a single logical link with 1 physical wire, not two separate links
        Assert.Single(graph.Edges);
        Assert.Single(graph.Edges.First().MemberLinks);
    }

    [Fact]
    public void Merges_PortChannels_With_Mixed_Interface_Naming()
    {
        var devices = new List<Device>
        {
            new Device { 
                Hostname = "SW1", 
                PortChannels = new List<PortChannel> { new PortChannel { Id = "Po1", Mode = "LACP", MemberPorts = new List<string> { "GigabitEthernet1/0/1", "GigabitEthernet1/0/2" } } } 
            },
            new Device { 
                Hostname = "SW2",
                PortChannels = new List<PortChannel> { new PortChannel { Id = "Po2", MemberPorts = new List<string> { "GigabitEthernet2/0/1", "GigabitEthernet2/0/2" } } }
            }
        };

        // Wires with mixed naming: "Gig 1/0/1", "Gigabit 2/0/1", "Gi1/0/2"
        var rawLinks = new List<Link>
        {
            new Link { Source = "SW1", SourceInterface = "Gig 1/0/1", Destination = "SW2", DestinationInterface = "Gigabit 2/0/1" },
            new Link { Source = "SW1", SourceInterface = "Gi1/0/2", Destination = "SW2", DestinationInterface = "Gig 2/0/2" }
        };

        var engine = new GraphEngine();
        var graph = engine.BuildGraph(devices, rawLinks);

        Assert.Single(graph.Edges);
        Assert.Equal(LinkType.PortChannel, graph.Edges.First().Type);
        Assert.Equal(2, graph.Edges.First().MemberLinks.Count);
    }

    [Fact]
    public void PortKey_Recognizes_All_Gigabit_Variants_As_Same()
    {
        var variants = new[]
        {
            "Gig 1/0/1",
            "Gigabit 1/0/1",
            "GigabitEthernet1/0/1",
            "GigabitEthernet 1/0/1",
            "Gi1/0/1",
            "Gi 1/0/1",
            "Gig1/0/1",
            "GE 1/0/1",
            "GE1/0/1",
            "Gig 1 / 0 / 1"
        };

        foreach (var a in variants)
        {
            foreach (var b in variants)
            {
                Assert.True(PortKey.Same(a, b), $"Expected '{a}' and '{b}' to be recognized as the same port.");
            }
        }

        Assert.True(PortKey.Same("Gig", "Gigabit"));
        Assert.True(PortKey.Same("Gig", "GigabitEthernet"));
    }

    [Fact]
    public void PortKey_Recognizes_IpPhone_Port_Variants_As_Same()
    {
        var phoneVariants = new[]
        {
            "Port 1",
            "Port1",
            "1",
            "P1",
            "10E37601C919:P1",
            "20CC274B8ACC:P1"
        };

        foreach (var a in phoneVariants)
        {
            foreach (var b in phoneVariants)
            {
                Assert.True(PortKey.Same(a, b), $"Expected '{a}' and '{b}' to be recognized as the same IP Phone port.");
            }
        }
    }

    [Fact]
    public void GraphEngine_Deduplicates_IpPhone_Port1_And_1_Links()
    {
        var sw = new Device { Hostname = "SW1", Role = "Access" };
        var phone = new Device { Hostname = "SEP10E37601C919", Role = "External", StencilKey = "ip_phone" };

        var links = new List<Link>
        {
            new Link { Source = "SW1", SourceInterface = "GigabitEthernet1/0/4", Destination = "SEP10E37601C919", DestinationInterface = "1", Description = "via CDP table" },
            new Link { Source = "SW1", SourceInterface = "GigabitEthernet1/0/4", Destination = "SEP10E37601C919", DestinationInterface = "Port 1", Description = "via CDP detail" },
            new Link { Source = "SW1", SourceInterface = "GigabitEthernet1/0/4", Destination = "SEP10E37601C919", DestinationInterface = "10E37601C919:P1", Description = "via LLDP" }
        };

        var engine = new GraphEngine();
        var graph = engine.BuildGraph(new[] { sw, phone }, links);

        var phoneEdges = graph.Edges.Where(e => e.Source == "SEP10E37601C919" || e.Destination == "SEP10E37601C919").ToList();
        Assert.Single(phoneEdges);

        var edge = phoneEdges[0];
        Assert.Single(edge.MemberLinks);
        var wire = edge.MemberLinks[0];
        Assert.Equal("Port 1", wire.DestinationInterface);
    }
}
