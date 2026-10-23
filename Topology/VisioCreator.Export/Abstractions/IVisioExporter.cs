using VisioCreator.Core.Graph;
using VisioCreator.Core.Layout;
using VisioCreator.Export.Configuration;

namespace VisioCreator.Export.Abstractions;

public interface IVisioExporter
{
    void Export(TopologyGraph graph, LayoutResult layout, string outputPath, VisioExportOptions? options = null);
    void Export(TopologyGraph graph, DiagramLayout layout, string outputPath, VisioExportOptions? options = null);
}
