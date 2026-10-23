using Microsoft.Extensions.Logging;
using VisioCreator.Core.Graph;
using VisioCreator.Core.Layout;
using VisioCreator.Export.Abstractions;
using VisioCreator.Export.Com;
using VisioCreator.Export.Configuration;
using VisioCreator.Export.OpenXml;
using VisioCreator.Export.Stencils;

namespace VisioCreator.Export;

public class VisioExporter : IVisioExporter
{
    private readonly OpenXmlVisioExporter _openXmlExporter;
    private readonly ComVisioExporter _comExporter;

    public VisioExporter(ILoggerFactory? loggerFactory = null, StencilManager? stencilManager = null)
    {
        _openXmlExporter = new OpenXmlVisioExporter(loggerFactory?.CreateLogger<OpenXmlVisioExporter>(), stencilManager);
        _comExporter = new ComVisioExporter(loggerFactory?.CreateLogger<ComVisioExporter>(), _openXmlExporter);
    }

    public void Export(TopologyGraph graph, LayoutResult layout, string outputPath, VisioExportOptions? options = null)
    {
        options ??= VisioExportOptions.Default;
        if (options.Backend == VisioBackendType.Com)
        {
            _comExporter.Export(graph, layout, outputPath, options);
        }
        else
        {
            _openXmlExporter.Export(graph, layout, outputPath, options);
        }
    }

    public void Export(TopologyGraph graph, DiagramLayout layout, string outputPath, VisioExportOptions? options = null)
    {
        options ??= VisioExportOptions.Default;
        if (options.Backend == VisioBackendType.Com)
        {
            _comExporter.Export(graph, layout, outputPath, options);
        }
        else
        {
            _openXmlExporter.Export(graph, layout, outputPath, options);
        }
    }
}
