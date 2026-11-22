using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;

namespace WinLayer.Core;

public enum Page { Dashboard, Battery, General, Updates }
public enum Command { Status, OpenSettings, Save, RefreshBattery, CheckUpdates, InstallUpdate, Quit }
public enum BatteryState { Available, Unknown, Disconnected, Unsupported }
public enum UpdateState { Unavailable, NotChecked, Current, Available, Error, Installing }

public sealed record BatteryDevice(string Id, string Name, BatteryState State, int? Percentage = null);
public sealed record BatteryStatus(IReadOnlyList<BatteryDevice> Devices, DateTimeOffset? UpdatedAt = null, string? Error = null);
public sealed record UpdateStatus(UpdateState State, string? Version = null, string? Info = null);
public sealed record AgentStatus(Preferences Preferences, BatteryStatus Battery, UpdateStatus Updates, string Version, string? Error, Page RequestedPage, long NavigationRevision);

public sealed record Request(Command Command, Page Page = Page.Dashboard, Preferences? Preferences = null, string? Version = null);
public sealed record Response(AgentStatus? Status = null, string? Error = null);

public static class LocalProtocol
{
    private const int MaximumMessageBytes = 1_048_576;
    public static string PipeName { get; } = $"WinLayer.v1.{Environment.UserName}.{CurrentSession()}";

    public static async Task<Response> SendAsync(Request request, CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(2500, cancellationToken);
        await WriteAsync(pipe, request, cancellationToken);
        return await ReadAsync<Response>(pipe, cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaximumMessageBytes)
            throw new InvalidDataException("Invalid WinLayer message size.");

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload) ?? throw new InvalidDataException("Empty WinLayer message.");
    }

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        if (payload.Length > MaximumMessageBytes)
            throw new InvalidDataException("WinLayer message is too large.");

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static int CurrentSession()
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.SessionId;
    }
}
