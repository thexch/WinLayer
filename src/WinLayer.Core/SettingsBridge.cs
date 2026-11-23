using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinLayer.Core;

// Web messages use a separate contract. The V1 agent pipe and preference formats are unchanged.
public static class SettingsBridge
{
    private const int MaximumMessageLength = 4096;
    private const int MaximumHubMessageLength = 32_768;
    private static readonly JsonSerializerOptions RequestFormat = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        AllowDuplicateProperties = false,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    // Responses wrap the persisted Hub options in an agent snapshot and a reply envelope.
    private static readonly JsonSerializerOptions ResponseFormat = new(RequestFormat) { MaxDepth = 16 };

    public static bool TryRead(string json, out int id, out Request? request)
    {
        id = 0;
        request = null;
        if (json.Length > MaximumHubMessageLength)
            return false;

        Message? message;
        try
        {
            message = JsonSerializer.Deserialize<Message>(json, RequestFormat);
        }
        catch (JsonException)
        {
            return false;
        }

        if (message is not { Id: > 0 })
            return false;

        id = message.Id;
        if (message.Action == "utility")
        {
            if (message is not { Preferences: null, Version: null, ClickerOptions: null, Hub: null, View: null, Utility: { } utility } ||
                !utility.IsValid())
                return false;
            request = new(Command.Utility, Utility: utility);
            return true;
        }
        if (message.Utility is not null)
            return false;
        if (message.Action == "hub")
        {
            if (message is not { Preferences: null, Version: null, ClickerOptions: null, View: null, Hub: { } hub } || !hub.IsValid())
                return false;
            request = new(Command.Hub, Hub: hub);
            return true;
        }
        if (json.Length > MaximumMessageLength || message.Hub is not null || message.View is not null && message.Action != "status")
            return false;

        if (message.Action == "saveAutoClicker")
        {
            if (message is not { Preferences: null, Version: null, ClickerOptions: { } clicker } || !clicker.IsValid())
                return false;
            request = new(Command.SaveAutoClicker, ClickerOptions: clicker);
            return true;
        }

        if (message.ClickerOptions is not null)
            return false;
        if (message is { Action: "ready", Preferences: null, Version: null })
            return true;

        request = message switch
        {
            { Action: "status", Preferences: null, Version: null } => new(Command.Status, message.View ?? Page.Dashboard),
            { Action: "pauseAutoClicker", Preferences: null, Version: null } => new(Command.PauseAutoClicker),
            { Action: "rearmAutoClicker", Preferences: null, Version: null } => new(Command.RearmAutoClicker),
            { Action: "refreshBattery", Preferences: null, Version: null } => new(Command.RefreshBattery),
            { Action: "checkUpdates", Preferences: null, Version: null } => new(Command.CheckUpdates),
            { Action: "save", Preferences: { } prefs, Version: null } when prefs.ToPreferences() is { } value =>
                new(Command.Save, Preferences: value),
            { Action: "installUpdate", Preferences: null, Version: { Length: > 0 and <= 128 } version }
                when Version.TryParse(version, out var parsed) && parsed is { Build: >= 0, Revision: -1 } =>
                new(Command.InstallUpdate, Version: version, ReportProgress: true),
            _ => null
        };
        return request is not null;
    }

    public static string Write(int id, Response response) => JsonSerializer.Serialize(new { id, response }, ResponseFormat);

    public static string WriteMacro(MacroPreset macro) => JsonSerializer.Serialize(macro, RequestFormat);

    private sealed record Message(int Id, string Action, WebPreferences? Preferences, string? Version, AutoClickerOptions? ClickerOptions, HubRequest? Hub, Page? View, UtilityRequest? Utility);

    private sealed record WebPreferences(Appearance? Appearance, bool? StartWithWindows,
        bool? AutomaticUpdateChecks, WebBattery? Battery, bool? BatteryTrayIcons)
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
                BatteryTrayIcons = BatteryTrayIcons ?? true,
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
