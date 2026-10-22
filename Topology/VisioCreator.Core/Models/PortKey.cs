// VisioCreator.Core/Models/PortKey.cs
using System;
using System.Text.RegularExpressions;

namespace VisioCreator.Core.Models;

/// <summary>
/// Canonical interface and port resolver ensuring consistent port naming across Excel,
/// Cisco configs, CDP/LLDP neighbors, and diagram rendering.
/// Resolves abbreviations such as "Gig", "Gigabit", "Gi", "GE" to canonical "GigabitEthernet".
/// </summary>
public static class PortKey
{
    /// <summary>
    /// Normalizes any port or interface variant (e.g. "Gig 1/0/1", "Gigabit 1/0/1", "Gi1/0/1")
    /// to its canonical full name ("GigabitEthernet1/0/1").
    /// </summary>
    public static string Normalize(string? port)
    {
        if (string.IsNullOrWhiteSpace(port)) return string.Empty;

        var n = port.Trim();
        // Remove spaces around slashes: '1 / 0 / 1' -> '1/0/1'
        n = Regex.Replace(n, @"\s*/\s*", "/");

        // Exact type keywords without digits
        if (Regex.IsMatch(n, @"^(Gi(g[a-zA-Z]*)?|GE)$", RegexOptions.IgnoreCase))
            return "GigabitEthernet";
        if (Regex.IsMatch(n, @"^(Te(n[a-zA-Z]*)?|10GE|XGE)$", RegexOptions.IgnoreCase))
            return "TenGigabitEthernet";
        if (Regex.IsMatch(n, @"^(Twe(n[a-zA-Z]*)?|25GE)$", RegexOptions.IgnoreCase))
            return "TwentyFiveGigE";
        if (Regex.IsMatch(n, @"^(Fo(r[a-zA-Z]*)?|40GE)$", RegexOptions.IgnoreCase))
            return "FortyGigabitEthernet";
        if (Regex.IsMatch(n, @"^(Hu(n[a-zA-Z]*)?|100GE)$", RegexOptions.IgnoreCase))
            return "HundredGigE";
        if (Regex.IsMatch(n, @"^(Fa(s[a-zA-Z]*)?|FE)$", RegexOptions.IgnoreCase))
            return "FastEthernet";
        if (Regex.IsMatch(n, @"^(Eth(ernet)?|Et)$", RegexOptions.IgnoreCase))
            return "Ethernet";
        if (Regex.IsMatch(n, @"^(Po(rt[-_ ]?channel)?|PC)$", RegexOptions.IgnoreCase))
            return "Port-channel";
        if (Regex.IsMatch(n, @"^(Vl(an)?)$", RegexOptions.IgnoreCase))
            return "Vlan";
        if (Regex.IsMatch(n, @"^(Lo(opback)?)$", RegexOptions.IgnoreCase))
            return "Loopback";

        // Prefixes followed by digits
        n = Regex.Replace(n, @"^(Gi(g[a-zA-Z]*)?|GE)\s*(?=\d)", "GigabitEthernet", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^(Te(n[a-zA-Z]*)?|10GE|XGE)\s*(?=\d)", "TenGigabitEthernet", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^(Twe(n[a-zA-Z]*)?|25GE)\s*(?=\d)", "TwentyFiveGigE", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^(Fo(r[a-zA-Z]*)?|40GE)\s*(?=\d)", "FortyGigabitEthernet", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^(Hu(n[a-zA-Z]*)?|100GE)\s*(?=\d)", "HundredGigE", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^(Fa(s[a-zA-Z]*)?|FE)\s*(?=\d)", "FastEthernet", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^(Eth(ernet)?|Et)\s*(?=\d)", "Ethernet", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^(Po(rt[-_ ]?channel)?|PC)\s*(?=\d)", "Port-channel", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^(Vl(an)?)\s*(?=\d)", "Vlan", RegexOptions.IgnoreCase);
        n = Regex.Replace(n, @"^(Lo(opback)?)\s*(?=\d)", "Loopback", RegexOptions.IgnoreCase);

        // IP Phone port variations: Port 1, Port1, P1, <MAC>:P1
        var phoneMatch = Regex.Match(n, @"^(?:(?:[0-9a-fA-F]{4}\.[0-9a-fA-F]{4}\.[0-9a-fA-F]{4}|[0-9a-fA-F]{12}|[0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5}):)?P(\d+)$", RegexOptions.IgnoreCase);
        if (phoneMatch.Success)
            return $"Port {phoneMatch.Groups[1].Value}";
        var portNumMatch = Regex.Match(n, @"^Port\s*(\d+)$", RegexOptions.IgnoreCase);
        if (portNumMatch.Success)
            return $"Port {portNumMatch.Groups[1].Value}";

        return n;
    }

    public static string Of(string? port) => Normalize(port);

    /// <summary>
    /// Checks if two port strings refer to the same interface regardless of abbreviation or spacing.
    /// E.g. Same("Gig 1/0/1", "GigabitEthernet1/0/1") => true.
    /// Also resolves IP Phone "Port 1" vs "1" or "P1".
    /// </summary>
    public static bool Same(string? a, string? b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (string.IsNullOrEmpty(na) || string.IsNullOrEmpty(nb))
            return false;
        if (string.Equals(na, nb, StringComparison.OrdinalIgnoreCase))
            return true;

        // One is "Port <N>" and other is "<N>"
        var matchA = Regex.Match(na, @"^Port\s*(\d+)$", RegexOptions.IgnoreCase);
        if (matchA.Success && string.Equals(matchA.Groups[1].Value, nb, StringComparison.OrdinalIgnoreCase))
            return true;

        var matchB = Regex.Match(nb, @"^Port\s*(\d+)$", RegexOptions.IgnoreCase);
        if (matchB.Success && string.Equals(matchB.Groups[1].Value, na, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Converts a port name to standard short form for diagram badges (e.g. "Gi1/0/1", "Te1/1/1", "Po1").
    /// </summary>
    public static string FormatShort(string? port)
    {
        if (string.IsNullOrWhiteSpace(port)) return string.Empty;
        var norm = Normalize(port);
        return norm
            .Replace("TwentyFiveGigE", "25GE")
            .Replace("TenGigabitEthernet", "Te")
            .Replace("FortyGigabitEthernet", "40GE")
            .Replace("HundredGigE", "100GE")
            .Replace("GigabitEthernet", "Gi")
            .Replace("FastEthernet", "Fa")
            .Replace("Port-channel", "Po")
            .Replace("Ethernet", "Eth")
            .Replace("Loopback", "Lo")
            .Replace("Vlan", "Vl");
    }
}
