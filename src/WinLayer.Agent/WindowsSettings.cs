using Microsoft.Win32;
using System.Diagnostics;

namespace WinLayer.Agent;

internal static class WindowsSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly Lock LogLock = new();

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinLayerData");

    public static void SetAutoStart(bool enabled, bool removeOwnEntryOnly = false)
    {
        var command = $"\"{Environment.ProcessPath}\" --background";
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        var existing = key?.GetValue("WinLayer") as string;
        if (removeOwnEntryOnly && !string.Equals(existing, command, StringComparison.OrdinalIgnoreCase))
            return;

        if (enabled && existing != command)
        {
            using var writableKey = key is null ? Registry.CurrentUser.CreateSubKey(RunKey) : null;
            (key ?? writableKey!).SetValue("WinLayer", command);
        }
        else if (!enabled && existing is not null)
        {
            key!.DeleteValue("WinLayer", throwOnMissingValue: false);
        }
    }

    public static void Log(string message, Exception? error = null)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(DataDirectory);
                var path = Path.Combine(DataDirectory, "agent.log");
                if (File.Exists(path) && new FileInfo(path).Length >= 128 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message} {error}{Environment.NewLine}");
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(failure);
        }
    }
}
