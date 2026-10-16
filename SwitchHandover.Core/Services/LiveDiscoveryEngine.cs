using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Common;
using SwitchHandover.Core.Models;

namespace SwitchHandover.Core.Services;

public class LiveDiscoveryEngine
{
    public delegate void LogAction(string category, string message);
    public delegate void ProgressAction(string ip, int percentage, string status);

    private static readonly Regex PromptRegex = new(@"(?:^|[\r\n])\s*[\w\.\-]+(?:\([^\)]+\))?[>#]\s*$", RegexOptions.Compiled);
    private static readonly Regex PasswordRegex = new(@"[Pp]assword:\s*$", RegexOptions.Compiled);
    private static readonly Regex MoreRegex = new(@"--\s*More\s*--", RegexOptions.Compiled);
    private static readonly Regex DeviceIdRegex = new(@"Device ID:\s*([^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlatformRegex = new(@"Platform:\s*([^,\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CapabilitiesRegex = new(@"Capabilities:\s*([^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CdpIpRegex = new(@"(?:Entry address\(es\):|Management address\(es\):)[\s\S]*?(?:IP|IPv4)\s*[Aa]ddress:\s*([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FallbackIpRegex = new(@"(?:IP|IPv4)\s*[Aa]ddress:\s*([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly string[] SwitchHandoverCommands =
    [
        "terminal length 0",
        "show version",
        "show inventory",
        "show idprom backplane",
        "show env power",
        "show environment",
        "show interfaces status",
        "show cdp neighbors detail",
        "show running-config"
    ];

    public static readonly string[] CiscoApCommands =
    [
        "terminal length 0",
        "show run",
        "show interfaces wired 0",
        "show inventory"
    ];

    public async Task<List<string>> RunLiveCaptureAsync(
        List<string> seedIps,
        LiveCredential credential,
        bool enableCdp,
        bool includeAps,
        string subnetFilter,
        int maxConcurrency,
        string outputDirectory,
        LogAction logCallback,
        ProgressAction progressCallback,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credential.Username))
        {
            throw new ArgumentException("SSH Username is required for Live Discovery.", nameof(credential));
        }

        Directory.CreateDirectory(outputDirectory);
        logCallback("Live", $"Starting Live Network Discovery on {seedIps.Count} seed device(s)...");

        var targetsToCollect = new ConcurrentDictionary<string, LiveDiscoveryTarget>(StringComparer.OrdinalIgnoreCase);
        var visitedDiscovery = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var ip in seedIps)
        {
            var cleanedIp = ip.Trim();
            if (!string.IsNullOrWhiteSpace(cleanedIp))
            {
                targetsToCollect.TryAdd(cleanedIp, new LiveDiscoveryTarget { IpAddress = cleanedIp, IsAp = false });
            }
        }

        if (enableCdp)
        {
            logCallback("CDP", "Initiating recursive CDP crawler (IP Phones and peripheral devices automatically excluded)...");
            var discoveryTasks = new List<Task>();
            foreach (var seed in targetsToCollect.Keys.ToList())
            {
                discoveryTasks.Add(PerformDiscoveryAsync(
                    seed,
                    credential,
                    subnetFilter,
                    includeAps,
                    targetsToCollect,
                    visitedDiscovery,
                    logCallback,
                    progressCallback,
                    cancellationToken));
            }

            await Task.WhenAll(discoveryTasks);
            logCallback("CDP", $"CDP Discovery complete. Discovered {targetsToCollect.Count} total valid network target(s).");
        }

        logCallback("Live", $"Starting parallel show command collection across {targetsToCollect.Count} device(s) (Concurrency: {maxConcurrency})...");

        var capturedFilePaths = new ConcurrentBag<string>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, maxConcurrency),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(targetsToCollect.Values, parallelOptions, async (target, ct) =>
        {
            var cmds = target.IsAp ? CiscoApCommands : SwitchHandoverCommands;
            string? filePath = await PerformCaptureAsync(
                target.IpAddress,
                credential,
                target.IsAp,
                outputDirectory,
                cmds,
                logCallback,
                progressCallback,
                ct);

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                capturedFilePaths.Add(filePath);
            }
        });

        logCallback("Live", $"Live collection complete! Successfully captured {capturedFilePaths.Count} device output file(s).");
        return capturedFilePaths.OrderBy(f => f).ToList();
    }

    public static async Task<bool> IsSshPortReachableAsync(string host, int port = 22, int timeoutMs = 2500, CancellationToken token = default)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeoutMs);

            await client.ConnectAsync(host, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private async Task PerformDiscoveryAsync(
        string ip,
        LiveCredential credential,
        string subnetFilter,
        bool includeAps,
        ConcurrentDictionary<string, LiveDiscoveryTarget> backupQueue,
        ConcurrentDictionary<string, bool> visited,
        LogAction log,
        ProgressAction progress,
        CancellationToken token)
    {
        if (visited.ContainsKey(ip)) return;
        visited.TryAdd(ip, true);

        progress(ip, 10, "Probing SSH (Port 22)...");
        bool reachable = await IsSshPortReachableAsync(ip, 22, 2500, token);
        if (!reachable)
        {
            progress(ip, 0, "Unreachable (Port 22 Closed/Timeout)");
            log("Error", $"Seed {ip} is unreachable on SSH port 22. Skipping CDP discovery.");
            return;
        }

        progress(ip, 25, "Connecting & discovering CDP neighbors...");

        SshClient? client = null;
        try
        {
            client = new SshClient(ip, credential.Username, credential.Password)
            {
                ConnectionInfo = { Timeout = TimeSpan.FromSeconds(7) }
            };
            await Task.Run(() => client.Connect(), token);
        }
        catch (Exception ex)
        {
            progress(ip, 0, $"Auth/Connection Failed: {ex.Message}");
            log("Auth", $"SSH connection to {ip} failed: {ex.Message}");
            client?.Dispose();
            return;
        }

        try
        {
            using (client)
            {
                using var stream = client.CreateShellStream("vt100", 255, 50, 800, 600, 2048);
                string initial = await ReadUntilPromptOrPatternAsync(stream, PromptRegex, 8000, token);

                await ElevatePrivilegesAsync(stream, initial, credential.Secret, token);
                await ExecuteRawCommandAsync(stream, "terminal length 0", token);

                string cdpOutput = await ExecuteRawCommandAsync(stream, "show cdp neighbors detail", token);
                client.Disconnect();

                progress(ip, 100, "Discovery complete");

                var neighbors = ParseCdpNeighbors(cdpOutput);
                var subTasks = new List<Task>();

                foreach (var neighbor in neighbors)
                {
                    if (string.IsNullOrWhiteSpace(neighbor.IpAddress)) continue;

                    // Automatically skip IP phones and non-managed endpoints
                    if (neighbor.IsPhone)
                    {
                        log("CDP", $"Found IP Phone: {neighbor.DeviceName} ({neighbor.IpAddress}) [{neighbor.Platform}] - Skipped.");
                        continue;
                    }

                    bool isAp = neighbor.IsAp;
                    if (isAp && !includeAps) continue;

                    bool validTarget = isAp || IsIpInSubnetFilter(neighbor.IpAddress, subnetFilter);

                    if (validTarget)
                    {
                        var target = new LiveDiscoveryTarget
                        {
                            IpAddress = neighbor.IpAddress,
                            Hostname = neighbor.DeviceName,
                            IsAp = isAp
                        };

                        if (backupQueue.TryAdd(neighbor.IpAddress, target))
                        {
                            log("CDP", $"Discovered {neighbor.DeviceName} ({neighbor.IpAddress}) [{(isAp ? "AP" : "Switch")}]");
                            progress(neighbor.IpAddress, 0, $"Discovered via CDP ({neighbor.DeviceName})");

                            if (!isAp)
                            {
                                subTasks.Add(PerformDiscoveryAsync(neighbor.IpAddress, credential, subnetFilter, includeAps, backupQueue, visited, log, progress, token));
                            }
                        }
                    }
                }

                await Task.WhenAll(subTasks);
            }
        }
        catch (Exception ex)
        {
            progress(ip, 0, "CDP Discovery Failed");
            log("Error", $"CDP Discovery failed on {ip}: {ex.Message}");
        }
    }

    private async Task<string?> PerformCaptureAsync(
        string ip,
        LiveCredential credential,
        bool isAp,
        string backupDir,
        string[] commands,
        LogAction log,
        ProgressAction progress,
        CancellationToken token)
    {
        progress(ip, 10, "Probing SSH (Port 22)...");

        bool reachable = await IsSshPortReachableAsync(ip, 22, 2500, token);
        if (!reachable)
        {
            progress(ip, 0, "Unreachable (Port 22)");
            log("Error", $"Device {ip} is unreachable on SSH port 22.");
            return null;
        }

        SshClient? client = null;
        try
        {
            progress(ip, 20, "Authenticating...");
            client = new SshClient(ip, credential.Username, credential.Password)
            {
                ConnectionInfo = { Timeout = TimeSpan.FromSeconds(8) }
            };
            await Task.Run(() => client.Connect(), token);
        }
        catch (Exception ex)
        {
            progress(ip, 0, $"Auth Failed: {ex.Message}");
            log("Error", $"SSH authentication failed on {ip}: {ex.Message}");
            client?.Dispose();
            return null;
        }

        try
        {
            using (client)
            {
                progress(ip, 30, "Session Connected");
                using var stream = client.CreateShellStream("vt100", 255, 50, 800, 600, 4096);

                string initial = await ReadUntilPromptOrPatternAsync(stream, PromptRegex, 10000, token);
                string prompt = await ElevatePrivilegesAsync(stream, initial, credential.Secret, token);

                string hostname = ip;
                var hostMatch = Regex.Match(prompt, @"(?:^|[\r\n])\s*([\w\.\-]+?)(?:\([^\)]+\))?[>#]");
                if (hostMatch.Success && !hostMatch.Groups[1].Value.Equals("Switch", StringComparison.OrdinalIgnoreCase) && !hostMatch.Groups[1].Value.Equals("Router", StringComparison.OrdinalIgnoreCase))
                {
                    hostname = hostMatch.Groups[1].Value.Trim();
                }

                progress(ip, 40, $"Ready ({hostname})");
                await ExecuteRawCommandAsync(stream, "terminal length 0", token);

                var sb = new StringBuilder();
                sb.AppendLine("==================================================");
                sb.AppendLine($"Capture Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Device IP   : {ip}");
                sb.AppendLine($"Hostname    : {hostname}");
                sb.AppendLine($"Device Type : {(isAp ? "Access Point" : "Cisco Switch")}");
                sb.AppendLine("==================================================\n");

                for (int i = 0; i < commands.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    string cmd = commands[i];

                    int pct = 40 + (int)(((double)(i + 1) / commands.Length) * 55);
                    progress(ip, pct, $"Running: {cmd}");

                    string output = await ExecuteRawCommandAsync(stream, cmd, token);

                    sb.AppendLine("==================================================");
                    sb.AppendLine($"Command: {cmd}");
                    sb.AppendLine("==================================================");
                    sb.AppendLine(output.TrimEnd());
                    sb.AppendLine();
                }

                client.Disconnect();

                string safeHostname = Regex.Replace(hostname, @"[^\w\.\-]", "_");
                string filename = Path.Combine(backupDir, $"{ip}_{safeHostname}_{(isAp ? "AP" : "Switch")}.txt");
                await File.WriteAllTextAsync(filename, sb.ToString(), token);

                progress(ip, 100, $"Completed ({commands.Length} cmds)");
                log("Capture", $"Successfully retrieved {hostname} ({ip}) -> {Path.GetFileName(filename)}");
                return filename;
            }
        }
        catch (Exception ex)
        {
            progress(ip, 0, $"Capture Failed: {ex.Message}");
            log("Error", $"Capture failed on {ip}: {ex.Message}");
            return null;
        }
    }

    private async Task<string> ElevatePrivilegesAsync(ShellStream stream, string initialOutput, string secret, CancellationToken token)
    {
        if (initialOutput.TrimEnd().EndsWith("#"))
        {
            return initialOutput;
        }

        if (!string.IsNullOrEmpty(secret))
        {
            FlushStream(stream);
            stream.WriteLine("enable");

            string response = await ReadUntilPromptOrPatternAsync(stream, new Regex(@"[Pp]assword:|[>#]\s*$", RegexOptions.Compiled), 8000, token);

            if (PasswordRegex.IsMatch(response))
            {
                FlushStream(stream);
                stream.WriteLine(secret);
                return await ReadUntilPromptOrPatternAsync(stream, PromptRegex, 8000, token);
            }
            return response;
        }

        return initialOutput;
    }

    private async Task<string> ExecuteRawCommandAsync(ShellStream stream, string command, CancellationToken token, int timeoutMs = 25000)
    {
        FlushStream(stream);
        stream.WriteLine(command);
        return await ReadUntilPromptOrPatternAsync(stream, PromptRegex, timeoutMs, token);
    }

    private void FlushStream(ShellStream stream)
    {
        while (stream.DataAvailable)
        {
            stream.Read();
        }
    }

    private async Task<string> ReadUntilPromptOrPatternAsync(ShellStream stream, Regex pattern, int timeoutMs, CancellationToken token)
    {
        var sb = new StringBuilder();
        var startTime = DateTime.UtcNow;

        while (!token.IsCancellationRequested && (DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            if (stream.DataAvailable)
            {
                string chunk = stream.Read();
                sb.Append(chunk);

                if (MoreRegex.IsMatch(chunk))
                {
                    stream.Write(" ");
                    continue;
                }

                int searchLen = Math.Min(sb.Length, 512);
                string tail = sb.ToString(sb.Length - searchLen, searchLen);

                if (pattern.IsMatch(tail))
                {
                    return sb.ToString();
                }
            }
            else
            {
                await Task.Delay(10, token);
            }
        }

        return sb.ToString();
    }

    private bool IsIpInSubnetFilter(string ipStr, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;

        filter = filter.Trim();
        if (filter.Contains('/'))
        {
            try
            {
                var parts = filter.Split('/');
                var targetIp = IPAddress.Parse(ipStr);
                var filterIp = IPAddress.Parse(parts[0]);
                int cidr = int.Parse(parts[1]);

                uint mask = cidr == 0 ? 0 : uint.MaxValue << (32 - cidr);
                mask = BitConverter.ToUInt32(BitConverter.GetBytes(mask), 0);

                byte[] targetBytes = targetIp.GetAddressBytes();
                byte[] filterBytes = filterIp.GetAddressBytes();

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(targetBytes);
                    Array.Reverse(filterBytes);
                }

                uint targetInt = BitConverter.ToUInt32(targetBytes, 0);
                uint filterInt = BitConverter.ToUInt32(filterBytes, 0);

                return (targetInt & mask) == (filterInt & mask);
            }
            catch
            {
                return ipStr.StartsWith(filter.Split('/')[0]);
            }
        }

        return ipStr.StartsWith(filter);
    }

    private List<CdpNeighbor> ParseCdpNeighbors(string cdpOutput)
    {
        var list = new List<CdpNeighbor>();
        var blocks = Regex.Split(cdpOutput, @"-{20,}");

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block)) continue;

            var n = new CdpNeighbor();
            var devMatch = DeviceIdRegex.Match(block);
            if (devMatch.Success) n.DeviceName = devMatch.Groups[1].Value.Trim().Split('.')[0];

            var ipMatch = CdpIpRegex.Match(block);
            if (ipMatch.Success)
            {
                n.IpAddress = ipMatch.Groups[1].Value.Trim();
            }
            else
            {
                var fallback = FallbackIpRegex.Match(block);
                if (fallback.Success) n.IpAddress = fallback.Groups[1].Value.Trim();
            }

            var platMatch = PlatformRegex.Match(block);
            if (platMatch.Success) n.Platform = platMatch.Groups[1].Value.Trim();

            var capMatch = CapabilitiesRegex.Match(block);
            if (capMatch.Success) n.Capabilities = capMatch.Groups[1].Value.Trim();

            if (!string.IsNullOrEmpty(n.IpAddress))
            {
                string dev = n.DeviceName;
                string plat = n.Platform;
                string cap = n.Capabilities;

                n.IsPhone = dev.StartsWith("SEP", StringComparison.OrdinalIgnoreCase) ||
                            dev.StartsWith("CP-", StringComparison.OrdinalIgnoreCase) ||
                            plat.Contains("IP Phone", StringComparison.OrdinalIgnoreCase) ||
                            plat.Contains("Phone", StringComparison.OrdinalIgnoreCase) ||
                            plat.Contains("ATA", StringComparison.OrdinalIgnoreCase) ||
                            cap.Contains("Phone", StringComparison.OrdinalIgnoreCase);

                n.IsAp = !n.IsPhone && (
                         cap.Contains("Access-Point", StringComparison.OrdinalIgnoreCase) ||
                         cap.Contains("Trans-Bridge", StringComparison.OrdinalIgnoreCase) ||
                         plat.StartsWith("AIR-", StringComparison.OrdinalIgnoreCase) ||
                         plat.StartsWith("C91", StringComparison.OrdinalIgnoreCase) ||
                         plat.Contains("AP", StringComparison.OrdinalIgnoreCase) ||
                         dev.StartsWith("AP", StringComparison.OrdinalIgnoreCase));

                list.Add(n);
            }
        }

        return list;
    }

    private class CdpNeighbor
    {
        public string DeviceName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string Capabilities { get; set; } = string.Empty;
        public bool IsAp { get; set; }
        public bool IsPhone { get; set; }
    }
}
