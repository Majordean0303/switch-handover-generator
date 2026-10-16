namespace SwitchHandover.Core.Models;

public class SwitchDetails
{
    public string Location { get; set; } = "Unknown";
    public string LocationType { get; set; } = "Unknown";
    public string Hostname { get; set; } = "N/A";
    public string StackMember { get; set; } = "Switch 1";
    public string IpAddress { get; set; } = "N/A";
    public string DeviceType { get; set; } = "L2 / Access Switch";
    public string Make { get; set; } = "Cisco";
    public string Model { get; set; } = "N/A";
    public string SrNo { get; set; } = "N/A";
    public string FirmwareVersion { get; set; } = "N/A";
    public string Uptime { get; set; } = "N/A";
    public string TotalPorts { get; set; } = "0";
    public string MacAddress { get; set; } = "N/A";
    public string DefaultGateway { get; set; } = "N/A";
    public string NtpServer { get; set; } = "";
    public string ActivePowerSupplies { get; set; } = "1";
}
