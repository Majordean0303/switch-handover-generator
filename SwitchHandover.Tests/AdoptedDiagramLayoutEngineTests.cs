// VisioCreator.Tests/DiagramLayoutEngineTests.cs
// NOT RUN YET: written against the verified Python prototype (0 crossings on this same sample network).
// If one fails, the C# port differs from the prototype: fix the port, not the test.
using System.Collections.Generic;
using System.Linq;
using VisioCreator.Core.Layout;
using Xunit;

namespace VisioCreator.Tests;

public class DiagramLayoutEngineTests
{
    private static (List<DiagramNode> Nodes, List<DiagramLink> Links) Sample()
    {
        var nodes = new List<DiagramNode>();
        void N(string id, string role, string? loc = null) => nodes.Add(new DiagramNode(id, role, loc));

        N("CSW", "Core"); N("VG_RTR", "Router"); N("CCR_WAN1", "Router"); N("NP02", "Unknown");
        N("LC02_DIST", "Distribution", "LC02 Building"); N("LC02_ACC", "Access", "LC02 Building"); N("LC02_AP", "AP", "LC02 Building");
        N("LC07_ACC", "Access", "LC07"); N("LC05_ACC", "Access", "LC05");
        N("LC08_DIST", "Distribution", "LC08 Building");
        for (int k = 1; k <= 3; k++) N($"LC08_ACC{k}", "Access", "LC08 Building");
        N("WLC", "Distribution", "CCR Ground Floor");
        N("CCR_GF_SW", "Access", "CCR Ground Floor"); N("CCR_SF_SW", "Access", "CCR Ground Floor"); N("CCR_FF_SW", "Access", "CCR Ground Floor");
        for (int k = 1; k <= 5; k++) { N($"GF_AP{k}", "AP"); N($"SF_AP{k}", "AP"); }

        var links = new List<DiagramLink>();
        void L(string a, string b, string ap, string bp, string kind = "Access", string? pc = null) =>
            links.Add(new DiagramLink($"L{links.Count}", a, b, ap, bp, kind, pc));

        L("CSW", "NP02", "", "10.30.128.113", "Uplink");
        L("CSW", "VG_RTR", "Gi0/0/2", "Gi0/0/2", "Uplink");
        L("CSW", "CCR_WAN1", "Te1/1/3", "Gi1/0/3", "Uplink");
        L("CSW", "LC02_DIST", "Twe1/0/7", "Twe1/0/1", "PortChannel", "1");
        L("CSW", "LC02_DIST", "Twe2/0/7", "Twe2/0/1", "PortChannel", "1");
        L("CSW", "LC08_DIST", "Twe1/0/9", "Twe1/0/1", "PortChannel", "2");
        L("CSW", "LC08_DIST", "Twe2/0/9", "Twe2/0/1", "PortChannel", "2");
        L("CSW", "WLC", "Twe1/0/17", "Ten0/1/0", "Uplink");
        L("CSW", "WLC", "Twe2/0/17", "Ten0/1/0", "Uplink");
        L("CSW", "CCR_GF_SW", "Twe1/0/11", "Gig1/1/1", "Uplink");
        L("CSW", "CCR_GF_SW", "Twe2/0/11", "Gig3/1/1", "Uplink");
        L("CSW", "CCR_SF_SW", "Twe1/0/13", "Gig1/1/1", "Uplink");
        L("CSW", "CCR_SF_SW", "Twe2/0/13", "Gig2/1/1", "Uplink");
        L("CSW", "CCR_FF_SW", "Twe1/0/24", "Te1/0/21", "Uplink");
        L("CSW", "LC05_ACC", "Twe1/0/15", "Ten1/1/3", "Uplink");
        L("LC02_DIST", "LC02_ACC", "Twe1/0/3", "Ten1/1/3", "Uplink");
        L("LC02_DIST", "LC07_ACC", "Twe1/0/5", "Ten1/1/3", "Uplink");
        for (int k = 1; k <= 3; k++) L("LC08_DIST", $"LC08_ACC{k}", $"Te1/0/{k}", "Te1/1/1", "Uplink");
        L("LC02_ACC", "LC02_AP", "Gi1/0/8", "", "AP");
        L("LC07_ACC", "LC05_ACC", "Ten1/1/4", "Ten1/1/3");
        for (int k = 1; k <= 5; k++)
        {
            L("CCR_GF_SW", $"GF_AP{k}", $"Gig1/0/{21 + k}", "", "AP");
            L("CCR_SF_SW", $"SF_AP{k}", $"Gig2/0/{21 + k}", "", "AP");
        }
        return (nodes, links);
    }

    [Fact]
    public void No_node_overlaps_another_node()
    {
        var (n, l) = Sample();
        var layout = DiagramLayoutEngine.Build(n, l);
        var list = layout.Nodes.Values.ToList();
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                var a = list[i]; var b = list[j];
                bool overlap = a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
                Assert.False(overlap, $"{a.Id} overlaps {b.Id}");
            }
    }

    [Fact]
    public void No_link_passes_through_an_unrelated_node()
    {
        var (n, l) = Sample();
        var layout = DiagramLayoutEngine.Build(n, l);
        Assert.Empty(DiagramLayoutEngine.FindNodeCrossings(layout));
    }

    [Fact]
    public void Every_link_is_routed_and_none_are_skipped()
    {
        var (n, l) = Sample();
        var layout = DiagramLayoutEngine.Build(n, l);
        Assert.Empty(layout.SkippedLinks);
        Assert.All(layout.Links, x => Assert.True(x.Points.Count >= 2));
    }

    [Fact]
    public void Layout_is_deterministic()
    {
        var (n1, l1) = Sample();
        var (n2, l2) = Sample();
        var a = DiagramLayoutEngine.Build(n1, l1);
        var b = DiagramLayoutEngine.Build(n2, l2);
        foreach (var id in a.Nodes.Keys)
        {
            Assert.Equal(a.Nodes[id].X, b.Nodes[id].X, 6);
            Assert.Equal(a.Nodes[id].Y, b.Nodes[id].Y, 6);
        }
    }

    [Fact]
    public void Located_nodes_sit_inside_their_location_container()
    {
        var (n, l) = Sample();
        var layout = DiagramLayoutEngine.Build(n, l);
        foreach (var node in layout.Nodes.Values.Where(x => x.Location != null))
        {
            var c = layout.Containers.Single(k => k.Name == node.Location);
            Assert.True(node.X >= c.X && node.Right <= c.X + c.W && node.Y >= c.Y && node.Bottom <= c.CaptionY,
                $"{node.Id} is outside container {c.Name}");
        }
    }

    [Fact]
    public void Core_row_is_above_all_containers_and_external_is_above_core()
    {
        var (n, l) = Sample();
        var layout = DiagramLayoutEngine.Build(n, l);
        double coreBottom = layout.Nodes["CSW"].Bottom;
        Assert.All(layout.Containers, c => Assert.True(c.Y > coreBottom));
        Assert.True(layout.Nodes["NP02"].Bottom < layout.Nodes["CSW"].Y);
    }

    [Fact]
    public void Ports_on_the_same_edge_get_distinct_slots()
    {
        var (n, l) = Sample();
        var layout = DiagramLayoutEngine.Build(n, l);
        var starts = layout.Links.Where(x => x.A == "CSW" && x.ASide == "bottom")
                           .Select(x => x.Points[0].X).ToList();
        Assert.Equal(starts.Count, starts.Distinct().Count());
    }

    [Fact]
    public void Dragging_a_node_reroutes_its_links_only_to_the_new_position()
    {
        var (n, l) = Sample();
        var layout = DiagramLayoutEngine.Build(n, l);
        layout.Nodes["LC02_AP"].X += 5;
        DiagramLayoutEngine.Reroute(layout);
        var link = layout.Links.Single(x => x.B == "LC02_AP");
        Assert.InRange(link.Points[^1].X, layout.Nodes["LC02_AP"].X, layout.Nodes["LC02_AP"].Right);
    }

    [Fact]
    public void Vertical_links_have_vertical_orientation_and_correct_label_offsets()
    {
        var (n, l) = Sample();
        var layout = DiagramLayoutEngine.Build(n, l);
        var verticalLink = layout.Links.First(x => x.AIsVertical);
        Assert.True(verticalLink.ASide == "top" || verticalLink.ASide == "bottom");
        Assert.True(verticalLink.AIsVertical);
        // Vertical link label X is slightly offset to the right of the vertical wire (X > Points[0].X)
        Assert.True(verticalLink.ALabel.X > verticalLink.Points[0].X);
    }

    [Fact]
    public void Tree_structure_layout_when_no_locations()
    {
        var nodes = new List<DiagramNode>
        {
            new("ISR4221", "Router", null),
            new("WS-C2960X", "Core", null),
            new("AP-1", "AP", null),
            new("AP-2", "AP", null),
            new("POE-Switch", "Access", null)
        };
        for (int i = 1; i <= 14; i++)
        {
            nodes.Add(new($"Phone-{i}", "Phone", null));
        }

        var links = new List<DiagramLink>
        {
            new("L0", "ISR4221", "WS-C2960X", "Gi0/0/0", "Gi1/0/1", "Uplink"),
            new("L1", "WS-C2960X", "AP-1", "Gi1/0/2", "Eth0", "AP"),
            new("L2", "WS-C2960X", "AP-2", "Gi1/0/3", "Eth0", "AP"),
            new("L3", "WS-C2960X", "POE-Switch", "Gi1/0/4", "Gi0/1", "Uplink")
        };
        for (int i = 1; i <= 14; i++)
        {
            links.Add(new($"LP{i}", "POE-Switch", $"Phone-{i}", $"Fa0/{i}", "Port 1", "Access"));
        }

        var layout = DiagramLayoutEngine.Build(nodes, links);

        // 1. No container boxes when locations are absent
        Assert.Empty(layout.Containers);

        // 2. Hierarchical tiers: Router on top, Core below, Access below Core, Phones below Access
        var router = layout.Nodes["ISR4221"];
        var core = layout.Nodes["WS-C2960X"];
        var poe = layout.Nodes["POE-Switch"];
        var phone1 = layout.Nodes["Phone-1"];
        var phone9 = layout.Nodes["Phone-9"];

        Assert.True(core.Y >= router.Bottom);
        Assert.True(poe.Y >= core.Bottom);
        Assert.True(phone1.Y >= poe.Bottom);
        // Second row of phones below first row
        Assert.True(phone9.Y >= phone1.Bottom);

        // 3. No overlaps among any nodes
        var all = layout.Nodes.Values.ToList();
        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                var a = all[i]; var b = all[j];
                bool overlap = a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
                Assert.False(overlap, $"{a.Id} overlaps {b.Id}");
            }
        }
    }

    [Fact]
    public void Dynamic_line_routing_when_dragged()
    {
        var nodes = new List<DiagramNode>
        {
            new("A", "Access", null),
            new("B", "Access", null)
        };
        var links = new List<DiagramLink>
        {
            new("L0", "A", "B", "Fa0/1", "Fa0/2", "Access")
        };

        var layout = DiagramLayoutEngine.Build(nodes, links);
        var link = layout.Links[0];

        // Initially B is placed relative to A
        // Case 1: B dragged clearly below A
        layout.Nodes["A"].X = 10; layout.Nodes["A"].Y = 5;
        layout.Nodes["B"].X = 10; layout.Nodes["B"].Y = 12;
        DiagramLayoutEngine.Reroute(layout);
        Assert.Equal("bottom", link.ASide);
        Assert.Equal("top", link.BSide);

        // Case 2: B dragged to the right of A, vertically aligned
        layout.Nodes["B"].X = 20; layout.Nodes["B"].Y = 5;
        DiagramLayoutEngine.Reroute(layout);
        Assert.Equal("right", link.ASide);
        Assert.Equal("left", link.BSide);

        // Case 3: B dragged clearly above A
        layout.Nodes["B"].X = 10; layout.Nodes["B"].Y = 1;
        DiagramLayoutEngine.Reroute(layout);
        Assert.Equal("top", link.ASide);
        Assert.Equal("bottom", link.BSide);
    }
}
