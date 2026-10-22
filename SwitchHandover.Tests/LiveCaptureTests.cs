using SwitchHandover.Core.Services;
namespace SwitchHandover.Tests;

public class LiveCaptureTests
{
    sealed class Fake : ICommandSessionFactory
    {
        public int Active, Peak; public List<string> Hosts = []; public Dictionary<string, string> Neighbors = [];
        public async Task<ICommandSession> ConnectAsync(string host, DeviceCredentials c, ConnectionSettings settings, CancellationToken token = default)
        {
            Assert.False(settings.AllowTelnetFallback);
            lock (Hosts)
                Hosts.Add(host);
            var count = Interlocked.Increment(ref Active);
            Peak = Math.Max(Peak, count);
            await Task.Delay(10, token);
            return new Session(this, host);
        }
        sealed class Session(Fake parent, string host) : ICommandSession
        {
            public string Hostname => "SW-" + host.Replace('.', '-'); public string Protocol => "Fake"; public Task<string> ExecuteAsync(string c, CancellationToken token = default)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(c == "show cdp neighbors detail" ? parent.Neighbors.GetValueOrDefault(host, "") : c == "show running-config" ? "hostname " + Hostname : "recorded output");
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Decrement(ref parent.Active);
                return ValueTask.CompletedTask;
            }
        }
    }
    static DeviceCredentials Credentials => new() { Username = "fixture", Password = "fixture" };
    static string Peer(string ip, string kind = "Switch") => $"Device ID: SW-{ip}\nIP address: {ip}\nCapabilities: {kind}\nInterface: Gi1/0/1, Port ID (outgoing port): Gi1/0/1\n";
    [Fact]
    public async Task Traversal_respects_loops_device_cap_and_concurrency()
    {
        var d = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var fake = new Fake();
            fake.Neighbors["192.0.2.1"] = Peer("192.0.2.2") + Peer("192.0.2.3") + Peer("192.0.2.4");
            fake.Neighbors["192.0.2.2"] = Peer("192.0.2.1");
            var result = await new BoundedLiveCapture(fake).RunAsync(["192.0.2.1"], Credentials, d, new(Workers: 2, MaxDevices: 3));
            Assert.Equal(3, result.Files.Count);
            Assert.Equal(3, fake.Hosts.Distinct().Count());
            Assert.InRange(fake.Peak, 1, 2);
            Assert.Contains(result.Errors, e => e.Contains("limit"));
            Assert.All(result.Files, p => Assert.Contains("Command: show running-config", File.ReadAllText(p)));
        }
        finally { if (Directory.Exists(d)) Directory.Delete(d, true); }
    }
    [Fact]
    public async Task Hop_zero_collects_seeds_only()
    {
        var d = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var f = new Fake();
            f.Neighbors["192.0.2.1"] = Peer("192.0.2.2");
            var r = await new BoundedLiveCapture(f).RunAsync(["192.0.2.1"], Credentials, d, new(MaxHops: 0));
            Assert.Single(r.Files);
            Assert.Single(f.Hosts);
        }
        finally { if (Directory.Exists(d)) Directory.Delete(d, true); }
    }
    [Fact]
    public void Phones_and_unsupported_endpoints_are_not_crawled()
    {
        var peers = BoundedLiveCapture.ParsePeers(Peer("192.0.2.2", "Host Phone") + Peer("192.0.2.3", "Host") + Peer("192.0.2.4", "Router")).ToArray();
        Assert.Equal("192.0.2.4", Assert.Single(peers).Ip);
    }
    [Fact]
    public async Task Cancellation_stops_before_connection()
    {
        var f = new Fake();
        using var c = new CancellationTokenSource();
        c.Cancel();
        var d = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BoundedLiveCapture(f).RunAsync(["192.0.2.1"], Credentials, d, token: c.Token));
            Assert.Empty(f.Hosts);
        }
        finally { if (Directory.Exists(d)) Directory.Delete(d, true); }
    }
}
