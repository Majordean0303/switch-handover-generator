// VisioCreator.Core/Models/HostKey.cs
namespace VisioCreator.Core.Models;

/// <summary>
/// One place that decides whether two hostnames are "the same device".
/// Use it for EVERY hostname lookup (tree, canvas, exporter, link building) so
/// 'SW1', 'sw1', 'SW1.corp.local' and 'SW1(FOC1234ABCD)' (CDP style) all match.
/// </summary>
public static class HostKey
{
    public static string Of(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname)) return string.Empty;

        var h = hostname.Trim();

        // CDP sometimes appends the serial: SW1(FOC1234ABCD)
        int paren = h.IndexOf('(');
        if (paren > 0) h = h[..paren];

        // Drop the domain suffix, but keep IP-looking names whole.
        int dot = h.IndexOf('.');
        if (dot > 0 && !char.IsDigit(h[0])) h = h[..dot];

        return h.ToUpperInvariant();
    }

    public static bool Same(string? a, string? b)
    {
        var ka = Of(a);
        return ka.Length > 0 && ka == Of(b);
    }
}
