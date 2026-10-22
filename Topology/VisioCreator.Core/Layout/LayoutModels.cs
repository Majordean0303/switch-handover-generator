using System.Collections.Generic;
using VisioCreator.Core.Models;

namespace VisioCreator.Core.Layout;

public class LayoutProfile
{
    public double NodeWidth { get; set; } = 1.8;
    public double NodeHeight { get; set; } = 0.9;
    public double XSpacing { get; set; } = 1.2;
    public double YSpacing { get; set; } = 2.2;
    public double ClusterMargin { get; set; } = 1.2;
    public int MaxDevicesPerRow { get; set; } = 8;
    
    public static LayoutProfile GetDefault() => new LayoutProfile();
}

public class NodeCoordinate
{
    public Device Device { get; set; } = null!;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class ClusterBox
{
    public string Location { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class Point2D
{
    public double X { get; set; }
    public double Y { get; set; }
    public Point2D(double x, double y) { X = x; Y = y; }
}

public class RoutedLink
{
    public LogicalLink Link { get; set; } = null!;
    public List<Point2D> Points { get; set; } = new();
    public string SvgPathData { get; set; } = string.Empty;
    
    // Port Details
    public string SourcePort { get; set; } = string.Empty;
    public string DestinationPort { get; set; } = string.Empty;
    public string MiddleLabel { get; set; } = string.Empty;
    
    public Point2D SourcePortPoint { get; set; } = new(0, 0);
    public Point2D DestinationPortPoint { get; set; } = new(0, 0);
    public Point2D MiddlePoint { get; set; } = new(0, 0);
}

public class LayoutResult
{
    public Dictionary<string, NodeCoordinate> Nodes { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
    public List<ClusterBox> Clusters { get; set; } = new();
    public List<RoutedLink> RoutedLinks { get; set; } = new();
}
