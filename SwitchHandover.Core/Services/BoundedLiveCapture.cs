using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
namespace SwitchHandover.Core.Services;

public sealed record CaptureOptions(int Workers = 5, int MaxHops = 8, int MaxDevices = 512, bool Traverse = true, bool IncludeAps = true, string SubnetFilter = "");
public sealed record CaptureResult(List<string> Files, List<string> Errors);
public sealed class BoundedLiveCapture(ICommandSessionFactory factory)
{
    public static readonly string[] SwitchCommands = ["show version", "show inventory", "show idprom backplane", "show env power", "show environment", "show ip interface brief", "show interfaces status", "show cdp neighbors detail", "show running-config"];
    public static readonly string[] ApCommands = ["show run", "show interfaces wired 0", "show inventory"];
    public async Task<CaptureResult> RunAsync(IEnumerable<string> seeds, DeviceCredentials credentials, string folder, CaptureOptions? options = null, Action<string>? log = null, CancellationToken token = default)
    {
        options ??= new();
        if (options.Workers is < 1 or > 32 || options.MaxHops is < 0 or > 32 || options.MaxDevices is < 1 or > 2000)
            throw new ArgumentException("Invalid live capture limits.");
        credentials.Validate();
        var first = seeds.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (first.Length == 0 || first.Length > options.MaxDevices || first.Any(s => s.Any(char.IsWhiteSpace)))
            throw new ArgumentException("Supply valid seed addresses within the device limit.");
        _ = InScope("192.0.2.1", options.SubnetFilter);
        Directory.CreateDirectory(folder);
        var seen = new HashSet<string>(first, StringComparer.OrdinalIgnoreCase);
        var layer = first.Select(ip => (Ip: ip, IsAp: false)).ToList();
        var files = new ConcurrentBag<string>();
        var errors = new ConcurrentBag<string>();
        for (int depth = 0; layer.Count > 0 && depth <= options.MaxHops; depth++)
        {
            token.ThrowIfCancellationRequested();
            var peers = new ConcurrentBag<(string Ip, bool IsAp)>();
            await Parallel.ForEachAsync(layer, new ParallelOptions { MaxDegreeOfParallelism = options.Workers, CancellationToken = token }, async (target, ct) =>
            {
                try
                {
                    await using var session = await factory.ConnectAsync(target.Ip, credentials, new(false), ct);
                    var text = new StringBuilder($"Device IP: {target.Ip}\nDevice Type: {(target.IsAp ? "AP" : "Switch")}\nProtocol: SSH\n");
                    var cdp = "";
                    foreach (var command in target.IsAp ? ApCommands : SwitchCommands)
                    {
                        ct.ThrowIfCancellationRequested();
                        var output = await session.ExecuteAsync(command, ct);
                        if (command == "show cdp neighbors detail")
                            cdp = output;
                        text.AppendLine("==============================\nCommand: " + command + "\n==============================");
                        text.AppendLine(output);
                    }
                    var filename = Regex.Replace(session.Hostname + "_" + target.Ip, @"[^A-Za-z0-9_.-]", "_") + ".txt";
                    var path = Path.Combine(folder, filename);
                    await AtomicFile.WriteAsync(path, text.ToString(), ct);
                    files.Add(path);
                    log?.Invoke(target.Ip + ": capture saved.");
                    if (options.Traverse && !target.IsAp)
                    foreach (var p in ParsePeers(cdp))
                    if ((!p.IsAp || options.IncludeAps) && InScope(p.Ip, options.SubnetFilter))
                        peers.Add(p);
                }
                catch (Exception ex) when (!token.IsCancellationRequested) { errors.Add(target.Ip + ": " + ex.GetType().Name + ": " + ex.Message); }
            });
            var next = new List<(string Ip, bool IsAp)>();
            foreach (var p in peers.OrderBy(p => p.Ip))
            {
                if (seen.Contains(p.Ip))
                    continue;
                if (seen.Count >= options.MaxDevices)
                {
                    errors.Add("Discovery device limit reached; additional peers skipped.");
                    break;
                }
                if (depth < options.MaxHops)
                {
                    seen.Add(p.Ip);
                    next.Add(p);
                }
            }
            layer = next;
        }
        return new(files.Order().ToList(), errors.ToList());
    }
    public static IEnumerable<(string Ip, bool IsAp)> ParsePeers(string text)
    {
        foreach (var block in Regex.Split(text, @"Device ID:", RegexOptions.IgnoreCase).Skip(1))
        {
            var ip = Regex.Match(block, @"(?:IP|IPv4) address:\s*((?:\d{1,3}\.){3}\d{1,3})", RegexOptions.IgnoreCase);
            if (!ip.Success || !IPAddress.TryParse(ip.Groups[1].Value, out _))
                continue;
            var isPhone = Regex.IsMatch(block, @"Host Phone|Cisco IP Phone|\bCP-\d|^\s*SEP[0-9A-F]{12}", RegexOptions.IgnoreCase);
            if (isPhone)
                continue;
            var isAp = Regex.IsMatch(block, @"WLAN Access Point|AIR-|C91\d", RegexOptions.IgnoreCase);
            if (!isAp && !Regex.IsMatch(block, @"Capabilities:[^\r\n]*\b(Switch|Router)\b", RegexOptions.IgnoreCase))
                continue;
            yield return (ip.Groups[1].Value, isAp);
        }
    }
    public static bool InScope(string address, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        filter = filter.Trim();
        if (!filter.Contains('/'))
        {
            if (filter.Any(c => !char.IsDigit(c) && c != '.'))
                throw new ArgumentException("Subnet filter must be IPv4 CIDR or numeric prefix.");
            return address.StartsWith(filter, StringComparison.Ordinal);
        }
        var parts = filter.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var network) || network.GetAddressBytes().Length != 4 || !int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32)
            throw new ArgumentException("Invalid IPv4 CIDR filter.");
        if (!IPAddress.TryParse(address, out var ip) || ip.GetAddressBytes().Length != 4)
            return false;
        var a = ip.GetAddressBytes();
        var b = network.GetAddressBytes();
        for (int i = 0; i < 4; i++)
        {
            var bits = Math.Clamp(prefix - i * 8, 0, 8);
            var mask = (byte)(255 << (8 - bits));
            if ((a[i] & mask) != (b[i] & mask))
                return false;
        }
        return true;
    }
}
