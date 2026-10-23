// NetHealth.Core/Topology/DiagramLayoutEngine.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace VisioCreator.Core.Layout;

public sealed class DiagramOptions
{
    public double NodeW { get; set; } = 2.4;
    public double NodeH { get; set; } = 1.35;
    public double LeafW { get; set; } = 1.8;
    public double LeafH { get; set; } = 1.15;
    public double HGap { get; set; } = 0.6;
    public double TopGap { get; set; } = 1.5;          // gap between devices on the core row (room for 2 port labels)
    public double TierGap { get; set; } = 2.2;         // vertical gap between rows (room for link lanes)
    public double BandGap { get; set; } = 2.6;
    public double BlockGap { get; set; } = 0.9;
    public double Pad { get; set; } = 0.45;
    public double CaptionH { get; set; } = 0.45;
    public double LabelW { get; set; } = 0.8;
    public double LabelH { get; set; } = 0.17;
    public double SlotPitch { get; set; } = 0.5;       // min horizontal space per link on a node edge
    public double SlotMargin { get; set; } = 0.3;
    public int MaxRowNodes { get; set; } = 6;          // access switches per row inside a container
    public int MaxLeafRow { get; set; } = 8;           // APs per parent per row
    public double MaxLeafRowW { get; set; } = 30.0;
    public double MaxCanvasW { get; set; } = 70.0;     // containers wrap to a new band beyond this
    public double ChannelStepMin { get; set; } = 0.07;
}

public readonly record struct RoutePoint(double X, double Y);

public sealed class DiagramNode
{
    public DiagramNode(string id, string role, string? location)
    {
        Id = id;
        Role = role;
        Location = location;
    }

    public string Id
    {
        get;
    }
    /// Core, Distribution, Access, Router, Firewall, AP, Printer, External, Unknown
    public string Role
    {
        get;
    }
    public string? Location
    {
        get; set;
    }
    public string? Parent
    {
        get; internal set;
    }

    public double X
    {
        get; set;
    }
    public double Y
    {
        get; set;
    }
    public double W
    {
        get; set;
    }
    public double H
    {
        get; set;
    }

    public double CenterX => X + W / 2;
    public double CenterY => Y + H / 2;
    public double Right => X + W;
    public double Bottom => Y + H;
}

public sealed class DiagramLink
{
    public DiagramLink(string id, string a, string b, string aPort, string bPort, string kind, string? portChannelId = null, bool isFiber = false, string? mediaType = null)
    {
        Id = id;
        A = a;
        B = b;
        APort = aPort;
        BPort = bPort;
        Kind = kind;
        PortChannelId = portChannelId;
        IsFiber = isFiber;
        MediaType = mediaType;
    }

    public string Id
    {
        get;
    }
    public string A
    {
        get;
    }
    public string B
    {
        get;
    }
    public string APort
    {
        get;
    }
    public string BPort
    {
        get;
    }
    /// Uplink, PortChannel, AP, Printer, Access (drives colour and line style)
    public string Kind
    {
        get;
    }
    public string? PortChannelId
    {
        get;
    }
    public bool IsFiber
    {
        get; set;
    }
    public string? MediaType
    {
        get; set;
    }

    // ---- results
    public List<RoutePoint> Points { get; internal set; } = new();
    /// Top-left corner of the label boxes (size = DiagramOptions.LabelW x LabelH)
    public RoutePoint ALabel
    {
        get; internal set;
    }
    public RoutePoint BLabel
    {
        get; internal set;
    }
    public string ASide { get; internal set; } = "";
    public string BSide { get; internal set; } = "";

    public bool AIsVertical => ASide is "top" or "bottom";
    public bool BIsVertical => BSide is "top" or "bottom";
}

public sealed class DiagramContainer
{
    public DiagramContainer(string name, double x, double y, double w, double h, double captionH)
    {
        Name = name;
        X = x;
        Y = y;
        W = w;
        H = h;
        CaptionH = captionH;
    }
    public string Name
    {
        get;
    }
    public double X
    {
        get; set;
    }
    public double Y
    {
        get; set;
    }
    public double W
    {
        get; set;
    }
    public double H
    {
        get; set;
    }
    public double CaptionH
    {
        get;
    }
    /// Caption strip is the bottom CaptionH of the container.
    public double CaptionY => Y + H - CaptionH;
}

/// Oval capsule around member ports of a port-channel
public sealed class DiagramHighlight
{
    public double X
    {
        get; set;
    }
    public double Y
    {
        get; set;
    }
    public double W
    {
        get; set;
    }
    public double H
    {
        get; set;
    }
    public string Text { get; init; } = "";
}

public sealed class DiagramLayout
{
    public Dictionary<string, DiagramNode> Nodes { get; internal set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DiagramLink> Links { get; internal set; } = new();
    public List<DiagramContainer> Containers { get; internal set; } = new();
    public List<DiagramHighlight> Highlights { get; internal set; } = new();
    public List<DiagramLink> SkippedLinks { get; } = new();
    public DiagramLayout CreateSnapshot()
    {
        var copy = new DiagramLayout { Width = Width, Height = Height };
        foreach (var n in Nodes.Values)
            copy.Nodes[n.Id] = new DiagramNode(n.Id, n.Role, n.Location) { Parent = n.Parent, X = n.X, Y = n.Y, W = n.W, H = n.H };
        foreach (var l in Links)
            copy.Links.Add(new DiagramLink(l.Id, l.A, l.B, l.APort, l.BPort, l.Kind, l.PortChannelId, l.IsFiber, l.MediaType)
            {
                Points = new List<RoutePoint>(l.Points),
                ALabel = l.ALabel,
                BLabel = l.BLabel,
                ASide = l.ASide,
                BSide = l.BSide
            });
        copy.Containers = Containers.Select(c => new DiagramContainer(c.Name, c.X, c.Y, c.W, c.H, c.CaptionH)).ToList();
        copy.Highlights = Highlights.Select(h => new DiagramHighlight { X = h.X, Y = h.Y, W = h.W, H = h.H, Text = h.Text }).ToList();
        return copy;
    }

    public double Width
    {
        get; internal set;
    }
    public double Height
    {
        get; internal set;
    }
}

public static class DiagramLayoutEngine
{
    private static bool IsLeaf(string role) =>
        role is "AP" or "Campus AP" or "Printer" or "Phone" or "IP Phone" or "IP Phones";

    private static bool IsTop(string role) =>
        role is "Core" or "Core Switch" or "Firewall" or "Router" or "External" or "Unknown" or "WLC" or "WLC Controller";

    private static bool IsExternal(string role) =>
        role is "External" or "Unknown";

    private static int TopOrder(string role) => role switch
    {
        "Firewall" => 0,
        "Router" => 1,
        "Core" or "Core Switch" => 2,
        "WLC" or "WLC Controller" => 3,
        _ => 4
    };

    private static int TierOf(DiagramNode n) =>
        IsLeaf(n.Role) ? 3 : (IsTop(n.Role) ? 0 : (n.Role is "Distribution" or "Distribution Switch" ? 1 : 2));

    // =====================================================================================
    // Public API
    // =====================================================================================

    /// Full layout: place nodes and containers, then route every link.
    public static DiagramLayout Build(IEnumerable<DiagramNode> nodeList, IEnumerable<DiagramLink> linkList, DiagramOptions? options = null)
    {
        var o = options ?? new DiagramOptions();
        var layout = new DiagramLayout();

        var nodes = new Dictionary<string, DiagramNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodeList)
            nodes[n.Id] = n;
        if (nodes.Count == 0)
            return layout;

        var links = new List<DiagramLink>();
        foreach (var l in linkList)
        {
            if (l.A.Equals(l.B, StringComparison.OrdinalIgnoreCase) || !nodes.ContainsKey(l.A) || !nodes.ContainsKey(l.B))
            {
                layout.SkippedLinks.Add(l);
                continue;
            }
            links.Add(l);
        }

        layout.Nodes = nodes;
        layout.Links = links;
        layout.Containers = PlaceNodes(nodes, links, o);
        layout.Highlights = RouteLinks(nodes, links, o);
        Normalize(layout, 0.6, o);
        return layout;
    }

    /// Re-route all links after nodes were moved (drag in the preview). Node sizes are kept.
    public static void Reroute(DiagramLayout layout, DiagramOptions? options = null)
    {
        var o = options ?? new DiagramOptions();
        layout.Highlights = RouteLinks(layout.Nodes, layout.Links, o);
        // Dragging must also expand export bounds; do not normalize/reposition the user's nodes.
        layout.Width = Math.Max(layout.Width, layout.Nodes.Values.Select(n => n.Right).DefaultIfEmpty(0).Max() + 0.6);
        layout.Height = Math.Max(layout.Height, layout.Nodes.Values.Select(n => n.Bottom).DefaultIfEmpty(0).Max() + 0.6);
        foreach (var link in layout.Links)
        {
            foreach (var point in link.Points.Append(link.ALabel).Append(link.BLabel))
            {
                layout.Width = Math.Max(layout.Width, point.X + o.LabelW + 0.6);
                layout.Height = Math.Max(layout.Height, point.Y + o.LabelH + 0.6);
            }
        }
    }

    /// Link segments passing through a node that is not one of the link's own ends. Should be empty.
    public static List<(string LinkId, string NodeId)> FindNodeCrossings(DiagramLayout layout, double eps = 0.02)
    {
        var bad = new List<(string, string)>();
        foreach (var l in layout.Links)
        {
            for (int i = 0; i + 1 < l.Points.Count; i++)
            {
                var p = l.Points[i];
                var q = l.Points[i + 1];
                double loX = Math.Min(p.X, q.X), hiX = Math.Max(p.X, q.X);
                double loY = Math.Min(p.Y, q.Y), hiY = Math.Max(p.Y, q.Y);
                foreach (var n in layout.Nodes.Values)
                {
                    if (n.Id == l.A || n.Id == l.B)
                        continue;
                    if (hiX > n.X + eps && loX < n.Right - eps && hiY > n.Y + eps && loY < n.Bottom - eps)
                        bad.Add((l.Id, n.Id));
                }
            }
        }
        return bad;
    }

    // =====================================================================================
    // Placement
    // =====================================================================================

    private sealed record BlockBox(string Name, List<DiagramNode> Nodes, double W, double H);

    private static List<DiagramContainer> PlaceNodes(Dictionary<string, DiagramNode> nodes, List<DiagramLink> links, DiagramOptions o)
    {
        if (nodes.Count == 0)
            return new List<DiagramContainer>();

        bool hasRealLocations = nodes.Values.Any(n =>
            !string.IsNullOrWhiteSpace(n.Location) &&
            !n.Location.Equals("Unassigned", StringComparison.OrdinalIgnoreCase));

        if (!hasRealLocations)
        {
            return PlaceNodesTree(nodes, links, o);
        }

        // ---- sizes: widen a node so every link on its top/bottom edge gets its own slot
        var topCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var botCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Inc(Dictionary<string, int> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;

        foreach (var l in links)
        {
            var a = nodes[l.A];
            var b = nodes[l.B];
            int ta = TierOf(a), tb = TierOf(b);
            if (ta == tb)
            {
                Inc(topCount, a.Id);
                Inc(topCount, b.Id);
            }
            else if (ta < tb)
            {
                Inc(botCount, a.Id);
                Inc(topCount, b.Id);
            }
            else
            {
                Inc(botCount, b.Id);
                Inc(topCount, a.Id);
            }
        }
        foreach (var n in nodes.Values)
        {
            double baseW = IsLeaf(n.Role) ? o.LeafW : o.NodeW;
            double baseH = IsLeaf(n.Role) ? o.LeafH : o.NodeH;
            double need = Math.Max(topCount.GetValueOrDefault(n.Id), botCount.GetValueOrDefault(n.Id)) * o.SlotPitch + 2 * o.SlotMargin;
            n.W = Math.Max(baseW, need);
            n.H = baseH;
            n.Parent = null;
        }

        var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes.Values)
            adj[n.Id] = new List<string>();
        foreach (var l in links)
        {
            adj[l.A].Add(l.B);
            adj[l.B].Add(l.A);
        }

        // ---- leaves (APs, printers, phones) belong to the switch they hang off
        foreach (var n in nodes.Values.Where(x => IsLeaf(x.Role)))
        {
            var parent = adj[n.Id]
                .Where(m => nodes.ContainsKey(m) && !IsLeaf(nodes[m].Role))
                .OrderBy(m => TierOf(nodes[m]) == 0)          // real switches before core/router
                .ThenBy(m => m, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (parent != null)
            {
                n.Parent = parent;
                n.Location ??= nodes[parent].Location;
            }
        }

        // ---- group into location blocks; core/router/firewall/external form the top rows
        var blocks = new Dictionary<string, List<DiagramNode>>(StringComparer.OrdinalIgnoreCase);
        var tops = new List<DiagramNode>();
        foreach (var n in nodes.Values)
        {
            if (TierOf(n) == 0 && !IsLeaf(n.Role))
                tops.Add(n);
            else
            {
                var key = n.Location ?? "Unassigned";
                if (!blocks.TryGetValue(key, out var list))
                    blocks[key] = list = new List<DiagramNode>();
                list.Add(n);
            }
        }

        var containers = new List<DiagramContainer>();
        var blockBoxes = new List<BlockBox>();

        foreach (var name in blocks.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var bn = blocks[name];
            var t1 = bn.Where(n => TierOf(n) == 1).OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToList();
            var t2 = bn.Where(n => TierOf(n) == 2).ToList();
            var lf = bn.Where(n => TierOf(n) == 3).ToList();
            var t1Ids = new HashSet<string>(t1.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);

            int ParentIndex(DiagramNode n)
            {
                int best = t1.Count;
                for (int i = 0; i < t1.Count; i++)
                    if (adj[n.Id].Contains(t1[i].Id))
                    {
                        best = i;
                        break;
                    }
                return best;
            }
            t2 = t2.OrderBy(ParentIndex).ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToList();

            var rows = new List<List<DiagramNode>>();
            if (t1.Count > 0)
                rows.Add(t1);
            for (int i = 0; i < t2.Count; i += o.MaxRowNodes)
                rows.Add(t2.Skip(i).Take(o.MaxRowNodes).ToList());

            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int k = 0;
            foreach (var r in rows)
            foreach (var n in r)
                order[n.Id] = k++;

            foreach (var n in t2)
            {
                var ps = adj[n.Id].Where(m => t1Ids.Contains(m)).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
                n.Parent = ps.Count > 0 ? ps[0] : null;
            }
            lf = lf.OrderBy(n => n.Parent != null && order.TryGetValue(n.Parent, out var oi) ? oi : int.MaxValue)
                   .ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToList();

            // ---- tier rows (local coordinates)
            double y = 0.0;
            var placed = new Dictionary<string, DiagramNode>(StringComparer.OrdinalIgnoreCase);
            double? t1Right = null;
            foreach (var r in rows)
            {
                double rh = r.Max(n => n.H);
                double? cursor = null;
                foreach (var n in r)
                {
                    double want;
                    if (n.Parent != null && placed.TryGetValue(n.Parent, out var pn))
                        want = pn.CenterX - n.W / 2;
                    else if (t1Right != null && TierOf(n) == 2)
                        want = t1Right.Value + o.HGap;   // never sit under a distribution node it is not linked to
                    else
                        want = 0;

                    double x = cursor == null ? want : Math.Max(want, cursor.Value + o.HGap);
                    n.X = x;
                    n.Y = y;
                    cursor = x + n.W;
                    placed[n.Id] = n;
                }
                if (r.All(n => TierOf(n) == 1))
                    t1Right = r.Max(n => n.Right);
                y += rh + o.TierGap;
            }

            // ---- leaf rows: one group per parent, centred under the parent
            var groups = lf.GroupBy(n => n.Parent ?? "").ToList();
            var glist = groups
                .OrderBy(g => placed.TryGetValue(g.Key, out var p) ? p.CenterX : 1e9)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pieces = new List<(string Parent, List<DiagramNode> Leaves)>();
            foreach (var g in glist)
            {
                var ls = g.ToList();
                for (int i = 0; i < ls.Count; i += o.MaxLeafRow)
                    pieces.Add((g.Key, ls.Skip(i).Take(o.MaxLeafRow).ToList()));
            }

            double GroupW(List<DiagramNode> ls) => ls.Sum(n => n.W) + (ls.Count - 1) * o.HGap;

            var leafRows = new List<List<(string Parent, List<DiagramNode> Leaves)>>();
            var cur = new List<(string Parent, List<DiagramNode> Leaves)>();
            double curW = 0.0;
            foreach (var piece in pieces)
            {
                double w = GroupW(piece.Leaves);
                if (cur.Count > 0 && curW + o.HGap + w > o.MaxLeafRowW)
                {
                    leafRows.Add(cur);
                    cur = new();
                    curW = 0.0;
                }
                cur.Add(piece);
                curW += w + (cur.Count > 1 ? o.HGap : 0);
            }
            if (cur.Count > 0)
                leafRows.Add(cur);

            foreach (var lrow in leafRows)
            {
                double rh = lrow.Max(p => p.Leaves.Max(n => n.H));
                double? cursor = null;
                foreach (var (parent, ls) in lrow)
                {
                    double w = GroupW(ls);
                    double want = placed.TryGetValue(parent, out var pn)
                        ? pn.CenterX - w / 2
                        : (cursor == null ? 0 : cursor.Value + o.HGap);
                    double x = cursor == null ? want : Math.Max(want, cursor.Value + o.HGap);
                    foreach (var n in ls)
                    {
                        n.X = x;
                        n.Y = y;
                        placed[n.Id] = n;
                        x += n.W + o.HGap;
                    }
                    cursor = x - o.HGap;
                }
                y += rh + o.TierGap;
            }
            y -= o.TierGap;

            double minX = bn.Min(n => n.X), maxX = bn.Max(n => n.Right);
            foreach (var n in bn)
                n.X -= minX;
            blockBoxes.Add(new BlockBox(name, bn, maxX - minX, y));
        }

        // ---- arrange blocks left to right, wrapping into bands
        var bands = new List<(List<BlockBox> Blocks, double Width)>();
        var curBand = new List<BlockBox>();
        double curBandW = 0;
        foreach (var bb in blockBoxes)
        {
            double bw = bb.W + 2 * o.Pad;
            if (curBand.Count > 0 && curBandW + bw + o.BlockGap > o.MaxCanvasW)
            {
                bands.Add((curBand, curBandW));
                curBand = new();
                curBandW = 0;
            }
            curBand.Add(bb);
            curBandW += bw + (curBand.Count > 1 ? o.BlockGap : 0);
        }
        if (curBand.Count > 0)
            bands.Add((curBand, curBandW));

        double canvasW = bands.Count > 0 ? bands.Max(b => b.Width) : 0;
        double bandY = 0.0;
        foreach (var (bandBlocks, _) in bands)
        {
            double x0 = 0.0, bandH = 0.0;
            foreach (var bb in bandBlocks)
            {
                double cx0 = x0 + o.Pad, cy0 = bandY + o.Pad;
                foreach (var n in bb.Nodes)
                {
                    n.X += cx0;
                    n.Y += cy0;
                }
                double cw = bb.W + 2 * o.Pad;
                double ch = bb.H + 2 * o.Pad + o.CaptionH;
                containers.Add(new DiagramContainer(bb.Name, x0, bandY, cw, ch, o.CaptionH));
                x0 += cw + o.BlockGap;
                bandH = Math.Max(bandH, ch);
            }
            bandY += bandH + o.BandGap;
        }

        // ---- top rows: externals above; core/routers/firewalls below them, routers hugging the core
        var ext = tops.Where(n => IsExternal(n.Role)).OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var midAll = tops.Where(n => !IsExternal(n.Role)).ToList();
        var cores = midAll.Where(n => n.Role is "Core" or "Core Switch").OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var others = midAll.Where(n => n.Role is not "Core" and not "Core Switch")
                           .OrderBy(n => TopOrder(n.Role)).ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var mid = new List<DiagramNode>(cores);
        for (int i = 0; i < others.Count; i++)
        {
            if (i % 2 == 0)
                mid.Add(others[i]);
            else
                mid.Insert(0, others[i]);
        }

        double RowW(List<DiagramNode> r) => r.Sum(n => n.W) + o.TopGap * Math.Max(r.Count - 1, 0);
        double midW = RowW(mid), extW = RowW(ext);
        double span = Math.Max(canvasW, Math.Max(midW, extW));

        double ty = 0.0;
        if (ext.Count > 0)
        {
            double x = (span - extW) / 2;
            foreach (var n in ext)
            {
                n.X = x;
                n.Y = ty;
                x += n.W + o.TopGap;
            }
            ty += ext.Max(n => n.H) + o.TierGap;
        }
        double midBottom = ty;
        if (mid.Count > 0)
        {
            double x = (span - midW) / 2;
            foreach (var n in mid)
            {
                n.X = x;
                n.Y = ty;
                x += n.W + o.TopGap;
            }
            midBottom = ty + mid.Max(n => n.H);
        }

        if (tops.Count > 0)
        {
            double shift = midBottom + o.BandGap;
            foreach (var c in containers)
                c.Y += shift;
            foreach (var bb in blockBoxes)
            foreach (var n in bb.Nodes)
                n.Y += shift;
        }
        return containers;
    }

    private static List<DiagramContainer> PlaceNodesTree(Dictionary<string, DiagramNode> nodes, List<DiagramLink> links, DiagramOptions o)
    {
        if (nodes.Count == 0)
            return new List<DiagramContainer>();

        // 1. Calculate node sizes
        var topCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var botCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Inc(Dictionary<string, int> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;

        foreach (var l in links)
        {
            Inc(botCount, l.A);
            Inc(topCount, l.B);
        }

        foreach (var n in nodes.Values)
        {
            double baseW = IsLeaf(n.Role) ? o.LeafW : o.NodeW;
            double baseH = IsLeaf(n.Role) ? o.LeafH : o.NodeH;
            double need = Math.Max(topCount.GetValueOrDefault(n.Id), botCount.GetValueOrDefault(n.Id)) * o.SlotPitch + 2 * o.SlotMargin;
            n.W = Math.Max(baseW, need);
            n.H = baseH;
            n.Parent = null;
        }

        // 2. Adjacency
        var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes.Values)
            adj[n.Id] = new List<string>();
        foreach (var l in links)
        {
            adj[l.A].Add(l.B);
            adj[l.B].Add(l.A);
        }

        // 3. Classify leaves and assign parents
        var leaves = new List<DiagramNode>();
        var nonLeaves = new List<DiagramNode>();

        foreach (var n in nodes.Values)
        {
            bool isExplicitLeaf = IsLeaf(n.Role);
            bool isEndpoint = !isExplicitLeaf && (n.Role is "External" or "Unknown") && adj[n.Id].Count <= 1;

            if (isExplicitLeaf || isEndpoint)
            {
                leaves.Add(n);
                var parent = adj[n.Id]
                    .Where(m => nodes.ContainsKey(m) && !IsLeaf(nodes[m].Role))
                    .OrderBy(m => nodes[m].Role is "Access" or "Access Switch" ? 0 : (nodes[m].Role is "Distribution" or "Distribution Switch" ? 1 : 2))
                    .ThenBy(m => m, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                n.Parent = parent;
            }
            else
            {
                nonLeaves.Add(n);
            }
        }

        // 4. Classify non-leaf devices into tiers:
        var routers = nonLeaves.Where(n => n.Role is "Router" or "Firewall").OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var switches = nonLeaves.Where(n => n.Role is not "Router" and not "Firewall").ToList();

        // Identify Core / Seed switch:
        DiagramNode? coreSwitch = switches.FirstOrDefault(n => n.Role is "Core" or "Core Switch");
        if (coreSwitch == null && switches.Count > 0)
        {
            coreSwitch = switches.OrderByDescending(s => adj[s.Id].Count(m => !leaves.Any(l => l.Id == m)))
                                 .ThenByDescending(s => adj[s.Id].Count)
                                 .First();
        }

        // Identify peer distribution / server / WLC switches:
        var peerSwitches = new List<DiagramNode>();
        var accessSwitches = new List<DiagramNode>();

        foreach (var s in switches)
        {
            if (coreSwitch != null && s.Id == coreSwitch.Id)
                continue;

            bool isExplicitAccess = s.Role is "Access" or "Access Switch" ||
                                    System.Text.RegularExpressions.Regex.IsMatch(s.Id, @"\b(?:acc|access|edge|gf|ff|sf|floor|wing)\b|_(?:acc|asw|gf|ff|sf|cc\d*)_", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            bool isSecondaryCore = System.Text.RegularExpressions.Regex.IsMatch(s.Id, @"\b(?:core[-_]?2|sec(?:ondary)?[-_]?core|core[-_]?sec)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            bool isPeer = !isExplicitAccess && (
                          s.Role is "Distribution" or "Distribution Switch" ||
                          s.Role.Contains("WLC", StringComparison.OrdinalIgnoreCase) ||
                          s.Id.Contains("WLC", StringComparison.OrdinalIgnoreCase) ||
                          s.Id.Contains("Server", StringComparison.OrdinalIgnoreCase) ||
                          s.Id.Contains("Dist", StringComparison.OrdinalIgnoreCase) ||
                          s.Id.Contains("Agg", StringComparison.OrdinalIgnoreCase) ||
                          isSecondaryCore);

            if (isPeer)
                peerSwitches.Add(s);
            else
                accessSwitches.Add(s);
        }

        string GetParentUplink(DiagramNode s)
        {
            var p = adj[s.Id].FirstOrDefault(m => peerSwitches.Any(ps => ps.Id == m) || (coreSwitch != null && coreSwitch.Id == m));
            return p ?? "";
        }
        accessSwitches = accessSwitches
            .OrderBy(s => GetParentUplink(s), StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        peerSwitches = peerSwitches.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToList();

        // 5. Layout calculations:
        var accessClusters = new List<(DiagramNode Switch, List<List<DiagramNode>> LeafRows, double ClusterW, double ClusterH)>();

        foreach (var sw in accessSwitches)
        {
            var swLeaves = leaves.Where(l => l.Parent == sw.Id).OrderBy(l => l.Id, StringComparer.OrdinalIgnoreCase).ToList();
            var rows = new List<List<DiagramNode>>();
            for (int i = 0; i < swLeaves.Count; i += o.MaxLeafRow)
            {
                rows.Add(swLeaves.Skip(i).Take(o.MaxLeafRow).ToList());
            }

            double maxRowW = 0;
            foreach (var r in rows)
            {
                double rw = r.Sum(l => l.W) + (r.Count - 1) * o.HGap;
                maxRowW = Math.Max(maxRowW, rw);
            }

            double clusterW = Math.Max(sw.W, maxRowW);
            double clusterH = sw.H;
            if (rows.Count > 0)
            {
                clusterH += o.TierGap + rows.Count * o.LeafH + Math.Max(0, rows.Count - 1) * 0.8;
            }
            accessClusters.Add((sw, rows, clusterW, clusterH));
        }

        int maxPerRow = Math.Max(1, o.MaxRowNodes);
        int numRows = (int)Math.Ceiling((double)accessClusters.Count / maxPerRow);
        int itemsPerRow = (int)Math.Ceiling((double)accessClusters.Count / Math.Max(1, numRows));

        var clusterRows = new List<List<(DiagramNode Switch, List<List<DiagramNode>> LeafRows, double ClusterW, double ClusterH)>>();
        for (int i = 0; i < accessClusters.Count; i += itemsPerRow)
        {
            clusterRows.Add(accessClusters.Skip(i).Take(itemsPerRow).ToList());
        }

        double RowClusterW(List<(DiagramNode Switch, List<List<DiagramNode>> LeafRows, double ClusterW, double ClusterH)> r) =>
            r.Sum(c => c.ClusterW) + Math.Max(0, r.Count - 1) * (o.HGap * 2.0);

        double accessMaxRowW = clusterRows.Count > 0 ? clusterRows.Max(RowClusterW) : 0;

        var coreLeaves = coreSwitch != null
            ? leaves.Where(l => l.Parent == coreSwitch.Id).OrderBy(l => IsLeaf(l.Role) ? 0 : 1).ThenBy(l => l.Id, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<DiagramNode>();

        var coreLeafRows = new List<List<DiagramNode>>();
        for (int i = 0; i < coreLeaves.Count; i += 4)
        {
            coreLeafRows.Add(coreLeaves.Skip(i).Take(4).ToList());
        }
        double coreLeavesW = 0;
        foreach (var r in coreLeafRows)
        {
            double rw = r.Sum(l => l.W) + (r.Count - 1) * o.HGap;
            coreLeavesW = Math.Max(coreLeavesW, rw);
        }

        double coreW = coreSwitch?.W ?? 0;
        double peersW = peerSwitches.Count > 0
            ? peerSwitches.Sum(p => p.W) + (peerSwitches.Count - 1) * o.TopGap
            : 0;

        double midRowW = (coreLeavesW > 0 ? coreLeavesW + o.TopGap : 0) +
                         coreW +
                         (peersW > 0 ? o.TopGap + peersW : 0);

        double routersW = routers.Count > 0
            ? routers.Sum(r => r.W) + (routers.Count - 1) * o.TopGap
            : 0;

        double totalCanvasW = Math.Max(accessMaxRowW, Math.Max(midRowW, routersW));

        // Placement Y coordinates:
        double curY = 0.0;

        // Place Routers at Tier 0
        if (routers.Count > 0)
        {
            double rx = (totalCanvasW - routersW) / 2;
            foreach (var r in routers)
            {
                r.X = rx;
                r.Y = curY;
                rx += r.W + o.TopGap;
            }
            curY += routers.Max(r => r.H) + o.TierGap;
        }

        // Place Core Row (Core Leaves on left, Core in center, Peers on right)
        double coreRowY = curY;
        double midStartX = (totalCanvasW - midRowW) / 2;
        double xCursor = midStartX;

        // Core Leaves
        if (coreLeafRows.Count > 0)
        {
            double leafRowH = coreLeafRows[0].Max(l => l.H);
            for (int rIdx = 0; rIdx < coreLeafRows.Count; rIdx++)
            {
                var r = coreLeafRows[rIdx];
                double rw = r.Sum(l => l.W) + (r.Count - 1) * o.HGap;
                double lx = xCursor + (coreLeavesW - rw) / 2;
                double ly = coreRowY + rIdx * (leafRowH + 0.5);
                foreach (var l in r)
                {
                    l.X = lx;
                    l.Y = ly;
                    lx += l.W + o.HGap;
                }
            }
            xCursor += coreLeavesW + o.TopGap;
        }

        // Core Switch
        if (coreSwitch != null)
        {
            coreSwitch.X = xCursor;
            coreSwitch.Y = coreRowY;
            xCursor += coreSwitch.W;
        }

        // Peer Switches
        if (peerSwitches.Count > 0)
        {
            xCursor += o.TopGap;
            foreach (var p in peerSwitches)
            {
                p.X = xCursor;
                p.Y = coreRowY;
                xCursor += p.W + o.TopGap;
            }
        }

        double maxMidH = Math.Max(
            coreSwitch?.H ?? 0,
            Math.Max(
                peerSwitches.Count > 0 ? peerSwitches.Max(p => p.H) : 0,
                coreLeafRows.Count > 0 ? coreLeafRows.Count * (o.LeafH + 0.5) : 0));

        curY = coreRowY + maxMidH + o.TierGap;

        // Place Access Switches and Leaf Rows in balanced multi-tier rows
        foreach (var rowClusters in clusterRows)
        {
            double thisRowW = RowClusterW(rowClusters);
            double axCursor = (totalCanvasW - thisRowW) / 2.0;
            double maxRowClusterH = 0;

            foreach (var (sw, leafRows, clusterW, clusterH) in rowClusters)
            {
                maxRowClusterH = Math.Max(maxRowClusterH, clusterH);
                sw.X = axCursor + (clusterW - sw.W) / 2.0;
                sw.Y = curY;

                double leafY = curY + sw.H + o.TierGap;
                foreach (var row in leafRows)
                {
                    double rowW = row.Sum(l => l.W) + (row.Count - 1) * o.HGap;
                    double lx = axCursor + (clusterW - rowW) / 2.0;
                    foreach (var leaf in row)
                    {
                        leaf.X = lx;
                        leaf.Y = leafY;
                        lx += leaf.W + o.HGap;
                    }
                    leafY += o.LeafH + 0.8;
                }

                axCursor += clusterW + (o.HGap * 2.0);
            }

            curY += maxRowClusterH + o.TierGap * 1.5;
        }

        // Orphan leaves
        var placedIds = new HashSet<string>(nodes.Values.Where(n => n.X > 0 || n.Y > 0).Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
        var orphans = leaves.Where(l => !placedIds.Contains(l.Id)).ToList();
        if (orphans.Count > 0)
        {
            double maxY = nodes.Values.Any() ? nodes.Values.Max(n => n.Bottom) : 0;
            double ox = 0;
            foreach (var oLeaf in orphans)
            {
                oLeaf.X = ox;
                oLeaf.Y = maxY + o.TierGap;
                ox += oLeaf.W + o.HGap;
            }
        }

        return new List<DiagramContainer>();
    }

    private static (string sa, string sb) DetermineSides(DiagramNode A, DiagramNode B, Dictionary<string, DiagramNode> allNodes)
    {
        // 1. If A is strictly above B
        if (B.Y >= A.Bottom - 0.05)
        {
            return ("bottom", "top");
        }

        // 2. If B is strictly above A
        if (A.Y >= B.Bottom - 0.05)
        {
            return ("top", "bottom");
        }

        // 3. Vertically overlapping (side-by-side)
        var lo = A.CenterX <= B.CenterX ? A : B;
        var hi = ReferenceEquals(lo, A) ? B : A;
        bool blocked = allNodes.Values.Any(n =>
            n.Id != A.Id && n.Id != B.Id &&
            Math.Abs(n.CenterY - A.CenterY) < Math.Max(A.H, n.H) / 2 &&
            lo.Right <= n.CenterX && n.CenterX <= hi.X);

        if (!blocked)
        {
            string sa = A.CenterX <= B.CenterX ? "right" : "left";
            string sb = sa == "right" ? "left" : "right";
            return (sa, sb);
        }

        // Blocked by an intermediate node on the same level: loop over the top
        return ("top", "top");
    }

    // =====================================================================================
    // Routing
    // =====================================================================================

    private static List<DiagramHighlight> RouteLinks(Dictionary<string, DiagramNode> nodes, List<DiagramLink> links, DiagramOptions o)
    {
        // ---- 1. which edge does each end of each link use
        foreach (var l in links)
        {
            var A = nodes[l.A];
            var B = nodes[l.B];
            var (sa, sb) = DetermineSides(A, B, nodes);
            l.ASide = sa;
            l.BSide = sb;
        }

        // ---- 2. one slot per link end along its edge, ordered by where the other end is (fewer crossings)
        var groups = new Dictionary<(string Node, string Side), List<(DiagramLink Link, char End)>>();
        foreach (var l in links)
        {
            AddTo(groups, (l.A, l.ASide), (l, 'a'));
            AddTo(groups, (l.B, l.BSide), (l, 'b'));
        }

        var slot = new Dictionary<(string Link, char End), (double X, double Y, int Index)>();
        foreach (var (key, items) in groups)
        {
            var n = nodes[key.Node];
            bool horizontalEdge = key.Side is "top" or "bottom";
            var ordered = items
                .OrderBy(it =>
                {
                    var other = nodes[it.End == 'a' ? it.Link.B : it.Link.A];
                    return horizontalEdge ? other.CenterX : other.CenterY;
                })
                .ThenBy(it => it.Link.Id, StringComparer.Ordinal)
                .ToList();
            int cnt = ordered.Count;
            for (int i = 0; i < cnt; i++)
            {
                double f = (i + 1) / (double)(cnt + 1);
                (double px, double py) = key.Side switch
                {
                    "top" => (n.X + n.W * f, n.Y),
                    "bottom" => (n.X + n.W * f, n.Bottom),
                    "left" => (n.X, n.Y + n.H * f),
                    _ => (n.Right, n.Y + n.H * f)
                };
                slot[(ordered[i].Link.Id, ordered[i].End)] = (px, py, i);
            }
        }

        double zone = o.LabelW + 0.10;       // room for vertical port label pill along connection line

        // ---- 3. lanes: links that leave the same row share one lane pool, so lanes never overlap
        var pools = new Dictionary<(string, string, double), List<(DiagramLink Link, double X1, double X2)>>();
        foreach (var l in links)
        {
            if (l.ASide is "top" or "bottom" && l.BSide is "top" or "bottom")
            {
                var s1 = slot[(l.Id, 'a')];
                var s2 = slot[(l.Id, 'b')];
                var key = (l.ASide, l.BSide, Math.Round(Math.Min(s1.Y, s2.Y), 1));
                if (!pools.TryGetValue(key, out var list))
                    pools[key] = list = new();
                list.Add((l, s1.X, s2.X));
            }
        }
        var lane = new Dictionary<string, (int Index, int Count)>(StringComparer.Ordinal);
        foreach (var (_, items) in pools)
        {
            var ordered = items.OrderBy(t => Math.Min(t.X1, t.X2)).ThenBy(t => t.Link.Id, StringComparer.Ordinal).ToList();
            var lanes = new List<double>();
            var assign = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (link, x1, x2) in ordered)
            {
                double lo = Math.Min(x1, x2), hi = Math.Max(x1, x2);
                int c = lanes.FindIndex(end => end + 0.05 < lo);
                if (c < 0)
                {
                    lanes.Add(hi);
                    c = lanes.Count - 1;
                }
                else
                    lanes[c] = hi;
                assign[link.Id] = c;
            }
            foreach (var (link, _, _) in ordered)
                lane[link.Id] = (assign[link.Id], lanes.Count);
        }

        // ---- 4. geometry
        foreach (var l in links)
        {
            var sa = l.ASide;
            var sb = l.BSide;
            var a = slot[(l.Id, 'a')];
            var b = slot[(l.Id, 'b')];
            var pts = new List<RoutePoint>();

            if (sa is "top" or "bottom" && sb is "top" or "bottom")
            {
                double yc = (a.Y + b.Y) / 2;
                if (lane.TryGetValue(l.Id, out var ln))
                {
                    var (c, count) = ln;
                    if (sa == sb)
                    {
                        double step = Math.Max(o.ChannelStepMin, 0.1);
                        yc = sa == "top"
                            ? Math.Min(a.Y, b.Y) - zone - (c + 1) * step
                            : Math.Max(a.Y, b.Y) + zone + (c + 1) * step;
                    }
                    else
                    {
                        bool down = sa == "bottom";
                        double yFrom = down ? a.Y : b.Y;
                        double yTo = down ? b.Y : a.Y;
                        double lo = yFrom + zone;
                        double hi = Math.Min(yFrom + o.TierGap - zone, yTo - zone);
                        if (hi < lo)
                            lo = hi = (yFrom + yTo) / 2;
                        yc = lo + (c + 1) * (hi - lo) / (count + 1);
                    }
                }
                else if (sa == sb)
                {
                    double step = Math.Max(o.ChannelStepMin, 0.1);
                    yc = sa == "top"
                        ? Math.Min(a.Y, b.Y) - zone - step
                        : Math.Max(a.Y, b.Y) + zone + step;
                }

                const double eps = 0.05;
                double minY = Math.Min(yc, b.Y), maxY = Math.Max(yc, b.Y);
                var blockingNode = nodes.Values.FirstOrDefault(n =>
                    n.Id != l.A && n.Id != l.B &&
                    b.X > n.X + eps && b.X < n.Right - eps &&
                    maxY > n.Y + eps && minY < n.Bottom - eps);

                if (blockingNode != null)
                {
                    double corridorX = b.X < blockingNode.CenterX ? blockingNode.X - o.HGap : blockingNode.Right + o.HGap;
                    int laneIdx = lane.TryGetValue(l.Id, out var lnVal) ? lnVal.Index : 0;
                    double step = Math.Max(o.ChannelStepMin, 0.1);
                    double ycMid = b.Y - zone - (laneIdx + 1) * step;

                    pts.Add(new(a.X, a.Y));
                    pts.Add(new(a.X, yc));
                    pts.Add(new(corridorX, yc));
                    pts.Add(new(corridorX, ycMid));
                    pts.Add(new(b.X, ycMid));
                    pts.Add(new(b.X, b.Y));
                }
                else
                {
                    if (Math.Abs(a.X - b.X) < 0.02)
                    {
                        pts.Add(new(a.X, a.Y));
                        pts.Add(new(b.X, b.Y));
                    }
                    else
                    {
                        pts.Add(new(a.X, a.Y));
                        pts.Add(new(a.X, yc));
                        pts.Add(new(b.X, yc));
                        pts.Add(new(b.X, b.Y));
                    }
                }
            }
            else if (sa is "left" or "right" && sb is "left" or "right")
            {
                if (Math.Abs(a.Y - b.Y) < 0.02)
                {
                    pts.Add(new(a.X, a.Y));
                    pts.Add(new(b.X, b.Y));
                }
                else if (sa == sb)
                {
                    double step = Math.Max(o.ChannelStepMin, 0.1);
                    double xc = sa == "left"
                        ? Math.Min(a.X, b.X) - zone - step
                        : Math.Max(a.X, b.X) + zone + step;
                    pts.Add(new(a.X, a.Y));
                    pts.Add(new(xc, a.Y));
                    pts.Add(new(xc, b.Y));
                    pts.Add(new(b.X, b.Y));
                }
                else
                {
                    double xc = (a.X + b.X) / 2;
                    pts.Add(new(a.X, a.Y));
                    pts.Add(new(xc, a.Y));
                    pts.Add(new(xc, b.Y));
                    pts.Add(new(b.X, b.Y));
                }
            }
            else if (sa is "top" or "bottom")
            {
                pts.Add(new(a.X, a.Y));
                pts.Add(new(a.X, b.Y));
                pts.Add(new(b.X, b.Y));
            }
            else
            {
                pts.Add(new(a.X, a.Y));
                pts.Add(new(b.X, a.Y));
                pts.Add(new(b.X, b.Y));
            }

            l.Points = pts;
            l.ALabel = LabelPos(sa, a.X, a.Y, a.Index, true, o);
            l.BLabel = LabelPos(sb, b.X, b.Y, b.Index, false, o);
        }

        // ---- 5. port-channel highlights
        var hl = new List<DiagramHighlight>();
        var pcGroups = new Dictionary<(string Node, string Side, string Pc), List<(double X, double Y, int Index)>>();
        foreach (var l in links.Where(x => !string.IsNullOrEmpty(x.PortChannelId)))
        {
            AddTo(pcGroups, (l.A, l.ASide, l.PortChannelId!), slot[(l.Id, 'a')]);
            AddTo(pcGroups, (l.B, l.BSide, l.PortChannelId!), slot[(l.Id, 'b')]);
        }
        foreach (var (key, ss) in pcGroups)
        {
            if (ss.Count < 2 || key.Side is not ("top" or "bottom"))
                continue;
            double minX = ss.Min(s => s.X), maxX = ss.Max(s => s.X), y = ss[0].Y;
            const double pad = 0.18;
            hl.Add(new DiagramHighlight { X = minX - pad, Y = y - 0.16, W = maxX - minX + 2 * pad, H = 0.32, Text = "Po" + key.Pc });
        }
        return hl;
    }

    private static void AddTo<TKey, TVal>(Dictionary<TKey, List<TVal>> d, TKey key, TVal value) where TKey : notnull
    {
        if (!d.TryGetValue(key, out var list))
            d[key] = list = new List<TVal>();
        list.Add(value);
    }

    private static RoutePoint LabelPos(string side, double x, double y, int idx, bool isA, DiagramOptions o)
    {
        switch (side)
        {
            case "bottom":
                return new(x + 0.03, y + 0.04);
            case "top":
                return new(x + 0.03, y - o.LabelW - 0.04);
            case "right":
                double dyR = isA ? -o.LabelH - 0.02 : 0.02;
                return new(x + 0.05, y + dyR);
            default: // "left"
                double dyL = isA ? -o.LabelH - 0.02 : 0.02;
                return new(x - o.LabelW - 0.05, y + dyL);
        }
    }

    // =====================================================================================
    // Normalise
    // =====================================================================================

    private static void Normalize(DiagramLayout layout, double margin, DiagramOptions? options = null)
    {
        var o = options ?? new DiagramOptions();
        double minX = double.MaxValue, minY = double.MaxValue;
        void See(double x, double y)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
        }

        foreach (var n in layout.Nodes.Values)
            See(n.X, n.Y);
        foreach (var c in layout.Containers)
            See(c.X, c.Y);
        foreach (var l in layout.Links)
        {
            foreach (var p in l.Points)
                See(p.X, p.Y);
            See(l.ALabel.X, l.ALabel.Y);
            See(l.BLabel.X, l.BLabel.Y);
        }
        if (minX == double.MaxValue)
            return;

        double dx = margin - minX, dy = margin - minY;
        foreach (var n in layout.Nodes.Values)
        {
            n.X += dx;
            n.Y += dy;
        }
        foreach (var c in layout.Containers)
        {
            c.X += dx;
            c.Y += dy;
        }
        foreach (var h in layout.Highlights)
        {
            h.X += dx;
            h.Y += dy;
        }
        foreach (var l in layout.Links)
        {
            l.Points = l.Points.Select(p => new RoutePoint(p.X + dx, p.Y + dy)).ToList();
            l.ALabel = new(l.ALabel.X + dx, l.ALabel.Y + dy);
            l.BLabel = new(l.BLabel.X + dx, l.BLabel.Y + dy);
        }

        double w = 0, h2 = 0;
        foreach (var n in layout.Nodes.Values)
        {
            w = Math.Max(w, n.Right);
            h2 = Math.Max(h2, n.Bottom);
        }
        foreach (var c in layout.Containers)
        {
            w = Math.Max(w, c.X + c.W);
            h2 = Math.Max(h2, c.Y + c.H);
        }
        foreach (var l in layout.Links)
        {
            foreach (var p in l.Points)
            {
                w = Math.Max(w, p.X);
                h2 = Math.Max(h2, p.Y);
            }
            double aW = l.AIsVertical ? o.LabelH : o.LabelW;
            double aH = l.AIsVertical ? o.LabelW : o.LabelH;
            w = Math.Max(w, l.ALabel.X + aW);
            h2 = Math.Max(h2, l.ALabel.Y + aH);

            double bW = l.BIsVertical ? o.LabelH : o.LabelW;
            double bH = l.BIsVertical ? o.LabelW : o.LabelH;
            w = Math.Max(w, l.BLabel.X + bW);
            h2 = Math.Max(h2, l.BLabel.Y + bH);
        }
        layout.Width = w + margin;
        layout.Height = h2 + margin;
    }
}
