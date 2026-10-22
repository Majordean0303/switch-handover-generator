using System.Text.RegularExpressions;
using SwitchHandover.Core.Models;

namespace SwitchHandover.Core.Parsers;

public class SwitchHandoverParser
{
    private readonly string _logText;
    private readonly string _location;
    private readonly string _locationType;
    private readonly string? _mgmtVlan;
    private readonly string _hostname;
    private readonly string _mgmtIp;

    public SwitchHandoverParser(string logText, string location = "Unknown", string locationType = "Unknown", string? mgmtVlan = null)
    {
        // Normalize: Strip ANSI codes, normalize line endings
        string cleanedText = Regex.Replace(logText, @"\x1b\[[0-9;]*m", "");
        _logText = cleanedText.Replace("\r", "").Replace("\\", "");
        _location = location;
        _locationType = locationType;
        _mgmtVlan = mgmtVlan;

        _hostname = ExtractHostname();
        _mgmtIp = ExtractMgmtIp();
    }

    private string ExtractHostname()
    {
        var match = Regex.Match(_logText, @"^hostname\s+(\S+)", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : "N/A";
    }

    private string ExtractMgmtIp()
    {
        string vlan;
        if (!string.IsNullOrWhiteSpace(_mgmtVlan))
        {
            vlan = $"Vlan{_mgmtVlan}";
        }
        else
        {
            var vlanMatch = Regex.Match(_logText, @"(?:snmp-server trap-source|ip radius source-interface)\s+(Vlan\d+)", RegexOptions.IgnoreCase);
            vlan = vlanMatch.Success ? vlanMatch.Groups[1].Value : @"Vlan\d+";
        }

        var ipBlockMatch = Regex.Match(_logText, $@"^interface {vlan}\b(.*?)(?:^!|^interface)", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (ipBlockMatch.Success)
        {
            var ipMatch = Regex.Match(ipBlockMatch.Groups[1].Value, @"ip address\s+(\d+\.\d+\.\d+\.\d+)");
            if (ipMatch.Success) return ipMatch.Groups[1].Value;
        }
        return "N/A";
    }

    public static string NormalizeIntf(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string n = name.ToLowerInvariant();
        n = Regex.Replace(n, @"^switch\s+\d+\s*-\s*", ""); // Strip "Switch 1 - " if present in NAME
        n = Regex.Replace(n, @"^twentyfivegige|^twe", "twe");
        n = Regex.Replace(n, @"^tengigabitethernet|^ten|^te", "te");
        n = Regex.Replace(n, @"^gigabitethernet|^gig|^gi", "gig");
        n = Regex.Replace(n, @"^fastethernet|^fas|^fa", "fa");
        n = Regex.Replace(n, @"^hundredgige|^hun|^hu", "hu");
        return Regex.Replace(n, @"\s+", "");
    }

    private List<SwitchDetails> ParseStackMembers()
    {
        var members = new List<SwitchDetails>();
        var seenSns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var pattern = @"Base Ethernet MAC Address\s*:\s*(?<mac>[\w:]+).*?Model Number\s*:\s*(?<model>\S+).*?System Serial Number\s*:\s*(?<sn>\S+)";
        var matches = Regex.Matches(_logText, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            string sn = match.Groups["sn"].Value.Trim();
            if (seenSns.Add(sn))
            {
                members.Add(new SwitchDetails
                {
                    StackMember = $"Switch {members.Count + 1}",
                    Model = match.Groups["model"].Value.Trim(),
                    SrNo = sn,
                    MacAddress = match.Groups["mac"].Value.Trim()
                });
            }
        }

        if (members.Count == 0)
        {
            var sn = Regex.Match(_logText, @"System Serial Number\s*:\s*(\S+)");
            var mac = Regex.Match(_logText, @"Base Ethernet MAC Address\s*:\s*([\w:]+)");
            var model = Regex.Match(_logText, @"Model Number\s*:\s*(\S+)");

            members.Add(new SwitchDetails
            {
                StackMember = "Switch 1",
                Model = model.Success ? model.Groups[1].Value.Trim() : "N/A",
                SrNo = sn.Success ? sn.Groups[1].Value.Trim() : "N/A",
                MacAddress = mac.Success ? mac.Groups[1].Value.Trim() : "N/A"
            });
        }

        return members;
    }

    private Dictionary<string, int> CountAllPortsBySwitch()
    {
        var portCounts = new Dictionary<string, int>();
        var seenPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var physicalIntfPattern = new Regex(
            @"^(?:GigabitEthernet|TenGigabitEthernet|FastEthernet|TwentyFiveGigE|HundredGigE|Gig|Te|Fa|Twe|Hu)(\d+)/\d+/\d+",
            RegexOptions.IgnoreCase);

        var blocks = Regex.Split(_logText, @"^interface ", RegexOptions.Multiline);
        for (int i = 1; i < blocks.Length; i++)
        {
            string block = blocks[i].Trim();
            var lines = block.Split('\n');
            string name = lines[0].Trim();

            var match = physicalIntfPattern.Match(name);
            if (match.Success)
            {
                if (seenPorts.Add(name))
                {
                    string switchId = match.Groups[1].Value;
                    portCounts[switchId] = portCounts.GetValueOrDefault(switchId, 0) + 1;
                }
            }
        }

        return portCounts;
    }

    private Dictionary<string, int> CountActivePowerSupplies()
    {
        var psCounts = new Dictionary<string, int>();
        bool foundAny = false;

        var matches = Regex.Matches(_logText, @"^(\d+)[A-Z]\s+(?:PWR|C\dK)\S+\s+\S+\s+(?:OK|Good)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            string swId = m.Groups[1].Value;
            psCounts[swId] = psCounts.GetValueOrDefault(swId, 0) + 1;
            foundAny = true;
        }

        if (!foundAny)
        {
            var fallbackMatches = Regex.Matches(_logText, @"^\s*PS\d+\s+Vout\s+(\d+)\s+GOOD", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            foreach (Match m in fallbackMatches)
            {
                string swId = m.Groups[1].Value;
                psCounts[swId] = psCounts.GetValueOrDefault(swId, 0) + 1;
            }
        }

        return psCounts;
    }

    public List<SwitchDetails> ParseSwitchDetails()
    {
        var portCounts = CountAllPortsBySwitch();
        var psCounts = CountActivePowerSupplies();
        bool isL3 = Regex.IsMatch(_logText, @"^ip routing", RegexOptions.Multiline);

        var uptimeAnchor = Regex.Match(_logText, @"uptime is");
        string firmwareVersion = "N/A";

        if (uptimeAnchor.Success)
        {
            int startIdx = Math.Max(0, uptimeAnchor.Index - 2000);
            string shVerChunk = _logText.Substring(startIdx, uptimeAnchor.Index - startIdx);
            var fwMatch = Regex.Match(shVerChunk, @"Cisco IOS.*?Software.*?Version\s+([A-Za-z0-9\.\(\)]+)", RegexOptions.IgnoreCase);
            if (fwMatch.Success)
            {
                firmwareVersion = fwMatch.Groups[1].Value;
            }
        }

        if (firmwareVersion == "N/A")
        {
            var fwMatch = Regex.Match(_logText, @"Cisco IOS.*?Software.*?Version\s+([A-Za-z0-9\.\(\)]+)", RegexOptions.IgnoreCase);
            if (fwMatch.Success) firmwareVersion = fwMatch.Groups[1].Value;
        }

        var uptimeMatch = Regex.Match(_logText, @"uptime is\s+(.*?)(?:\r|\n)");
        var gwMatch = Regex.Match(_logText, @"ip default-gateway (\d+\.\d+\.\d+\.\d+)");

        var ntpServers = new List<string>();
        foreach (Match m in Regex.Matches(_logText, @"^ntp server (\d+\.\d+\.\d+\.\d+)", RegexOptions.Multiline))
        {
            ntpServers.Add(m.Groups[1].Value);
        }

        var stackMembers = ParseStackMembers();
        var results = new List<SwitchDetails>();

        foreach (var member in stackMembers)
        {
            var swNumMatch = Regex.Match(member.StackMember, @"\d+");
            string sId = swNumMatch.Success ? swNumMatch.Value : "1";

            int totalPorts = portCounts.GetValueOrDefault(sId, 0);
            int psCount = psCounts.GetValueOrDefault(sId, 0);

            results.Add(new SwitchDetails
            {
                Location = _location,
                LocationType = _locationType,
                Hostname = _hostname,
                StackMember = member.StackMember,
                IpAddress = _mgmtIp,
                DeviceType = isL3 ? "L3 / Core Switch" : "L2 / Access Switch",
                Make = "Cisco",
                Model = member.Model,
                SrNo = member.SrNo,
                FirmwareVersion = firmwareVersion,
                Uptime = uptimeMatch.Success ? uptimeMatch.Groups[1].Value.Trim() : "N/A",
                TotalPorts = totalPorts.ToString(),
                MacAddress = member.MacAddress,
                DefaultGateway = gwMatch.Success ? gwMatch.Groups[1].Value : "N/A",
                NtpServer = string.Join(", ", ntpServers),
                ActivePowerSupplies = psCount > 0 ? psCount.ToString() : "1"
            });
        }

        return results;
    }

    public static Dictionary<string, string> ExtractCdpNeighborIps(string rawText)
    {
        var neighborMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cdpBlocks = Regex.Split(rawText, @"-------------------------");

        foreach (var block in cdpBlocks)
        {
            var deviceMatch = Regex.Match(block, @"Device ID:\s*(\S+)");
            var ipMatch = Regex.Match(block, @"IP address:\s*([\d\.]+)");

            if (deviceMatch.Success && ipMatch.Success)
            {
                string rawHostname = deviceMatch.Groups[1].Value.Trim();
                string ipAddress = ipMatch.Groups[1].Value.Trim();
                neighborMap[rawHostname] = ipAddress;
            }
        }

        return neighborMap;
    }

    public List<PortMapping> ParsePortMapping(bool returnAll = false)
    {
        var ports = new Dictionary<string, PortMapping>(StringComparer.OrdinalIgnoreCase);
        var blocks = Regex.Split(_logText, @"^interface ", RegexOptions.Multiline);

        for (int i = 1; i < blocks.Length; i++)
        {
            string block = blocks[i].Trim();
            var lines = block.Split('\n');
            string raw = lines[0].Trim();

            if (raw.Contains("Vlan", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("Port-channel", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("Null", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string desc = "";
            var descMatch = Regex.Match(block, @"^\s*description\s+(.*)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (descMatch.Success) desc = descMatch.Groups[1].Value.Trim();

            string mode = "Access";
            string vlanId = "1";

            if (Regex.IsMatch(block, @"^\s*switchport mode trunk", RegexOptions.Multiline | RegexOptions.IgnoreCase) ||
                Regex.IsMatch(block, @"^\s*switchport trunk allowed vlan", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                mode = "Trunk";
                vlanId = "All";
            }
            else
            {
                var vlanMatch = Regex.Match(block, @"^\s*switchport access vlan\s+(\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (vlanMatch.Success)
                {
                    vlanId = vlanMatch.Groups[1].Value;
                }
                else if (Regex.IsMatch(block, @"^\s*no switchport", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                {
                    mode = "Routed / L3";
                    vlanId = "-";
                }
            }

            string clean = raw.Replace("TwentyFiveGigE", "Twe ", StringComparison.OrdinalIgnoreCase)
                              .Replace("TenGigabitEthernet", "Te ", StringComparison.OrdinalIgnoreCase)
                              .Replace("GigabitEthernet", "Gig ", StringComparison.OrdinalIgnoreCase)
                              .Replace("FastEthernet", "Fa ", StringComparison.OrdinalIgnoreCase)
                              .Replace("HundredGigE", "Hu ", StringComparison.OrdinalIgnoreCase);
            clean = Regex.Replace(clean, @"\s+", " ").Trim();

            string key = NormalizeIntf(raw);

            ports[key] = new PortMapping
            {
                Location = _location,
                LocationType = _locationType,
                DeviceType = _logText.Contains("ip routing") ? "Core" : "Access",
                Hostname = _hostname,
                DeviceIp = _mgmtIp,
                PortNo = clean,
                Description = desc,
                Mode = mode,
                VlanId = vlanId,
                State = "-",
                NeighbourHostname = "-",
                NeighbourDeviceIp = "-",
                NeighbourPortNo = "-"
            };
        }

        // Port states
        foreach (Match m in Regex.Matches(_logText, @"^([A-Za-z0-9/]+)\s+(?:\S+)\s+(?:\w+)\s+(?:\w+)\s+(up|down|administratively down)", RegexOptions.Multiline))
        {
            string k = NormalizeIntf(m.Groups[1].Value);
            if (ports.TryGetValue(k, out var p))
            {
                p.State = m.Groups[2].Value;
            }
        }

        foreach(Match m in Regex.Matches(_logText,@"^(\S+)\s+.*?\s+(connected|notconnect|disabled|err-disabled)\s+\S+",RegexOptions.Multiline|RegexOptions.IgnoreCase))
            if(ports.TryGetValue(NormalizeIntf(m.Groups[1].Value),out var currentPort))currentPort.State=m.Groups[2].Value.ToLowerInvariant()=="connected"?"up":"down";

        var cdpIpDict = ExtractCdpNeighborIps(_logText);

        var splitLines = _logText.Split('\n');
        bool inCdp = false;
        string prev = "";
        var portPat = new Regex(@"\b(Gig|Gi|Te|Ten|Fa|Fas|Twe|Tw|Hu|Hun|Port|Eth|Ethernet)\s*\d+(?:/\d+)*", RegexOptions.IgnoreCase);

        foreach (var line in splitLines)
        {
            if (line.Contains("Device ID") && line.Contains("Local Intrfce"))
            {
                inCdp = true;
                continue;
            }
            if (inCdp && (line.Contains("#") || line.Contains("Total") || line.Contains("====") || line.Contains("Command:")))
            {
                inCdp = false;
                continue;
            }
            if (inCdp)
            {
                var matches = portPat.Matches(line);
                if (matches.Count > 0)
                {
                    string key = NormalizeIntf(matches[0].Value);
                    if (ports.TryGetValue(key, out var p))
                    {
                        string rawH = line.Substring(0, matches[0].Index).Trim();
                        string neighHost = string.IsNullOrEmpty(rawH) ? prev : rawH;

                        p.NeighbourHostname = neighHost;
                        p.NeighbourPortNo = matches[^1].Value.Trim();

                        if (cdpIpDict.TryGetValue(neighHost, out var resolvedIp))
                        {
                            p.NeighbourDeviceIp = resolvedIp;
                        }
                    }
                }
            }
            prev = line.Trim();
        }

        // Live captures use detail output; retain the summary parser for old files.
        foreach (var block in Regex.Split(_logText, @"Device ID:", RegexOptions.IgnoreCase).Skip(1))
        {
            var host = block.Split(['\r', '\n'])[0].Trim();
            var local = Regex.Match(block, @"Interface:\s*([^,\r\n]+)", RegexOptions.IgnoreCase);
            var remote = Regex.Match(block, @"Port ID \(outgoing port\):\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var ip = Regex.Match(block, @"(?:IP|IPv4) address:\s*((?:\d{1,3}\.){3}\d{1,3})", RegexOptions.IgnoreCase);
            if (local.Success && ports.TryGetValue(NormalizeIntf(local.Groups[1].Value.Trim()), out var port))
            {
                port.NeighbourHostname = host;
                port.NeighbourPortNo = remote.Success ? remote.Groups[1].Value.Trim() : "-";
                port.NeighbourDeviceIp = ip.Success ? ip.Groups[1].Value : "-";
            }
        }

        if (returnAll)
        {
            return ports.Values.ToList();
        }

        return ports.Values.Where(p => p.NeighbourHostname != "-").ToList();
    }

    public List<SfpInventory> ParseSfpInventory()
    {
        var sfps = new List<SfpInventory>();
        var portStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(_logText, @"^([A-Za-z0-9/]+)\s+(?:\S+)\s+(?:\w+)\s+(?:\w+)\s+(up|down|administratively down)", RegexOptions.Multiline))
        {
            portStates[NormalizeIntf(m.Groups[1].Value)] = m.Groups[2].Value.ToLowerInvariant();
        }

        string pattern = @"NAME:\s*""([^""]+)"",\s*DESCR:\s*""([^""]+)""(?:\r?\n)*PID:\s*([^,]*?)\s*,\s*VID:\s*([^,]*?)\s*,\s*SN:\s*(.*)";
        foreach (Match match in Regex.Matches(_logText, pattern, RegexOptions.IgnoreCase))
        {
            string name = match.Groups[1].Value.Trim();
            string desc = match.Groups[2].Value.Trim();
            string pid = match.Groups[3].Value.Trim();
            string sn = match.Groups[5].Value.Trim();
            if (string.IsNullOrWhiteSpace(sn)) sn = "N/A";

            if (Regex.IsMatch(desc, @"(SFP|QSFP|XFP|Transceiver|GLC-|FET-|BIDI|10G|40G|100G|25G|CWDM|DWDM)", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(pid, @"(SFP|QSFP|XFP|GLC-|FET-|BIDI|CWDM|DWDM)", RegexOptions.IgnoreCase))
            {
                string k = NormalizeIntf(name);
                string state = portStates.GetValueOrDefault(k, "unknown");
                string inUse = state == "up" ? "Yes" : (state is "down" or "administratively down" ? "No" : "Unknown");

                sfps.Add(new SfpInventory
                {
                    Location = _location,
                    LocationType = _locationType,
                    Hostname = _hostname,
                    DeviceIp = _mgmtIp,
                    Interface = name,
                    Description = desc,
                    Pid = pid,
                    SerialNumber = sn,
                    InUse = inUse
                });
            }
        }

        return sfps;
    }
}
