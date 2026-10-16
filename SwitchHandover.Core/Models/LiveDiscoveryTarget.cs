namespace SwitchHandover.Core.Models;

public class LiveDiscoveryTarget
{
    public string IpAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public bool IsAp { get; set; }
    public string Status { get; set; } = "Queued";
    public int Progress { get; set; }
}
