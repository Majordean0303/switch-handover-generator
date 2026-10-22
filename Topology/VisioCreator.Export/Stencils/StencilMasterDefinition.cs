using System.Collections.Generic;

namespace VisioCreator.Export.Stencils;

public class ConnectionPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public string FormulaX { get; set; } = string.Empty;
    public string FormulaY { get; set; } = string.Empty;
    public int DirX { get; set; }
    public int DirY { get; set; }
}

public class StencilMasterDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameU { get; set; } = string.Empty;
    public string StencilKey { get; set; } = string.Empty;
    public string FillColor { get; set; } = "#FFFFFF";
    public string LineColor { get; set; } = "#37474F";
    public double DefaultWidth { get; set; } = 2.4;
    public double DefaultHeight { get; set; } = 1.0;
    public List<ConnectionPoint> ConnectionPoints { get; set; } = new();

    public static List<StencilMasterDefinition> GetStarterMasters()
    {
        var masters = new List<StencilMasterDefinition>
        {
            new StencilMasterDefinition
            {
                Id = 1,
                Name = "Core Switch",
                NameU = "Core Switch",
                StencilKey = "core_switch",
                FillColor = "#C62828", // Core Crimson
                LineColor = "#B71C1C",
                DefaultWidth = 2.4,
                DefaultHeight = 1.0
            },
            new StencilMasterDefinition
            {
                Id = 2,
                Name = "Distribution Switch",
                NameU = "Distribution Switch",
                StencilKey = "dist_switch",
                FillColor = "#E65100", // Dist Orange
                LineColor = "#BF360C",
                DefaultWidth = 2.4,
                DefaultHeight = 1.0
            },
            new StencilMasterDefinition
            {
                Id = 3,
                Name = "Access Switch",
                NameU = "Access Switch",
                StencilKey = "access_switch",
                FillColor = "#2E7D32", // Access Forest Green
                LineColor = "#1B5E20",
                DefaultWidth = 2.4,
                DefaultHeight = 1.0
            },
            new StencilMasterDefinition
            {
                Id = 4,
                Name = "Access Point",
                NameU = "Access Point",
                StencilKey = "ap",
                FillColor = "#0288D1", // AP Sky Blue
                LineColor = "#01579B",
                DefaultWidth = 2.0,
                DefaultHeight = 0.8
            },
            new StencilMasterDefinition
            {
                Id = 5,
                Name = "Router",
                NameU = "Router",
                StencilKey = "router",
                FillColor = "#1565C0", // Router Blue
                LineColor = "#0D47A1",
                DefaultWidth = 2.4,
                DefaultHeight = 1.0
            },
            new StencilMasterDefinition
            {
                Id = 6,
                Name = "Firewall",
                NameU = "Firewall",
                StencilKey = "firewall",
                FillColor = "#D32F2F", // Firewall Brick Red
                LineColor = "#B71C1C",
                DefaultWidth = 2.4,
                DefaultHeight = 1.0
            },
            new StencilMasterDefinition
            {
                Id = 7,
                Name = "Printer",
                NameU = "Printer",
                StencilKey = "printer",
                FillColor = "#546E7A", // Printer Slate Gray
                LineColor = "#37474F",
                DefaultWidth = 2.0,
                DefaultHeight = 0.8
            },
            new StencilMasterDefinition
            {
                Id = 8,
                Name = "External Device",
                NameU = "External Device",
                StencilKey = "external",
                FillColor = "#455A64", // Endpoint Charcoal
                LineColor = "#263238",
                DefaultWidth = 2.0,
                DefaultHeight = 0.8
            },
            new StencilMasterDefinition
            {
                Id = 9,
                Name = "Generic Switch",
                NameU = "Generic Switch",
                StencilKey = "generic",
                FillColor = "#78909C", // Fallback Gray
                LineColor = "#455A64",
                DefaultWidth = 2.4,
                DefaultHeight = 1.0
            },
            new StencilMasterDefinition
            {
                Id = 10,
                Name = "Dynamic connector",
                NameU = "Dynamic connector",
                StencilKey = "connector",
                FillColor = "#000000",
                LineColor = "#1976D2",
                DefaultWidth = 1.0,
                DefaultHeight = 1.0
            }
        };

        foreach (var m in masters)
        {
            if (m.Id < 10)
            {
                // Standard 4 connection points: bottom, top, left, right
                m.ConnectionPoints = new List<ConnectionPoint>
                {
                    new ConnectionPoint { X = m.DefaultWidth * 0.5, Y = 0.0, FormulaX = "Width*0.5", FormulaY = "Height*0", DirX = 0, DirY = -1 },
                    new ConnectionPoint { X = m.DefaultWidth * 0.5, Y = m.DefaultHeight, FormulaX = "Width*0.5", FormulaY = "Height*1", DirX = 0, DirY = 1 },
                    new ConnectionPoint { X = 0.0, Y = m.DefaultHeight * 0.5, FormulaX = "Width*0", FormulaY = "Height*0.5", DirX = -1, DirY = 0 },
                    new ConnectionPoint { X = m.DefaultWidth, Y = m.DefaultHeight * 0.5, FormulaX = "Width*1", FormulaY = "Height*0.5", DirX = 1, DirY = 0 }
                };
            }
        }

        return masters;
    }
}
