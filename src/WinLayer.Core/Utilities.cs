using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace WinLayer.Core;

public sealed record ActionEntry(string Id, string Label, string Category, string Keywords, string Icon,
    string? Page = null, string? Target = null, string? Tool = null)
{
    public string Caption => $"{Label}  ·  {Category}";
    public override string ToString() => Caption;
}

public static class ActionCatalog
{
    public static IReadOnlyList<ActionEntry> Entries { get; } = Load();
    public static ActionEntry? Find(string id) => Entries.FirstOrDefault(entry => entry.Id == id);

    private static ActionEntry[] Load()
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinLayer.actions.json")!;
        return JsonSerializer.Deserialize<ActionEntry[]>(source, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    public static string Fold(string input)
    {
        var text = new StringBuilder(input.Length);
        foreach (var letter in input.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letter) == UnicodeCategory.NonSpacingMark)
                continue;
            text.Append(char.IsLetterOrDigit(letter) ? char.ToLowerInvariant(letter) : ' ');
        }
        return text.ToString();
    }

    public static int Match(string query, string label, string keywords)
    {
        var title = Fold(label);
        var terms = (title + " " + Fold(keywords)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var score = 0;
        var hits = 0;
        foreach (var token in Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct())
        {
            var best = title.Contains(token, StringComparison.Ordinal) ? 8 : 0;
            foreach (var term in terms)
            {
                if (term == token) best = Math.Max(best, 12);
                else if (term.StartsWith(token, StringComparison.Ordinal)) best = Math.Max(best, 7);
                else if (token.Length >= 4 && Distance(token, term) <= (token.Length >= 7 ? 2 : 1)) best = Math.Max(best, 4);
            }
            if (best > 0) hits++;
            score += best;
        }
        return hits == 0 ? 0 : score + hits * 4;
    }

    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2)
            return int.MaxValue;
        Span<int> row = stackalloc int[b.Length + 1];
        for (var i = 0; i <= b.Length; i++) row[i] = i;
        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var previous = row[j];
                row[j] = Math.Min(Math.Min(row[j - 1], previous) + 1, diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = previous;
            }
        }
        return row[^1];
    }
}

public sealed record AppTheme(string Name, string Canvas, string Surface, string Ink, string Accent, string Border,
    int Radius = 15, int Density = 100, bool Motion = true, string Font = "Manrope")
{
    public bool IsValid() => Name is { Length: > 0 and <= 40 } && Radius is >= 4 and <= 30 && Density is >= 85 and <= 120 &&
        Font is "Manrope" or "Segoe UI" or "Geist Mono" &&
        new[] { Canvas, Surface, Ink, Accent, Border }.All(HubOptions.IsColor) &&
        Contrast(Ink, Canvas) >= 4.5 && Contrast(Ink, Surface) >= 4.5 &&
        Contrast(Accent, Canvas) >= 3 && Contrast(Accent, Surface) >= 3;

    private static double Contrast(string a, string b)
    {
        static double Light(string c)
        {
            static double Channel(string color, int index)
            {
                var n = byte.Parse(color.AsSpan(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
                return n <= 0.04045 ? n / 12.92 : Math.Pow((n + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c, 1) + 0.7152 * Channel(c, 3) + 0.0722 * Channel(c, 5);
        }
        var x = Light(a);
        var y = Light(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}

public sealed record CornerSetting(string Action = "", int Delay = 600)
{
    public bool IsValid() => Delay is >= 200 and <= 3000 && (Action is not null && (Action == "" || UtilityOptions.IsActionId(Action)));
}

public sealed record RadialPreset(string Name, string[] Actions, string Accent)
{
    public bool IsValid() => Name is { Length: > 0 and <= 40 } && UtilityOptions.IsRadialList(Actions) && HubOptions.IsColor(Accent);
}

public sealed record UtilityOptions
{
    public ClickShortcut Ocr { get; init; } = new(0x75, 3);
    public ClickShortcut Pin { get; init; } = new(0x76, 3);
    public ClickShortcut Shelf { get; init; } = new(0x77, 3);
    public ClickShortcut Palette { get; init; } = new(0x50, 6);
    public ClickShortcut Radial { get; init; } = new(0x52, 6);
    public string Language { get; init; } = "fr";
    public bool AutoCopy { get; init; } = true;
    public bool OcrHistory { get; init; }
    public bool PersistRecent { get; init; }
    public int ShelfPosition { get; init; } = 3;
    public int ShelfHours { get; init; } = 4;
    public int ShelfWidth { get; init; } = 400;
    public int ShelfHeight { get; init; } = 370;
    public bool Caps { get; init; }
    public bool Num { get; init; }
    public bool Scroll { get; init; }
    public int OsdSeconds { get; init; } = 2;
    public int OsdPosition { get; init; } = 1;
    public int OsdOpacity { get; init; } = 94;
    public int OsdSize { get; init; } = 100;
    public bool CompactOsd { get; init; }
    public bool HideOsdFullscreen { get; init; } = true;
    public bool ExcludeFullscreen { get; init; } = true;
    public string CornerExclusions { get; init; } = "";
    public int CornerPixels { get; init; } = 5;
    public int CornerCooldown { get; init; } = 2000;
    public bool CornerModifier { get; init; } = true;
    public CornerSetting[] Corners { get; init; } = [new(), new(), new(), new()];
    public string[] RadialItems { get; init; } = ["ocr", "screen-pin", "color", "shelf", "converter", "palette"];
    public string RadialAccent { get; init; } = "#9A86F1";
    public RadialPreset[] RadialPresets { get; init; } = [];
    public LocalLauncher[] Launchers { get; init; } = [];
    public string[] Favorites { get; init; } = [];
    public AppTheme[] Themes { get; init; } = [];
    public string Theme { get; init; } = "";

    public bool IsValid() => new[] { Ocr, Pin, Shelf, Palette, Radial }.All(static key => key.IsValid()) &&
        new[] { Ocr, Pin, Shelf, Palette, Radial }.Distinct().Count() == 5 &&
        Language is "fr" or "en" && ShelfPosition is >= 0 and <= 3 && ShelfHours is >= 1 and <= 72 &&
        ShelfWidth is >= 320 and <= 800 && ShelfHeight is >= 280 and <= 760 &&
        OsdSeconds is >= 1 and <= 8 && OsdPosition is >= 0 and <= 2 && OsdOpacity is >= 25 and <= 100 &&
        OsdSize is >= 75 and <= 150 && CornerExclusions is { Length: <= 512 } &&
        CornerPixels is >= 2 and <= 24 && CornerCooldown is >= 500 and <= 10_000 &&
        Corners is { Length: 4 } && Corners.All(static corner => corner?.IsValid() == true) &&
        IsRadialList(RadialItems) && HubOptions.IsColor(RadialAccent) &&
        RadialPresets is { Length: <= 8 } && RadialPresets.All(static preset => preset?.IsValid() == true) &&
        RadialPresets.Select(preset => preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == RadialPresets.Length &&
        Launchers is { Length: <= 12 } && Launchers.All(static item => item?.IsValid() == true) &&
        Launchers.Select(static item => item.Id).Distinct(StringComparer.Ordinal).Count() == Launchers.Length &&
        Favorites is { Length: <= 16 } && ValidIds(Favorites) &&
        Themes is { Length: <= 8 } && Themes.All(static theme => theme?.IsValid() == true) &&
        Themes.Select(static theme => theme.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Themes.Length &&
        Theme is { Length: <= 40 } && (Theme == "" || Themes.Any(theme => theme.Name == Theme));

    internal static bool IsRadialList(string[]? ids) => ids is { Length: >= 1 and <= 8 } && ValidIds(ids);

    private static bool ValidIds(string[] ids) => ids.All(IsActionId);

    internal static bool IsActionId(string? id) => id is { Length: > 0 and <= 100 } &&
        (ActionCatalog.Find(id) is not null || id.StartsWith("macro:", StringComparison.Ordinal) || LocalLauncher.IsId(id));
}

public enum UtilityAction { Execute, Save, Undo, Export, Import, AddFile, AddFolder }
public sealed record UtilityRequest(UtilityAction Action, string? Id = null, UtilityOptions? Options = null)
{
    public bool IsValid() => Action switch
    {
        UtilityAction.Execute => Options is null && UtilityOptions.IsActionId(Id),
        UtilityAction.Save => Id is null && Options?.IsValid() == true,
        UtilityAction.Undo or UtilityAction.Export or UtilityAction.Import or UtilityAction.AddFile or UtilityAction.AddFolder => Id is null && Options is null,
        _ => false
    };
}

public sealed record UtilityStatus(string? Undo, string? Error, string[] Recent);

public sealed record PreferenceUndo(string Label, UtilityOptions? Utilities = null, Appearance? Appearance = null, HubOptions? Taskbar = null)
{
    public bool IsValid() => Label is { Length: > 0 and <= 80 } &&
        (Utilities, Appearance, Taskbar) switch
        {
            ({ } options, null, null) => options.IsValid(),
            (null, { } appearance, null) => Enum.IsDefined(appearance),
            (null, null, { } taskbar) => taskbar.IsValid(),
            _ => false
        };
}

public sealed record LocalLauncher(string Id, string Label, string Path)
{
    public static bool IsId(string id) => id.StartsWith("local:", StringComparison.Ordinal) &&
        Guid.TryParseExact(id.AsSpan(6), "N", out _);

    public bool IsValid() => Id is not null && IsId(Id) && Label is { Length: > 0 and <= 64 } &&
        Path is { Length: >= 4 and <= 520 } && char.IsAsciiLetter(Path[0]) && Path[1] == ':' && Path[2] == '\\' &&
        Path.AsSpan(3).IndexOfAny(':', '\0') < 0;
}
