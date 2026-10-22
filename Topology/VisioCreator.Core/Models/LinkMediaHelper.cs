// VisioCreator.Core/Models/LinkMediaHelper.cs
using System;
using System.Text.RegularExpressions;

namespace VisioCreator.Core.Models;

/// <summary>
/// Helper to classify network links as Fiber (SFP / optical based) or Copper based on
/// media descriptions, transceivers, device roles, and interface port nomenclature.
/// </summary>
public static class LinkMediaHelper
{
    private static readonly string[] CopperKeywords =
    {
        "copper", "basetx", "base-tx", "base-t", "cat6", "cat5", "cat5e", "cat6a",
        "cat7", "rj45", "rj-45", "utp", "stp", "glc-t", "glc-te"
    };

    private static readonly string[] FiberKeywords =
    {
        "fiber", "fibre", "optic", "optical", "baselx", "base-lx", "basesx", "base-sx",
        "baselr", "base-lr", "basesr", "base-sr", "basezr", "base-zr", "baseer", "base-er",
        "glc-lh", "glc-sx", "smf", "mmf", "cwdm", "dwdm"
    };

    /// <summary>
    /// Determines whether a link represents a Fiber (SFP / optical) connection.
    /// Returns true for Fiber, false for Copper.
    /// </summary>
    public static bool DetermineIsFiber(Link link, Device? srcDevice = null, Device? dstDevice = null)
    {
        return DetermineIsFiber(
            link.MediaType,
            link.Description,
            link.SourceInterface,
            link.DestinationInterface,
            srcDevice?.Role,
            dstDevice?.Role,
            link.LinkType);
    }

    /// <summary>
    /// Determines whether link attributes represent Fiber (true) or Copper (false).
    /// </summary>
    public static bool DetermineIsFiber(
        string? mediaType,
        string? description,
        string? port1 = null,
        string? port2 = null,
        string? role1 = null,
        string? role2 = null,
        LinkType linkType = LinkType.Unknown)
    {
        // 1. AP / Printer / IP Phone endpoint devices or link types are always copper
        if (linkType is LinkType.AP or LinkType.Printer or LinkType.Phone)
            return false;

        if (IsLeafRole(role1) || IsLeafRole(role2))
            return false;

        // 2. Combine textual media information from Media Type, Description, and any annotations
        string combinedText = $"{mediaType} {description}".Trim();
        if (!string.IsNullOrEmpty(combinedText))
        {
            var textLower = combinedText.ToLowerInvariant();

            // Check copper indicators first (e.g. "10/100/1000BaseTX SFP", "GLC-TE" are copper SFPs)
            foreach (var copper in CopperKeywords)
            {
                if (textLower.Contains(copper))
                    return false;
            }

            // Check fiber indicators
            foreach (var fiber in FiberKeywords)
            {
                if (textLower.Contains(fiber))
                    return true;
            }

            // Other generic SFP keywords without copper indicator (e.g. "SFP", "SFP+", "SFP28", "QSFP", "QSFP28", "XFP")
            if (textLower.Contains("sfp") || textLower.Contains("qsfp") || textLower.Contains("xfp"))
                return true;
        }

        // 3. Port nomenclature analysis
        if (IsFiberPort(port1) || IsFiberPort(port2))
            return true;

        if (IsCopperPort(port1) && IsCopperPort(port2))
            return false;

        // 4. Default backbone connections between Core / Distribution tiers without explicit copper markers
        if (IsBackboneRole(role1) && IsBackboneRole(role2))
            return true;

        // Default to copper
        return false;
    }

    private static bool IsLeafRole(string? role) =>
        role is "AP" or "Printer" or "Phone" or "IP Phone" or "IP Phones";

    private static bool IsBackboneRole(string? role) =>
        role is "Core" or "Distribution" or "Router" or "Firewall";

    public static bool IsFiberPort(string? port)
    {
        if (string.IsNullOrWhiteSpace(port)) return false;
        var norm = PortKey.Normalize(port);

        // 10G, 25G, 40G, 100G interfaces are optical SFP+/SFP28/QSFP
        if (norm.StartsWith("TenGigabitEthernet", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("TwentyFiveGigE", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("FortyGigabitEthernet", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("HundredGigE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Modular uplink slots (e.g. Gi1/1/1, Gi2/1/1, Gig 1/1/1) on fixed access switches
        // Port numbers: <switch>/<module>/<port> where module >= 1
        var match = Regex.Match(norm, @"GigabitEthernet\d+/([1-9]\d*)/\d+", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return true;
        }

        return false;
    }

    public static bool IsCopperPort(string? port)
    {
        if (string.IsNullOrWhiteSpace(port)) return false;
        var norm = PortKey.Normalize(port);

        if (norm.StartsWith("FastEthernet", StringComparison.OrdinalIgnoreCase))
            return true;

        // Standard fixed access ports: GigabitEthernet<switch>/0/<port>
        var match = Regex.Match(norm, @"GigabitEthernet\d+/0/\d+", RegexOptions.IgnoreCase);
        if (match.Success)
            return true;

        return false;
    }
}
