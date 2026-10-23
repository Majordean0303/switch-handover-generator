using System.Collections.Generic;

namespace VisioCreator.Core.Models;

public class Interface
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Vlans { get; set; } = string.Empty;
    public bool IsShutdown { get; set; }
    public string Speed { get; set; } = string.Empty;
    public string Duplex { get; set; } = string.Empty;
}

public class PortChannel
{
    public string Id { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public List<string> MemberPorts { get; set; } = new();
}

public record Device
{
    public string Hostname { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? Model { get; set; }
    public string? DeviceType { get; set; }
    public string? SerialNumber { get; set; }
    public string? Location { get; set; }
    public string? StackId { get; set; }
    public string? Vendor { get; set; }
    public string? Site { get; set; }
    public string Role { get; set; } = "Unknown";

    // New Module 3 properties
    public int Tier { get; set; }
    public string? StencilKey { get; set; }
    public bool IsAp { get; set; }
    public bool IsPhone { get; set; }
    public bool IsExternal { get; set; }
    public string? ParentSwitch { get; set; }
    public string? ParentPort { get; set; }
    public string? CdpPlatform { get; set; }
    public string? CdpCapability { get; set; }
    public List<Interface> Interfaces { get; set; } = new();
    public List<PortChannel> PortChannels { get; set; } = new();

    public bool IsPhoneDevice =>
        IsPhone ||
        Role is "Phone" or "IP Phones" ||
        StencilKey == "ip_phone" ||
        (Model?.Contains("IP Phone", System.StringComparison.OrdinalIgnoreCase) == true) ||
        (Model?.StartsWith("CP-", System.StringComparison.OrdinalIgnoreCase) == true) ||
        (Hostname.StartsWith("SEP", System.StringComparison.OrdinalIgnoreCase) && Hostname.Length == 15) ||
        Hostname.StartsWith("ATA", System.StringComparison.OrdinalIgnoreCase) ||
        Hostname.StartsWith("CIP-", System.StringComparison.OrdinalIgnoreCase) ||
        (CdpCapability?.Contains("Host Phone", System.StringComparison.OrdinalIgnoreCase) == true);

    public bool IsApDevice =>
        IsAp ||
        Role == "AP" ||
        StencilKey == "ap" ||
        (CdpCapability?.Contains("WLAN Access Point", System.StringComparison.OrdinalIgnoreCase) == true) ||
        (Model?.Contains("AIR-", System.StringComparison.OrdinalIgnoreCase) == true) ||
        (Model?.Contains("C91", System.StringComparison.OrdinalIgnoreCase) == true) ||
        (CdpPlatform?.Contains("C91", System.StringComparison.OrdinalIgnoreCase) == true);
}

public enum LinkType
{
    Uplink,
    PortChannel,
    AP,
    Printer,
    Phone,
    Access,
    Unknown
}

public record Link
{
    public string Source { get; set; } = string.Empty;
    public string SourceInterface { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string DestinationInterface { get; set; } = string.Empty;
    public string? LinkSpeed { get; set; }
    public string? MediaType { get; set; }
    public string? Description { get; set; }
    public LinkType LinkType { get; set; } = LinkType.Unknown;
    public bool IsFiber { get; set; }
}

public class LogicalLink
{
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public LinkType Type { get; set; } = LinkType.Unknown;
    public string? MediaType { get; set; }
    public bool IsFiber { get; set; }
    
    // For PortChannels
    public string? SourcePortChannelId { get; set; }
    public string? DestinationPortChannelId { get; set; }
    public string? Mode { get; set; }
    
    // Physical links making up this logical link (often A->B and B->A CDP neighbors)
    public List<Link> MemberLinks { get; set; } = new();

    public string Id => string.Compare(Source, Destination, StringComparison.OrdinalIgnoreCase) < 0 
        ? $"{Source}-{Destination}" 
        : $"{Destination}-{Source}";
}

public class ValidationError
{
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? Row { get; set; }
}
