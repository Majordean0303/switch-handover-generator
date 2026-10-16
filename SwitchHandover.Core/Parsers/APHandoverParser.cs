using System.Text.RegularExpressions;
using SwitchHandover.Core.Models;

namespace SwitchHandover.Core.Parsers;

public class APHandoverParser
{
    private readonly string _logText;
    private readonly string _location;
    private readonly string _locationType;

    public APHandoverParser(string logText, string location = "Unknown", string locationType = "Unknown")
    {
        string cleanedText = Regex.Replace(logText, @"\x1b\[[0-9;]*m", "");
        _logText = cleanedText.Replace("\r", "").Replace("\\", "");
        _location = location;
        _locationType = locationType;
    }

    public List<SwitchDetails> ParseDeviceDetails()
    {
        var apNameMatch = Regex.Match(_logText, @"AP Name\s*:\s*(\S+)", RegexOptions.IgnoreCase);
        string hostname = apNameMatch.Success ? apNameMatch.Groups[1].Value : "N/A";

        var ipMatch = Regex.Match(_logText, @"Device IP:\s*(\S+)", RegexOptions.IgnoreCase);
        if (!ipMatch.Success)
        {
            ipMatch = Regex.Match(_logText, @"inet addr:\s*(\S+)", RegexOptions.IgnoreCase);
        }
        string mgmtIp = ipMatch.Success ? ipMatch.Groups[1].Value : "N/A";

        var macMatch = Regex.Match(_logText, @"HWaddr\s+([\w:]+)", RegexOptions.IgnoreCase);
        if (!macMatch.Success)
        {
            macMatch = Regex.Match(_logText, @"Base Ethernet MAC Address\s*:\s*([\w:]+)", RegexOptions.IgnoreCase);
        }
        string macAddr = macMatch.Success ? macMatch.Groups[1].Value : "N/A";

        var modelMatch = Regex.Match(_logText, @"PID:\s*([A-Za-z0-9\-]+)");
        if (!modelMatch.Success)
        {
            modelMatch = Regex.Match(_logText, @"NAME:\s*([^,]+)");
        }
        string model = modelMatch.Success ? modelMatch.Groups[1].Value.Trim() : "N/A";

        var snMatch = Regex.Match(_logText, @"SN:\s*(\S+)");
        string sn = snMatch.Success ? snMatch.Groups[1].Value : "N/A";

        return new List<SwitchDetails>
        {
            new SwitchDetails
            {
                Location = _location,
                LocationType = _locationType,
                Hostname = hostname,
                StackMember = "N/A",
                IpAddress = mgmtIp,
                DeviceType = "AP",
                Make = "Cisco",
                Model = model,
                SrNo = sn,
                FirmwareVersion = "N/A",
                Uptime = "N/A",
                TotalPorts = "N/A",
                MacAddress = macAddr,
                DefaultGateway = "N/A",
                NtpServer = "N/A",
                ActivePowerSupplies = "N/A"
            }
        };
    }

    public List<PortMapping> ParsePortMapping(bool returnAll = false) => new();

    public List<SfpInventory> ParseSfpInventory() => new();
}
