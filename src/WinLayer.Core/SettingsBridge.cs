using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinLayer.Core;

// Web messages use a separate contract. The V1 agent pipe and preference formats are unchanged.
public static class SettingsBridge
{
    private const int MaximumMessageLength = 4096;
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        AllowDuplicateProperties = false,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static bool TryRead(string json, out int id, out Request? request)
    {
        id = 0;
        request = null;
        if (json.Length > MaximumMessageLength)
            return false;

        Message? message;
        try
        {
            message = JsonSerializer.Deserialize<Message>(json, Format);
        }
        catch (JsonException)
        {
            return false;
        }

        if (message is not { Id: > 0 })
            return false;

        id = message.Id;
        if (message is { Action: "ready", Preferences: null, Version: null })
            return true;

        request = message switch
        {
            { Action: "status", Preferences: null, Version: null } => new(Command.Status),
            { Action: "refreshBattery", Preferences: null, Version: null } => new(Command.RefreshBattery),
            { Action: "checkUpdates", Preferences: null, Version: null } => new(Command.CheckUpdates),
            { Action: "save", Preferences: { } prefs, Version: null } when prefs.ToPreferences() is { } value =>
                new(Command.Save, Preferences: value),
            { Action: "installUpdate", Preferences: null, Version: { Length: > 0 and <= 128 } version }
                when Version.TryParse(version, out var parsed) && parsed is { Build: >= 0, Revision: -1 } =>
                new(Command.InstallUpdate, Version: version),
            _ => null
        };
        return request is not null;
    }

    public static string Write(int id, Response response) => JsonSerializer.Serialize(new { id, response }, Format);

    private sealed record Message(int Id, string Action, WebPreferences? Preferences, string? Version);

    private sealed record WebPreferences(Appearance? Appearance, bool? StartWithWindows,
        bool? AutomaticUpdateChecks, WebBattery? Battery)
    {
        public Preferences? ToPreferences()
        {
            if (Appearance is not { } appearance || !Enum.IsDefined(appearance) || StartWithWindows is not { } startup ||
                AutomaticUpdateChecks is not { } updates || Battery is not
                { Enabled: { } enabled, Alerts: { } alerts, RefreshMinutes: >= 2 and <= 60, LowThreshold: >= 5 and <= 50 } battery)
                return null;

            return new Preferences
            {
                Appearance = appearance,
                StartWithWindows = startup,
                AutomaticUpdateChecks = updates,
                Battery = new BatteryOptions
                {
                    Enabled = enabled,
                    Alerts = alerts,
                    RefreshMinutes = battery.RefreshMinutes!.Value,
                    LowThreshold = battery.LowThreshold!.Value
                }
            };
        }
    }

    private sealed record WebBattery(bool? Enabled, int? RefreshMinutes, bool? Alerts, int? LowThreshold);
}
