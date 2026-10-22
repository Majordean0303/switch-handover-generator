using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VisioCreator.Core.Layout;
namespace SwitchHandover.Desktop;
using VisioCreator;
public sealed class TopologyCanvas:Control{
 public static readonly StyledProperty<DiagramLayout?> DiagramProperty=AvaloniaProperty.Register<TopologyCanvas,DiagramLayout?>(nameof(Diagram));
 public DiagramLayout? Diagram{get=>GetValue(DiagramProperty);set=>SetValue(DiagramProperty,value);}
 public static readonly StyledProperty<double> ScaleProperty=AvaloniaProperty.Register<TopologyCanvas,double>(nameof(Scale),70);
 public double Scale{get=>GetValue(ScaleProperty);set=>SetValue(ScaleProperty,value);}
 public static readonly StyledProperty<VisioCreator.Core.Graph.TopologyGraph?> GraphProperty=AvaloniaProperty.Register<TopologyCanvas,VisioCreator.Core.Graph.TopologyGraph?>(nameof(Graph));
 public VisioCreator.Core.Graph.TopologyGraph? Graph{get=>GetValue(GraphProperty);set=>SetValue(GraphProperty,value);}
 public event Action<string>? NodePicked;
 DiagramNode? _drag;Point _offset;
 static TopologyCanvas(){AffectsRender<TopologyCanvas>(DiagramProperty,ScaleProperty,GraphProperty);AffectsMeasure<TopologyCanvas>(DiagramProperty,ScaleProperty);}
 protected override Size MeasureOverride(Size available)=>Diagram is {} d?new(Math.Max(400,d.Width*Scale),Math.Max(300,d.Height*Scale)):new(600,400);
 IBrush ThemeBrush(string key,IBrush fallback)=>this.TryFindResource(key,out var value)&&value is IBrush b?b:fallback;
 public override void Render(DrawingContext context){base.Render(context);var foreground=ThemeBrush("TextFillColorPrimaryBrush",Brushes.Black);context.FillRectangle(ThemeBrush("SolidBackgroundFillColorBaseBrush",Brushes.White),new Rect(Bounds.Size));if(Diagram is not {} d){Text(context,"Import a workbook or command capture to build a topology",new Point(24,24),foreground,15);return;}
  foreach(var c in d.Containers){context.DrawRectangle(new SolidColorBrush(Color.FromArgb(22,38,140,90)),new Pen(new SolidColorBrush(Color.Parse("#66A98D")),1),new Rect(c.X*Scale,c.Y*Scale,c.W*Scale,c.H*Scale),8,8);Text(context,c.Name,new(c.X*Scale+8,c.Y*Scale+5),foreground,12);}
  foreach(var link in d.Links){var pen=new Pen(link.IsFiber?Brushes.OrangeRed:ThemeBrush("TextFillColorSecondaryBrush",Brushes.DimGray),1.5);for(int i=0;i+1<link.Points.Count;i++)context.DrawLine(pen,new(link.Points[i].X*Scale,link.Points[i].Y*Scale),new(link.Points[i+1].X*Scale,link.Points[i+1].Y*Scale));Text(context,link.APort,new(link.ALabel.X*Scale,link.ALabel.Y*Scale),foreground,10);Text(context,link.BPort,new(link.BLabel.X*Scale,link.BLabel.Y*Scale),foreground,10);}
  foreach(var node in d.Nodes.Values){var color=node.Role switch{"Core"=>"#234E84","Distribution"=>"#375D89","AP" or "Campus AP"=>"#237C78","Phone" or "IP Phones"=>"#69528A",_=>"#3C6485"};var rect=new Rect(node.X*Scale,node.Y*Scale,node.W*Scale,Math.Min(node.H*.65,.85)*Scale);var device=Graph?.Nodes.Values.FirstOrDefault(d=>VisioCreator.Core.Models.HostKey.Of(d.Hostname)==VisioCreator.Core.Models.HostKey.Of(node.Id));context.DrawRectangle(new SolidColorBrush(Color.Parse(color)),new Pen(Brushes.LightSteelBlue,1),rect,6,6);Text(context,device?.Model??node.Role,new(rect.X+8,rect.Y+12),Brushes.White,12);Text(context,device?.Hostname??node.Id,new(rect.X+5,rect.Bottom+4),foreground,11);Text(context,device?.IpAddress??"",new(rect.X+5,rect.Bottom+19),foreground,10);}
 }
 static void Text(DrawingContext c,string value,Point p,IBrush brush,double size){var text=new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Inter"),size,brush);c.DrawText(text,p);}
 protected override void OnPointerPressed(PointerPressedEventArgs e){base.OnPointerPressed(e);if(Diagram is null||!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;var p=e.GetPosition(this);_drag=Diagram.Nodes.Values.LastOrDefault(n=>new Rect(n.X*Scale,n.Y*Scale,n.W*Scale,n.H*Scale).Contains(p));if(_drag is null)return;NodePicked?.Invoke(_drag.Id);_offset=new(p.X/Scale-_drag.X,p.Y/Scale-_drag.Y);e.Pointer.Capture(this);e.Handled=true;}
 protected override void OnPointerMoved(PointerEventArgs e){base.OnPointerMoved(e);if(_drag is null||Diagram is null)return;var p=e.GetPosition(this);_drag.X=Math.Clamp(p.X/Scale-_offset.X,.6,500);_drag.Y=Math.Clamp(p.Y/Scale-_offset.Y,.6,500);DiagramLayoutEngine.Reroute(Diagram);InvalidateMeasure();InvalidateVisual();}
 protected override void OnPointerReleased(PointerReleasedEventArgs e){_drag=null;e.Pointer.Capture(null);base.OnPointerReleased(e);}
 protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){_drag=null;base.OnPointerCaptureLost(e);}
}
