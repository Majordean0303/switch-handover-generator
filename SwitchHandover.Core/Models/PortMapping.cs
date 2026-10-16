namespace SwitchHandover.Core.Models;

public class PortMapping
{
    public string Location { get; set; } = "Unknown";
    public string LocationType { get; set; } = "Unknown";
    public string DeviceType { get; set; } = "Access";
    public string Hostname { get; set; } = "N/A";
    public string DeviceIp { get; set; } = "N/A";
    public string PortNo { get; set; } = "";
    public string Description { get; set; } = "";
    public string State { get; set; } = "-";
    public string Mode { get; set; } = "Access";
    public string VlanId { get; set; } = "1";
    public string NeighbourHostname { get; set; } = "-";
    public string NeighbourDeviceIp { get; set; } = "-";
    public string NeighbourPortNo { get; set; } = "-";
}
