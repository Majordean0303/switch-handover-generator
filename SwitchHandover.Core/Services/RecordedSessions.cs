using System.Text.Json;
namespace SwitchHandover.Core.Services;

public sealed record RecordedDevice(string Hostname, Dictionary<string, string> Outputs);
public sealed record RecordedCapture(string Seed, string Target, string Type, Dictionary<string, RecordedDevice> Devices)
{
    public static async Task<RecordedCapture> ReadAsync(string path, CancellationToken token = default) => JsonSerializer.Deserialize<RecordedCapture>(await File.ReadAllTextAsync(path, token), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Recorded capture is empty.");
}
public sealed class RecordedSessionFactory(RecordedCapture capture) : ICommandSessionFactory
{
    public Task<ICommandSession> ConnectAsync(string host, DeviceCredentials credentials, ConnectionSettings settings, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!capture.Devices.TryGetValue(host, out var device))
            throw new IOException("No recorded capture for device " + host);
        return Task.FromResult<ICommandSession>(new Session(device));
    }
    private sealed class Session(RecordedDevice device) : ICommandSession
    {
        public string Hostname => device.Hostname; public string Protocol => "Recorded";
        public Task<string> ExecuteAsync(string command, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(device.Outputs.TryGetValue(command, out var output) ? output : throw new InvalidDataException("Command not present in recorded capture: " + command));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
