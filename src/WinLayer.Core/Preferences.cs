using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinLayer.Core;

public enum Appearance { System, Light, Dark }

public sealed record BatteryOptions
{
    public bool Enabled { get; init; }
    public int RefreshMinutes { get; init; } = 5;
    public bool Alerts { get; init; } = true;
    public int LowThreshold { get; init; } = 20;

    public BatteryOptions Normalize() => this with
    {
        RefreshMinutes = Math.Clamp(RefreshMinutes, 2, 60),
        LowThreshold = Math.Clamp(LowThreshold, 5, 50)
    };
}

public sealed record Preferences
{
    public Appearance Appearance { get; init; }
    public bool StartWithWindows { get; init; }
    public bool AutomaticUpdateChecks { get; init; } = true;
    public bool BatteryTrayIcons { get; init; } = true;
    public BatteryOptions Battery { get; init; } = new();

    public Preferences Normalize() => this with
    {
        Appearance = Enum.IsDefined(Appearance) ? Appearance : Appearance.System,
        Battery = (Battery ?? new()).Normalize()
    };
}

public sealed class PreferencesStore(string path)
{
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<Appearance>() }
    };

    public async Task<Preferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var preferences = await JsonSerializer.DeserializeAsync<Preferences>(stream, Format, cancellationToken);
            return (preferences ?? throw new JsonException("Empty preferences.")).Normalize();
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new();
        }
    }

    // The agent is the sole writer. A sibling temporary file keeps replacement on the same volume.
    public async Task SaveAsync(Preferences preferences, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, preferences.Normalize(), Format, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
