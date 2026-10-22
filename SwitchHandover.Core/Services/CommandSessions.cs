using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;
namespace SwitchHandover.Core.Services;

public sealed class DeviceCredentials
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string Secret { get; init; } = "";
    public override string ToString() => "Device credentials (redacted)";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
            throw new ArgumentException("Username and password are required.");
        if (new[] { Username, Password, Secret }.Any(s => s.Contains('\r') || s.Contains('\n')))
            throw new ArgumentException("Credentials must be single-line values.");
    }
}
public sealed record ConnectionSettings(bool AllowTelnetFallback = true, int SshPort = 22, int TelnetPort = 23, TimeSpan? CommandTimeout = null);
public interface ICommandSession : IAsyncDisposable
{
    string Hostname
    {
        get;
    }
    string Protocol
    {
        get;
    }
    Task<string> ExecuteAsync(string command, CancellationToken token = default);
}
public interface ICommandSessionFactory
{
    Task<ICommandSession> ConnectAsync(string host, DeviceCredentials credentials, ConnectionSettings settings, CancellationToken token = default);
}
public sealed class SshTelnetSessionFactory : ICommandSessionFactory
{
    public async Task<ICommandSession> ConnectAsync(string host, DeviceCredentials credentials, ConnectionSettings settings, CancellationToken token = default)
    {
        credentials.Validate();
        token.ThrowIfCancellationRequested();
        Exception? sshFailure = null;
        try
        {
            var client = new SshClient(host, settings.SshPort, credentials.Username, credentials.Password);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
            try
            {
                await client.ConnectAsync(token);
                var terminal = new SshTerminal(client, client.CreateShellStream("vt100", 255, 50, 800, 600, 4096));
                var session = new InteractiveSession(terminal, "SSH", settings.CommandTimeout ?? TimeSpan.FromSeconds(25));
                try
                {
                    await session.InitializeAsync(credentials, token);
                    return session;
                }
                catch { await session.DisposeAsync(); throw; }
            }
            catch { client.Dispose(); throw; }
        }
        catch (Exception ex) when (!token.IsCancellationRequested && settings.AllowTelnetFallback) { sshFailure = ex; }
        var tcp = new TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await tcp.ConnectAsync(host, settings.TelnetPort, timeout.Token);
            var session = new InteractiveSession(new TelnetTerminal(tcp), "Telnet", settings.CommandTimeout ?? TimeSpan.FromSeconds(25));
            try
            {
                await session.InitializeAsync(credentials, token);
                return session;
            }
            catch { await session.DisposeAsync(); throw; }
        }
        catch (Exception ex) when (!token.IsCancellationRequested) { tcp.Dispose(); throw new IOException($"Connection to {host} failed via SSH ({sshFailure?.GetType().Name}) and Telnet ({ex.GetType().Name}).", ex); }
        catch { tcp.Dispose(); throw; }
    }
    private interface ITerminal : IAsyncDisposable
    {
        bool DataAvailable
        {
            get;
        }
        Task<string> ReadAsync(CancellationToken token); Task WriteAsync(string text, CancellationToken token);
    }
    private sealed class SshTerminal(SshClient client, ShellStream stream) : ITerminal
    {
        public bool DataAvailable => stream.DataAvailable;
        public Task<string> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(stream.Read());
        }
        public Task WriteAsync(string text, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            stream.Write(text);
            stream.Flush();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
    private sealed class TelnetTerminal(TcpClient client) : ITerminal
    {
        private readonly TelnetDecoder _decoder = new();
        public bool DataAvailable => client.Available > 0;
        public async Task<string> ReadAsync(CancellationToken token)
        {
            var buffer = new byte[4096];
            int count = await client.GetStream().ReadAsync(buffer, token);
            if (count == 0)
                throw new IOException("Telnet connection closed.");
            var result = _decoder.Decode(buffer.AsSpan(0, count));
            if (result.Replies.Length > 0)
                await client.GetStream().WriteAsync(result.Replies, token);
            return result.Text;
        }
        public async Task WriteAsync(string text, CancellationToken token) => await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes(text), token);
        public ValueTask DisposeAsync()
        {
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
    private sealed class InteractiveSession(ITerminal terminal, string protocol, TimeSpan timeout) : ICommandSession
    {
        private static readonly Regex Prompt = new(@"(?:^|[\r\n])\s*[\w.\-]+(?:\([^)]*\))?[>#]\s*$", RegexOptions.Compiled);
        private static readonly Regex Login = new(@"(?:[Uu]sername|[Ll]ogin|[Pp]assword):\s*$|(?:^|[\r\n])\s*[\w.\-]+(?:\([^)]*\))?[>#]\s*$", RegexOptions.Compiled);
        public string Hostname { get; private set; } = "";
        public string Protocol => protocol;
        public async Task InitializeAsync(DeviceCredentials credentials, CancellationToken token)
        {
            var response = await ReadUntilAsync(Login, token);
            if (Regex.IsMatch(response, @"(?:[Uu]sername|[Ll]ogin):\s*$"))
            {
                await terminal.WriteAsync(credentials.Username + "\r\n", token);
                response = await ReadUntilAsync(Login, token);
            }
            if (Regex.IsMatch(response, @"[Pp]assword:\s*$"))
            {
                await terminal.WriteAsync(credentials.Password + "\r\n", token);
                response = await ReadUntilAsync(Login, token);
            }
            if (!Prompt.IsMatch(response))
                throw new UnauthorizedAccessException("Device login did not reach an EXEC prompt.");
            if (!response.TrimEnd().EndsWith('#'))
            {
                await terminal.WriteAsync("enable\r\n", token);
                response = await ReadUntilAsync(Login, token);
                if (Regex.IsMatch(response, @"[Pp]assword:\s*$"))
                {
                    await terminal.WriteAsync((credentials.Secret.Length > 0 ? credentials.Secret : credentials.Password) + "\r\n", token);
                    response = await ReadUntilAsync(Login, token);
                }
                if (!response.TrimEnd().EndsWith('#'))
                    throw new UnauthorizedAccessException("Privileged EXEC access was not obtained.");
            }
            var prompt = Prompt.Match(response).Value.Trim();
            Hostname = Regex.Replace(prompt, @"(?:\([^)]*\))?[>#]\s*$", "").Trim();
            await ExecuteAsync("terminal length 0", token);
        }
        public async Task<string> ExecuteAsync(string command, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (command.Contains('\r') || command.Contains('\n'))
                throw new ArgumentException("Commands must be single-line values.");
            while (terminal.DataAvailable)
            {
                token.ThrowIfCancellationRequested();
                await terminal.ReadAsync(token);
            }
            await terminal.WriteAsync(command + "\r\n", token);
            return await ReadUntilAsync(Prompt, token);
        }
        private async Task<string> ReadUntilAsync(Regex pattern, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();
            var text = new StringBuilder();
            while (watch.Elapsed < timeout)
            {
                token.ThrowIfCancellationRequested();
                if (terminal.DataAvailable)
                {
                    var chunk = await terminal.ReadAsync(token);
                    text.Append(chunk);
                    if (text.Length > 16 * 1024 * 1024)
                        throw new InvalidDataException("Command output exceeds 16 MiB.");
                    if (Regex.IsMatch(chunk, @"--\s*More\s*--", RegexOptions.IgnoreCase))
                        await terminal.WriteAsync(" ", token);
                    var tail = text.ToString(Math.Max(0, text.Length - 1024), Math.Min(1024, text.Length));
                    if (pattern.IsMatch(tail))
                        return text.ToString();
                }
                else
                    await Task.Delay(20, token);
            }
            throw new TimeoutException("No device prompt received before timeout.");
        }
        public ValueTask DisposeAsync() => terminal.DisposeAsync();
    }
}
public sealed class TelnetDecoder
{
    private int _state; private byte _optionCommand;
    public (string Text, byte[] Replies) Decode(ReadOnlySpan<byte> input)
    {
        var output = new List<byte>();
        var replies = new List<byte>();
        foreach (var b in input)
        {
            switch (_state)
            {
                case 0:
                    if (b == 255)
                        _state = 1;
                    else
                        output.Add(b);
                    break;
                case 1:
                    if (b == 255)
                    {
                        output.Add(255);
                        _state = 0;
                    }
                    else if (b is 251 or 252 or 253 or 254)
                    {
                        _optionCommand = b;
                        _state = 2;
                    }
                    else if (b == 250)
                        _state = 3;
                    else
                        _state = 0;
                    break;
                case 2:
                    if (_optionCommand is 251 or 253)
                        replies.AddRange([255, (byte)(_optionCommand == 251 ? 254 : 252), b]);
                    _state = 0;
                    break;
                case 3:
                    if (b == 255)
                        _state = 4;
                    break;
                case 4:
                    _state = b == 240 ? 0 : 3;
                    break;
            }
        }
        return (Encoding.UTF8.GetString(output.ToArray()), replies.ToArray());
    }
}
