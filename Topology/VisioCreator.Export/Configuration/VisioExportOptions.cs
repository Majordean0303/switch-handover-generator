using System;
using System.Collections.Generic;

namespace VisioCreator.Export.Configuration;

public enum VisioBackendType
{
    OpenXml,
    Com
}

public class VisioExportOptions
{
    public VisioBackendType Backend { get; set; } = VisioBackendType.OpenXml;
    public string LabelTemplate { get; set; } = "{hostname}\n{mgmt_ip}\n{model}";
    public bool PagePerSite { get; set; } = true;
    public string CustomStencilPath { get; set; } = string.Empty;

    public Dictionary<string, string> StencilKeyToMasterMap { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["core_switch"] = "Core Switch",
        ["dist_switch"] = "Distribution Switch",
        ["access_switch"] = "Access Switch",
        ["ap"] = "Access Point",
        ["router"] = "Router",
        ["firewall"] = "Firewall",
        ["printer"] = "Printer",
        ["external"] = "External Device",
        ["ip_phone"] = "External Device",
        ["rf_bridge"] = "External Device",
        ["generic"] = "Generic Switch"
    };

    public static VisioExportOptions Default => new();
}
