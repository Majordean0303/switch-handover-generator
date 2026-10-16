namespace SwitchHandover.Core.Models;

public class SfpInventory
{
    public string Location { get; set; } = "Unknown";
    public string LocationType { get; set; } = "Unknown";
    public string Hostname { get; set; } = "N/A";
    public string DeviceIp { get; set; } = "N/A";
    public string Interface { get; set; } = "";
    public string Description { get; set; } = "";
    public string Pid { get; set; } = "";
    public string SerialNumber { get; set; } = "N/A";
    public string InUse { get; set; } = "Unknown";
}
