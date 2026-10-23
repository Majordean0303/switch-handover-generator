using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VisioCreator.Core.Models;

namespace VisioCreator.Core.Parsing;

public class ParsedBackup
{
    public string Hostname { get; set; } = string.Empty;
    public string ManagementIp { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string SoftwareVersion { get; set; } = string.Empty;
    
    public List<ParsedInterface> Interfaces { get; set; } = new();
    public List<ParsedPortChannel> PortChannels { get; set; } = new();
    public List<ParsedNeighbor> Neighbors { get; set; } = new();
    public Dictionary<string, string> InterfaceTransceivers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    
    public int UnparsedLineCount { get; set; }
}

public class ParsedInterface
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty; // access, trunk
    public string Vlans { get; set; } = string.Empty;
    public bool IsShutdown { get; set; }
    public string Speed { get; set; } = string.Empty;
    public string Duplex { get; set; } = string.Empty;
    public string ChannelGroupId { get; set; } = string.Empty;
    public string ChannelGroupMode { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}

public class ParsedPortChannel
{
    public string Id { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public List<string> MemberPorts { get; set; } = new();
}

public class ParsedNeighbor
{
    public string LocalPort { get; set; } = string.Empty;
    public string RemoteHostname { get; set; } = string.Empty;
    public string RemotePort { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Capabilities { get; set; } = string.Empty;
    public string RemoteIp { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty; // CDP or LLDP
}

public class ConfigParser
{
    private readonly Action<string, int, string> _unparsedLogger;

    public ConfigParser(Action<string, int, string>? unparsedLogger = null)
    {
        _unparsedLogger = unparsedLogger ?? ((file, lineNo, line) => { });
    }

    public static string NormalizeInterfaceName(string name) => PortKey.Normalize(name);

    public ParsedBackup ParseFile(string filePath, string[]? mgmtIpOrder = null)
    {
        mgmtIpOrder ??= new[] { "mgmt", "vlan", "loopback" };
        var backup = new ParsedBackup();
        
        try
        {
            var lines = File.ReadAllLines(filePath);
            var sections = SplitIntoSections(lines);
            
            foreach (var section in sections)
            {
                ParseSection(section, backup, filePath);
            }

            DetermineManagementIp(backup, mgmtIpOrder);
            LinkPortChannels(backup);
        }
        catch (Exception)
        {
            // The parser never throws on bad input
        }

        return backup;
    }

    public ParsedBackup ParseText(string text, string identifier = "live_session", string[]? mgmtIpOrder = null)
    {
        mgmtIpOrder ??= new[] { "mgmt", "vlan", "loopback" };
        var backup = new ParsedBackup();
        
        try
        {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sections = SplitIntoSections(lines);
            
            foreach (var section in sections)
            {
                ParseSection(section, backup, identifier);
            }

            DetermineManagementIp(backup, mgmtIpOrder);
            LinkPortChannels(backup);
        }
        catch (Exception)
        {
            // The parser never throws on bad input
        }

        return backup;
    }

    private void DetermineManagementIp(ParsedBackup backup, string[] order)
    {
        var interfaces = backup.Interfaces.Where(i => !string.IsNullOrEmpty(i.IpAddress)).ToList();
        foreach (var prefix in order)
        {
            foreach (var intf in interfaces)
            {
                if (intf.Name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    backup.ManagementIp = intf.IpAddress;
                    return;
                }
            }
        }
        
        if (string.IsNullOrEmpty(backup.ManagementIp) && interfaces.Any())
        {
            backup.ManagementIp = interfaces.First().IpAddress;
        }
    }

    private void LinkPortChannels(ParsedBackup backup)
    {
        foreach (var intf in backup.Interfaces)
        {
            if (!string.IsNullOrEmpty(intf.ChannelGroupId))
            {
                var pc = backup.PortChannels.FirstOrDefault(p => p.Id == intf.ChannelGroupId);
                if (pc == null)
                {
                    pc = new ParsedPortChannel { Id = intf.ChannelGroupId, Mode = intf.ChannelGroupMode };
                    backup.PortChannels.Add(pc);
                }
                if (!pc.MemberPorts.Contains(intf.Name))
                {
                    pc.MemberPorts.Add(intf.Name);
                }
                // Mode might be extracted from interface if not present in PC
                if (string.IsNullOrEmpty(pc.Mode)) pc.Mode = intf.ChannelGroupMode;
            }
        }
    }

    private List<Section> SplitIntoSections(string[] lines)
    {
        var sections = new List<Section>();
        Section? current = null;
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            
            // Match custom headers
            var customMatch = Regex.Match(line, @"^Command:\s+(.*)$", RegexOptions.IgnoreCase);
            // Match typical CLI prompts like "JSW_SW1#sh run"
            var cliMatch = Regex.Match(line, @"^\S+[#>]\s*(sh(?:ow)?\s+.*)$", RegexOptions.IgnoreCase);

            if (customMatch.Success || cliMatch.Success)
            {
                if (current != null) sections.Add(current);
                var cmdText = customMatch.Success ? customMatch.Groups[1].Value : cliMatch.Groups[1].Value;
                var cmd = cmdText.Trim().ToLowerInvariant();
                
                if (cmd.StartsWith("sh ")) cmd = "show " + cmd.Substring(3).Trim();

                if (cmd.StartsWith("show run")) cmd = "show running-config";
                else if (cmd.StartsWith("show ver")) cmd = "show version";
                else if (cmd.StartsWith("show inv")) cmd = "show inventory";
                else if (cmd.StartsWith("show int") && (cmd.Contains("status") || cmd.Contains("stat"))) cmd = "show interfaces status";
                else if (Regex.IsMatch(cmd, @"^show\s+cdp\s+ne\S*\s+de", RegexOptions.IgnoreCase)) cmd = "show cdp neighbors detail";
                else if (Regex.IsMatch(cmd, @"^show\s+lldp\s+ne\S*\s+de", RegexOptions.IgnoreCase)) cmd = "show lldp neighbors detail";
                else if (Regex.IsMatch(cmd, @"^show\s+cdp\s+ne", RegexOptions.IgnoreCase)) cmd = "show cdp neighbors";
                else if (Regex.IsMatch(cmd, @"^show\s+lldp\s+ne", RegexOptions.IgnoreCase)) cmd = "show lldp neighbors";
                else if (cmd.StartsWith("show etherchannel") || cmd.StartsWith("show ether-channel")) cmd = "show etherchannel summary";
                else if (cmd.StartsWith("show ip int") || cmd.StartsWith("show ip interface")) cmd = "show ip interface brief";
                
                current = new Section { Command = cmd };
            }
            else if (current != null)
            {
                // Skip the equal signs separators
                if (!line.StartsWith("==========") && !line.StartsWith("----------"))
                {
                    current.Lines.Add(new LineInfo { Text = line, LineNumber = i + 1 });
                }
            }
        }
        if (current != null) sections.Add(current);
        
        return sections;
    }

    private void ParseSection(Section section, ParsedBackup backup, string filePath)
    {
        if (section.Command.StartsWith("show version")) ParseShowVersion(section, backup, filePath);
        else if (section.Command.StartsWith("show inventory")) ParseShowInventory(section, backup, filePath);
        else if (section.Command.StartsWith("show interfaces status")) ParseShowInterfacesStatus(section, backup, filePath);
        else if (section.Command.StartsWith("show running-config")) ParseShowRunningConfig(section, backup, filePath);
        else if (section.Command.StartsWith("show cdp neighbors detail")) ParseShowCdpNeighborsDetail(section, backup, filePath);
        else if (section.Command.StartsWith("show cdp neighbors")) ParseShowCdpNeighbors(section, backup, filePath);
        else if (section.Command.StartsWith("show lldp neighbors detail")) ParseShowLldpNeighborsDetail(section, backup, filePath);
        else if (section.Command.StartsWith("show lldp neighbors")) ParseShowLldpNeighbors(section, backup, filePath);
        else if (section.Command.StartsWith("show etherchannel summary")) ParseShowEtherchannelSummary(section, backup, filePath);
        else if (section.Command.StartsWith("show ip interface brief")) ParseShowIpInterfaceBrief(section, backup, filePath);
    }

    private void ParseShowVersion(Section section, ParsedBackup backup, string filePath)
    {
        foreach (var line in section.Lines)
        {
            var text = line.Text;
            var verMatch = Regex.Match(text, @"Version\s+([\w\.\(\)]+)(?:,|\s)");
            if (verMatch.Success && string.IsNullOrEmpty(backup.SoftwareVersion)) 
                backup.SoftwareVersion = verMatch.Groups[1].Value;

            var hwMatch = Regex.Match(text, @"[cC]isco\s+(\S+).+processor");
            if (hwMatch.Success && string.IsNullOrEmpty(backup.Model))
                backup.Model = hwMatch.Groups[1].Value;
        }
    }

    private void ParseShowInventory(Section section, ParsedBackup backup, string filePath)
    {
        for (int i = 0; i < section.Lines.Count; i++)
        {
            var text = section.Lines[i].Text;
            var nameMatch = Regex.Match(text, @"NAME:\s*""([^""]+)"",\s*DESCR:\s*""([^""]*)""", RegexOptions.IgnoreCase);
            if (nameMatch.Success)
            {
                var name = nameMatch.Groups[1].Value.Trim();
                var descr = nameMatch.Groups[2].Value.Trim();
                if (Regex.IsMatch(name, @"^(Gi|Te|Twe|Fo|Hu|Fa|Eth)", RegexOptions.IgnoreCase))
                {
                    var normIntf = NormalizeInterfaceName(name);
                    string transceiverInfo = descr;
                    if (i + 1 < section.Lines.Count)
                    {
                        var nextText = section.Lines[i + 1].Text;
                        var transPidMatch = Regex.Match(nextText, @"PID:\s*([^, ]+)");
                        if (transPidMatch.Success)
                        {
                            transceiverInfo = $"{descr} ({transPidMatch.Groups[1].Value})";
                        }
                    }
                    backup.InterfaceTransceivers[normIntf] = transceiverInfo;
                    backup.InterfaceTransceivers[PortKey.FormatShort(name)] = transceiverInfo;
                    backup.InterfaceTransceivers[name] = transceiverInfo;
                }
            }

            var pidMatch = Regex.Match(text, @"PID:\s*([^, ]+)");
            if (pidMatch.Success && string.IsNullOrEmpty(backup.Model)) backup.Model = pidMatch.Groups[1].Value;

            var snMatch = Regex.Match(text, @"SN:\s*(\S+)");
            if (snMatch.Success && string.IsNullOrEmpty(backup.SerialNumber)) backup.SerialNumber = snMatch.Groups[1].Value;
        }
    }

    private void ParseShowInterfacesStatus(Section section, ParsedBackup backup, string filePath)
    {
        foreach (var line in section.Lines)
        {
            var text = line.Text.Trim();
            if (text.StartsWith("Port ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("---") ||
                text.StartsWith("==="))
                continue;

            var match = Regex.Match(text, @"^([A-Za-z0-9\/\.\-]+)\s+.*?\s+(auto|\d+|a-\d+)\s+([A-Za-z0-9\/\.\-]+(?:\s+SFP)?)\s*$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var port = match.Groups[1].Value;
                var mediaType = match.Groups[3].Value;
                var norm = NormalizeInterfaceName(port);
                if (!backup.InterfaceTransceivers.ContainsKey(norm))
                {
                    backup.InterfaceTransceivers[norm] = mediaType;
                    backup.InterfaceTransceivers[PortKey.FormatShort(port)] = mediaType;
                    backup.InterfaceTransceivers[port] = mediaType;
                }
            }
        }
    }

    private void ParseShowRunningConfig(Section section, ParsedBackup backup, string filePath)
    {
        ParsedInterface? currentIntf = null;

        for (int i = 0; i < section.Lines.Count; i++)
        {
            var lineInfo = section.Lines[i];
            var text = lineInfo.Text.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            if (text.StartsWith("hostname ", StringComparison.OrdinalIgnoreCase))
            {
                backup.Hostname = text.Substring(9).Trim();
            }
            else if (text.StartsWith("interface ", StringComparison.OrdinalIgnoreCase))
            {
                currentIntf = new ParsedInterface { Name = NormalizeInterfaceName(text.Substring(10).Trim()) };
                backup.Interfaces.Add(currentIntf);
            }
            else if (currentIntf != null && lineInfo.Text.StartsWith(" "))
            {
                // inside interface block
                if (text.StartsWith("description ", StringComparison.OrdinalIgnoreCase))
                    currentIntf.Description = text.Substring(12).Trim();
                else if (text.StartsWith("shutdown", StringComparison.OrdinalIgnoreCase))
                    currentIntf.IsShutdown = true;
                else if (text.StartsWith("switchport mode ", StringComparison.OrdinalIgnoreCase))
                    currentIntf.Mode = text.Substring(16).Trim();
                else if (text.StartsWith("switchport access vlan ", StringComparison.OrdinalIgnoreCase))
                    currentIntf.Vlans = text.Substring(23).Trim();
                else if (text.StartsWith("switchport trunk allowed vlan ", StringComparison.OrdinalIgnoreCase))
                    currentIntf.Vlans = text.Substring(30).Trim();
                else if (text.StartsWith("channel-group ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = text.Split(' ');
                    if (parts.Length >= 2) currentIntf.ChannelGroupId = parts[1];
                    if (parts.Length >= 4 && parts[2].Equals("mode", StringComparison.OrdinalIgnoreCase))
                        currentIntf.ChannelGroupMode = parts[3];
                }
                else if (text.StartsWith("ip address ", StringComparison.OrdinalIgnoreCase) && !text.Contains("no ip address"))
                {
                    var parts = text.Split(' ');
                    if (parts.Length >= 3) currentIntf.IpAddress = parts[2];
                }
                else if (text.StartsWith("speed ", StringComparison.OrdinalIgnoreCase))
                    currentIntf.Speed = text.Substring(6).Trim();
                else if (text.StartsWith("duplex ", StringComparison.OrdinalIgnoreCase))
                    currentIntf.Duplex = text.Substring(7).Trim();
                else
                {
                    backup.UnparsedLineCount++;
                    _unparsedLogger(filePath, lineInfo.LineNumber, lineInfo.Text);
                }
            }
            else
            {
                currentIntf = null; // exit interface block
            }
        }
    }

    private void ParseShowCdpNeighborsDetail(Section section, ParsedBackup backup, string filePath)
    {
        ParsedNeighbor? current = null;
        for (int i = 0; i < section.Lines.Count; i++)
        {
            var lineInfo = section.Lines[i];
            var text = lineInfo.Text.Trim();
            
            if (text.StartsWith("Device ID: "))
            {
                if (current != null)
                {
                    AddNeighbor(backup, current);
                }
                current = new ParsedNeighbor { Protocol = "CDP", RemoteHostname = text.Substring(11).Trim() };
            }
            else if (current != null)
            {
                if (text.StartsWith("IP address: ", StringComparison.OrdinalIgnoreCase))
                    current.RemoteIp = text.Substring(12).Trim();
                else if (text.StartsWith("IPv4 Address: ", StringComparison.OrdinalIgnoreCase))
                    current.RemoteIp = text.Substring(14).Trim();
                else if (Regex.IsMatch(text, @"^(?:IP|IPv4)\s+[Aa]ddress\s*:\s*([\d\.]+)"))
                {
                    var ipM = Regex.Match(text, @"^(?:IP|IPv4)\s+[Aa]ddress\s*:\s*([\d\.]+)");
                    current.RemoteIp = ipM.Groups[1].Value.Trim();
                }
                else if (text.StartsWith("Platform: "))
                {
                    var match = Regex.Match(text, @"Platform:\s*([^,]+),\s*Capabilities:\s*(.+)");
                    if (match.Success)
                    {
                        current.Platform = match.Groups[1].Value.Trim();
                        current.Capabilities = match.Groups[2].Value.Trim();
                    }
                }
                else if (text.StartsWith("Interface: "))
                {
                    var match = Regex.Match(text, @"Interface:\s*([^,]+),\s*Port ID \(outgoing port\):\s*(.+)");
                    if (match.Success)
                    {
                        current.LocalPort = NormalizeInterfaceName(match.Groups[1].Value.Trim());
                        current.RemotePort = NormalizeInterfaceName(match.Groups[2].Value.Trim());
                    }
                    else
                    {
                        backup.UnparsedLineCount++;
                        _unparsedLogger(filePath, lineInfo.LineNumber, lineInfo.Text);
                    }
                }
                else
                {
                    // Ignore empty lines and known safe noise
                    if (!string.IsNullOrEmpty(text) && !text.StartsWith("Holdtime") && !text.StartsWith("Version") && !text.StartsWith("Cisco IOS") && !text.StartsWith("Technical") && !text.StartsWith("Copyright") && !text.StartsWith("Compiled") && !text.StartsWith("advertisement") && !text.StartsWith("Peer Source") && !text.StartsWith("VTP") && !text.StartsWith("Native VLAN") && !text.StartsWith("Duplex") && !text.StartsWith("Management address") && !text.StartsWith("Total cdp"))
                    {
                        backup.UnparsedLineCount++;
                        _unparsedLogger(filePath, lineInfo.LineNumber, lineInfo.Text);
                    }
                }
            }
        }
        if (current != null)
        {
            AddNeighbor(backup, current);
        }
    }

    private void ParseShowCdpNeighbors(Section section, ParsedBackup backup, string filePath)
    {
        string? pendingHost = null;

        for (int i = 0; i < section.Lines.Count; i++)
        {
            var text = section.Lines[i].Text.Trim();
            if (string.IsNullOrEmpty(text) ||
                text.StartsWith("Capability", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Device ID", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Total ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("(") ||
                text.StartsWith("%") ||
                text.StartsWith("---") ||
                text.StartsWith("==="))
            {
                continue;
            }

            var matchIndented = Regex.Match(text, @"^([A-Za-z0-9\/\.\-]+(?:\s+\d+\/\d+(?:\/\d+)?)?)\s+(\d+)\s+([A-Za-z\s]+?)\s+((?:IP\s+Phone|[^\s]+))\s+(Port\s+\d+|(?:Gig|Ten|Twe|Fas|Eth)\s+\d+(?:\/\d+)*|[A-Za-z0-9\/\.\-]+(?:\s+\d+\/\d+(?:\/\d+)?)?)$");
            if (matchIndented.Success && !string.IsNullOrEmpty(pendingHost))
            {
                var localPort = NormalizeInterfaceName(matchIndented.Groups[1].Value.Trim());
                var platform = matchIndented.Groups[4].Value.Trim();
                var remotePort = NormalizeInterfaceName(matchIndented.Groups[5].Value.Trim());

                if (pendingHost.StartsWith("SEP", StringComparison.OrdinalIgnoreCase) || platform.Contains("Phone", StringComparison.OrdinalIgnoreCase))
                {
                    if (remotePort == "1" || remotePort.StartsWith("P1", StringComparison.OrdinalIgnoreCase))
                        remotePort = "Port 1";
                    if (platform.Equals("Port", StringComparison.OrdinalIgnoreCase))
                        platform = "IP Phone";
                }

                var neighbor = new ParsedNeighbor
                {
                    Protocol = "CDP",
                    RemoteHostname = pendingHost,
                    LocalPort = localPort,
                    RemotePort = remotePort,
                    Platform = platform
                };
                AddNeighbor(backup, neighbor);
                pendingHost = null;
                continue;
            }

            var matchSingle = Regex.Match(text, @"^(\S+)\s+([A-Za-z0-9\/\.\-]+(?:\s+\d+\/\d+(?:\/\d+)?)?)\s+(\d+)\s+([A-Za-z\s]+?)\s+((?:IP\s+Phone|[^\s]+))\s+(Port\s+\d+|(?:Gig|Ten|Twe|Fas|Eth)\s+\d+(?:\/\d+)*|[A-Za-z0-9\/\.\-]+(?:\s+\d+\/\d+(?:\/\d+)?)?)$");
            if (matchSingle.Success)
            {
                var host = matchSingle.Groups[1].Value.Trim();
                var localPort = NormalizeInterfaceName(matchSingle.Groups[2].Value.Trim());
                var platform = matchSingle.Groups[5].Value.Trim();
                var remotePort = NormalizeInterfaceName(matchSingle.Groups[6].Value.Trim());

                if (host.StartsWith("SEP", StringComparison.OrdinalIgnoreCase) || platform.Contains("Phone", StringComparison.OrdinalIgnoreCase))
                {
                    if (remotePort == "1" || remotePort.StartsWith("P1", StringComparison.OrdinalIgnoreCase))
                        remotePort = "Port 1";
                    if (platform.Equals("Port", StringComparison.OrdinalIgnoreCase))
                        platform = "IP Phone";
                }

                var neighbor = new ParsedNeighbor
                {
                    Protocol = "CDP",
                    RemoteHostname = host,
                    LocalPort = localPort,
                    RemotePort = remotePort,
                    Platform = platform
                };
                AddNeighbor(backup, neighbor);
                pendingHost = null;
                continue;
            }

            if (!text.Contains(" ") && text.Length > 2)
            {
                pendingHost = text;
            }
        }
    }

    private void ParseShowLldpNeighborsDetail(Section section, ParsedBackup backup, string filePath)
    {
        ParsedNeighbor? current = null;
        string? candidateChassisId = null;

        for (int i = 0; i < section.Lines.Count; i++)
        {
            var lineInfo = section.Lines[i];
            var text = lineInfo.Text.Trim();
            
            if (text.StartsWith("Local Intf: "))
            {
                if (current != null)
                {
                    if (string.IsNullOrEmpty(current.RemoteHostname) && !string.IsNullOrEmpty(candidateChassisId) && !IsMacAddress(candidateChassisId))
                    {
                        current.RemoteHostname = candidateChassisId;
                    }
                    AddNeighbor(backup, current);
                }
                current = new ParsedNeighbor { Protocol = "LLDP", LocalPort = NormalizeInterfaceName(text.Substring(12).Trim()) };
                candidateChassisId = null;
            }
            else if (current != null)
            {
                if (text.StartsWith("Chassis id: "))
                {
                    var cid = text.Substring(12).Trim();
                    if (!cid.Equals("- not advertised", StringComparison.OrdinalIgnoreCase))
                        candidateChassisId = cid;
                }
                else if (text.StartsWith("System Name: "))
                {
                    var sname = text.Substring(13).Trim();
                    if (!sname.Equals("- not advertised", StringComparison.OrdinalIgnoreCase))
                        current.RemoteHostname = sname;
                }
                else if (text.StartsWith("Port id: "))
                {
                    var pid = text.Substring(9).Trim();
                    if (!pid.Equals("- not advertised", StringComparison.OrdinalIgnoreCase))
                        current.RemotePort = NormalizeInterfaceName(pid);
                }
                else if (text.StartsWith("System Description: "))
                {
                    var sdesc = text.Substring(20).Trim();
                    if (!sdesc.Equals("- not advertised", StringComparison.OrdinalIgnoreCase))
                        current.Platform = sdesc;
                }
                else if (text.StartsWith("System Capabilities: "))
                {
                    var caps = text.Substring(21).Trim();
                    if (!caps.Equals("- not advertised", StringComparison.OrdinalIgnoreCase))
                        current.Capabilities = caps;
                }
                else if (text.StartsWith("Device type: "))
                {
                    if (text.Contains("Endpoint Class", StringComparison.OrdinalIgnoreCase))
                        current.Capabilities = "Endpoint Class";
                }
                else if (text.StartsWith("Management Address:") || text.StartsWith("IP: "))
                {
                    if (text.StartsWith("IP: "))
                    {
                        current.RemoteIp = text.Substring(4).Trim();
                    }
                    else if (i + 1 < section.Lines.Count)
                    {
                        var nextLine = section.Lines[i + 1].Text.Trim();
                        if (Regex.IsMatch(nextLine, @"^\d+\.\d+\.\d+\.\d+"))
                            current.RemoteIp = nextLine;
                    }
                }
            }
        }
        if (current != null)
        {
            if (string.IsNullOrEmpty(current.RemoteHostname) && !string.IsNullOrEmpty(candidateChassisId) && !IsMacAddress(candidateChassisId))
            {
                current.RemoteHostname = candidateChassisId;
            }
            AddNeighbor(backup, current);
        }
    }

    private void ParseShowLldpNeighbors(Section section, ParsedBackup backup, string filePath)
    {
        for (int i = 0; i < section.Lines.Count; i++)
        {
            var text = section.Lines[i].Text.Trim();
            if (string.IsNullOrEmpty(text) ||
                text.StartsWith("Capability", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Device ID", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Total ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("(") ||
                text.StartsWith("%") ||
                text.StartsWith("---") ||
                text.StartsWith("==="))
            {
                continue;
            }

            var matchConcat = Regex.Match(text, @"^(.+?)(Gi\d+\S*|Te\d+\S*|Twe\d+\S*|Fa\d+\S*|Eth\d+\S*)\s+(\d+)\s+([A-Za-z,\s]*?)\s+([A-Za-z0-9\/\.\-]+)\s*$");
            if (matchConcat.Success)
            {
                var host = matchConcat.Groups[1].Value.Trim().TrimEnd('.');
                var localPort = NormalizeInterfaceName(matchConcat.Groups[2].Value.Trim());
                var remotePort = NormalizeInterfaceName(matchConcat.Groups[5].Value.Trim());

                var neighbor = new ParsedNeighbor
                {
                    Protocol = "LLDP",
                    RemoteHostname = host,
                    LocalPort = localPort,
                    RemotePort = remotePort
                };
                AddNeighbor(backup, neighbor);
                continue;
            }

            var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5)
            {
                var neighbor = new ParsedNeighbor
                {
                    Protocol = "LLDP",
                    RemoteHostname = parts[0],
                    LocalPort = NormalizeInterfaceName(parts[1]),
                    RemotePort = NormalizeInterfaceName(parts[^1])
                };
                AddNeighbor(backup, neighbor);
            }
        }
    }

    public static bool IsMacAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        // Cisco 0000.0000.0000
        if (Regex.IsMatch(trimmed, @"^[0-9a-fA-F]{4}\.[0-9a-fA-F]{4}\.[0-9a-fA-F]{4}$")) return true;
        // Standard colon / hyphen 00:00:00:00:00:00
        if (Regex.IsMatch(trimmed, @"^[0-9a-fA-F]{2}(?:[:-][0-9a-fA-F]{2}){5}$")) return true;
        // Raw 12 hex
        if (Regex.IsMatch(trimmed, @"^[0-9a-fA-F]{12}$")) return true;
        return false;
    }

    public static bool IsClientEndpoint(string? host, string? remotePort, string? platform = null, string? capabilities = null)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        var trimmedHost = host.Trim();

        // Host is raw MAC
        if (IsMacAddress(trimmedHost)) return true;

        // Remote port is a MAC address (e.g. LLDP learned MAC address on switch access port)
        if (IsMacAddress(remotePort)) return true;

        // CLI header noise / capability code lines / syslog prompts / wrapped fragments
        if (trimmedHost.StartsWith("(") || 
            trimmedHost.StartsWith("%") || 
            trimmedHost.StartsWith("---") ||
            trimmedHost.StartsWith("===") ||
            trimmedHost.Equals("Gig", StringComparison.OrdinalIgnoreCase) ||
            trimmedHost.Equals("Te", StringComparison.OrdinalIgnoreCase) ||
            trimmedHost.Equals("Twe", StringComparison.OrdinalIgnoreCase) ||
            trimmedHost.Equals("Fa", StringComparison.OrdinalIgnoreCase) ||
            trimmedHost.Equals("Eth", StringComparison.OrdinalIgnoreCase) ||
            trimmedHost.Equals("Total", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Hostname represents end-user client PCs, laptops, desktops, or workstations:
        // Examples: JSWCL-NGR-D0050, JSWCL-NGR-D0058, JSWCL-NAN-L0108, DESKTOP-7L19BN1, DELLJSW-0SE5JMI
        if (Regex.IsMatch(trimmedHost, @"^(?i)(?:[A-Z0-9]+-(?:D|L)\d+|DESKTOP-[A-Z0-9]+|DELL[A-Z0-9\-]+|LAPTOP-[A-Z0-9]+|PC-[A-Z0-9]+)$"))
        {
            return true;
        }

        // LLDP-MED endpoint class check
        if (!string.IsNullOrEmpty(platform) && platform.Contains("Endpoint Class", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!string.IsNullOrEmpty(capabilities) && capabilities.Contains("Endpoint Class", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static bool IsPrefixOrTruncatedMatch(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        var normA = HostKey.Of(a);
        var normB = HostKey.Of(b);
        if (normA.Equals(normB, StringComparison.OrdinalIgnoreCase)) return true;
        
        // One is truncated prefix of the other (Cisco table column cutoff at ~18-20 chars)
        if (normA.StartsWith(normB, StringComparison.OrdinalIgnoreCase) && normB.Length >= 8) return true;
        if (normB.StartsWith(normA, StringComparison.OrdinalIgnoreCase) && normA.Length >= 8) return true;
        return false;
    }

    private void ParseShowEtherchannelSummary(Section section, ParsedBackup backup, string filePath)
    {
        foreach (var lineInfo in section.Lines)
        {
            var text = lineInfo.Text.Trim();
            if (string.IsNullOrEmpty(text) || text.StartsWith("Group", StringComparison.OrdinalIgnoreCase) || text.StartsWith("---") || text.StartsWith("Flags:", StringComparison.OrdinalIgnoreCase))
                continue;

            var match = Regex.Match(text, @"^\s*(\d+)\s+([A-Za-z0-9\-]+)(?:\([^\)]*\))?\s+(?:[A-Za-z0-9\-]+)?\s+(.+)$");
            if (match.Success)
            {
                var grp = match.Groups[1].Value;
                var poName = match.Groups[2].Value;
                var portsText = match.Groups[3].Value;

                var pc = backup.PortChannels.FirstOrDefault(p => p.Id == grp || p.Id.Equals(poName, StringComparison.OrdinalIgnoreCase));
                if (pc == null)
                {
                    pc = new ParsedPortChannel { Id = grp, Mode = "active" };
                    backup.PortChannels.Add(pc);
                }

                var portMatches = Regex.Matches(portsText, @"([A-Za-z]+[0-9\/\.\-]+)(?:\([^\)]*\))?");
                foreach (Match pm in portMatches)
                {
                    var pName = NormalizeInterfaceName(pm.Groups[1].Value.Trim());
                    if (!pc.MemberPorts.Contains(pName))
                    {
                        pc.MemberPorts.Add(pName);
                    }
                }
            }
        }
    }

    private void ParseShowIpInterfaceBrief(Section section, ParsedBackup backup, string filePath)
    {
        foreach (var lineInfo in section.Lines)
        {
            var text = lineInfo.Text.Trim();
            var match = Regex.Match(text, @"^([A-Za-z0-9\/\.\-]+)\s+([\d\.]+)\s+(?:YES|NO)\s+\S+\s+up\s+up", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var intfName = NormalizeInterfaceName(match.Groups[1].Value);
                var ip = match.Groups[2].Value;
                if (ip != "unassigned" && !ip.StartsWith("127."))
                {
                    var existing = backup.Interfaces.FirstOrDefault(i => PortKey.Same(i.Name, intfName));
                    if (existing == null)
                    {
                        existing = new ParsedInterface { Name = intfName };
                        backup.Interfaces.Add(existing);
                    }
                    existing.IpAddress = ip;
                }
            }
        }
    }

    private static void AddNeighbor(ParsedBackup backup, ParsedNeighbor neighbor)
    {
        if (string.IsNullOrWhiteSpace(neighbor.LocalPort) || string.IsNullOrWhiteSpace(neighbor.RemoteHostname))
        {
            return;
        }

        if (IsClientEndpoint(neighbor.RemoteHostname, neighbor.RemotePort, neighbor.Platform, neighbor.Capabilities))
        {
            return;
        }

        var existing = backup.Neighbors.FirstOrDefault(n =>
            PortKey.Same(n.LocalPort, neighbor.LocalPort) &&
            (HostKey.Same(n.RemoteHostname, neighbor.RemoteHostname) ||
             IsPrefixOrTruncatedMatch(n.RemoteHostname, neighbor.RemoteHostname)));

        if (existing == null)
        {
            backup.Neighbors.Add(neighbor);
        }
        else
        {
            // Prefer longer, untruncated hostname
            if (neighbor.RemoteHostname.Length > existing.RemoteHostname.Length &&
                !neighbor.RemoteHostname.EndsWith("."))
            {
                existing.RemoteHostname = neighbor.RemoteHostname;
            }

            if (string.IsNullOrEmpty(existing.RemoteIp) && !string.IsNullOrEmpty(neighbor.RemoteIp))
                existing.RemoteIp = neighbor.RemoteIp;
            if (string.IsNullOrEmpty(existing.Platform) && !string.IsNullOrEmpty(neighbor.Platform))
                existing.Platform = neighbor.Platform;
            if (string.IsNullOrEmpty(existing.Capabilities) && !string.IsNullOrEmpty(neighbor.Capabilities))
                existing.Capabilities = neighbor.Capabilities;
            if (string.IsNullOrEmpty(existing.RemotePort) ||
                (existing.RemotePort == "1" && neighbor.RemotePort == "Port 1") ||
                (existing.RemotePort.Contains(":") && neighbor.RemotePort == "Port 1") ||
                (!string.IsNullOrEmpty(neighbor.RemotePort) && PortKey.Same(existing.RemotePort, neighbor.RemotePort) && neighbor.RemotePort.Length > existing.RemotePort.Length))
            {
                existing.RemotePort = neighbor.RemotePort;
            }

            if (existing.RemoteHostname.StartsWith("SEP", StringComparison.OrdinalIgnoreCase) && existing.RemotePort == "1")
            {
                existing.RemotePort = "Port 1";
            }
        }
    }
}

public class Section
{
    public string Command { get; set; } = string.Empty;
    public List<LineInfo> Lines { get; set; } = new();
}

public class LineInfo
{
    public string Text { get; set; } = string.Empty;
    public int LineNumber { get; set; }
}
