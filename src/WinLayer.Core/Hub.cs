namespace WinLayer.Core;

public enum HubTool { Taskbar, Switcher, Macro, Repeater, Picker, Ruler, Network, System, Pin }
public enum HubAction { Configure, Open, Stop, Rearm, SaveMacro, DeleteMacro, Play, PauseRecording, Color, PinAllOff, Processes, EndProcess, CopyColor, ExportMacro }
public enum MacroKind { Wait, Move, Left, Right, Middle, Key }
public enum SwitcherLayout { Grid, List, Cards }
public enum RulerMode { Free, Horizontal, Vertical }
public enum ColorFormat { Hex, Rgb, Hsl }
public enum RateUnit { Kilobytes, Megabytes, Megabits }
public enum TrayMetric { None, Cpu, Ram, Network, Gpu }

public sealed record HubOptions
{
    public bool Enabled { get; init; }
    public ClickShortcut Shortcut { get; init; }
    public int Seconds { get; init; } = 2;
    public int History { get; init; } = 60;
    public string Adapter { get; init; } = "";
    public string Exclusions { get; init; } = "";
    public bool Indicator { get; init; } = true;
    public string Color { get; init; } = "#7669DD";
    public int Opacity { get; init; } = 75;
    public int Size { get; init; } = 4;
    public int Zoom { get; init; } = 12;
    public int Radius { get; init; }
    public int Inset { get; init; }
    public int Threshold { get; init; } = 90;
    public bool Alerts { get; init; }
    public int Width { get; init; } = 700;
    public SwitcherLayout Layout { get; init; }
    public RulerMode RulerMode { get; init; }
    public ColorFormat ColorFormat { get; init; }
    public RateUnit RateUnit { get; init; }
    public TrayMetric[] TrayMetrics { get; init; } = [];
    public bool AutoCopy { get; init; } = true;
    public AutoClickerOptions Repeat { get; init; } = new();
    public int OutputKey { get; init; } = 0x20;
    public int OutputModifiers { get; init; }
    public int PlayCount { get; init; } = 1;
    public int SpeedPercent { get; init; } = 100;
    public bool Loop { get; init; }
    public string SelectedMacro { get; init; } = "";
    public string PinRules { get; init; } = "";
    public string Profile { get; init; } = "";
    public TaskbarStyle[] Styles { get; init; } = [];
    public ColorPalette[] Palettes { get; init; } = [];

    public bool IsValid() => Shortcut.IsValid() && Seconds is >= 1 and <= 60 && History is >= 15 and <= 180 &&
        Adapter is { Length: <= 160 } && Exclusions is { Length: <= 512 } && IsColor(Color) &&
        Opacity is >= 5 and <= 100 && Size is >= 1 and <= 24 && Zoom is >= 4 and <= 24 &&
        Radius is >= 0 and <= 16 && Inset is >= 0 and <= 80 &&
        Threshold is >= 1 and <= 100 && Width is >= 420 and <= 1000 && Enum.IsDefined(Layout) &&
        Enum.IsDefined(RulerMode) && Enum.IsDefined(ColorFormat) && Enum.IsDefined(RateUnit) && TrayMetrics is { Length: <= 3 } &&
        TrayMetrics.All(static metric => metric is TrayMetric.Cpu or TrayMetric.Ram or TrayMetric.Gpu) &&
        TrayMetrics.Distinct().Count() == TrayMetrics.Length &&
        Repeat is not null && Repeat.IsValid() && KeyboardKey(OutputKey) && (OutputModifiers & ~7) == 0 &&
        PlayCount is >= 1 and <= 100 && SpeedPercent is >= 25 and <= 400 && SelectedMacro is { Length: <= 60 } &&
        PinRules is { Length: <= 512 } && Profile is { Length: <= 40 } && Styles is { Length: <= 8 } &&
        Styles.All(static style => style is not null && style.IsValid()) &&
        Styles.Select(static style => style.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Styles.Length &&
        Palettes is { Length: <= 8 } && Palettes.All(static palette => palette is not null && palette.IsValid()) &&
        Palettes.Select(static palette => palette.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Palettes.Length;
    public static bool IsColor(string? color) => color is { Length: 7 } && color[0] == '#' &&
        color.AsSpan(1).IndexOfAnyExcept("0123456789ABCDEFabcdef") == -1;
    public static bool KeyboardKey(int key) => key is 0x08 or 0x09 or 0x0D or >= 0x20 and <= 0x28 or 0x2E or
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87;
    public static HubOptions Default(HubTool tool) => new()
    {
        Shortcut = new(0x70 + (int)tool, 2 | 4),
        Indicator = tool != HubTool.Network,
        Repeat = new()
        {
            Shortcut = new(0x78),
            Emergency = tool == HubTool.Macro ? new(0x7A, 2 | 4) : new(0x79, 2 | 1)
        }
    };
}

public sealed record ColorPalette(string Name, string[] Colors)
{
    public bool IsValid() => Name is { Length: > 0 and <= 40 } && Colors is { Length: > 0 and <= 24 } &&
        Colors.All(HubOptions.IsColor);
}

public sealed record TaskbarStyle(string Name, string Color, int Opacity, int Size, int Radius = 0, int Inset = 0)
{
    public bool IsValid() => Name is { Length: > 0 and <= 40 } && HubOptions.IsColor(Color) &&
        Opacity is >= 5 and <= 100 && Size is >= 1 and <= 24 && Radius is >= 0 and <= 16 && Inset is >= 0 and <= 80;
}

public sealed record MacroStep(MacroKind Kind, int Delay, int X = 0, int Y = 0, int Key = 0, int Modifiers = 0)
{
    public bool IsValid() => Enum.IsDefined(Kind) && Delay is >= 0 and <= 60_000 &&
        X is >= 0 and <= 10_000 && Y is >= 0 and <= 10_000 && (Modifiers & ~7) == 0 &&
        (Kind != MacroKind.Key || HubOptions.KeyboardKey(Key));
}

public sealed record MacroPreset(string Name, MacroStep[] Steps)
{
    public bool IsValid() => Name is { Length: > 0 and <= 60 } && !string.IsNullOrWhiteSpace(Name) &&
        Steps is { Length: > 0 and <= 240 } && Steps.All(static step => step is not null && step.IsValid());
}

public sealed record HubPreferences
{
    public Dictionary<HubTool, HubOptions> Tools { get; init; } = [];
    public MacroPreset[] Macros { get; init; } = [];
    public string[] Colors { get; init; } = [];
    public string[] Favorites { get; init; } = [];

    public HubOptions Get(HubTool tool) => Tools.GetValueOrDefault(tool) ?? HubOptions.Default(tool);
    public HubPreferences Normalize() => this with
    {
        Tools = (Tools ?? []).Where(static pair => Enum.IsDefined(pair.Key) && pair.Value is not null && pair.Value.IsValid())
            .ToDictionary(),
        Macros = (Macros ?? []).Where(static value => value is not null && value.IsValid())
            .DistinctBy(static value => value.Name, StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
        Colors = (Colors ?? []).Where(HubOptions.IsColor).Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray(),
        Favorites = (Favorites ?? []).Where(HubOptions.IsColor).Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray()
    };
}

public sealed record HubRequest(HubTool Tool, HubAction Action, HubOptions? Options = null, MacroPreset? Macro = null,
    string? Value = null, int Pid = 0, long Started = 0)
{
    public bool IsValid() => Enum.IsDefined(Tool) && Enum.IsDefined(Action) && Action switch
    {
        HubAction.Configure => Options?.IsValid() == true && Macro is null && Value is null && Pid == 0 && Started == 0,
        HubAction.SaveMacro or HubAction.ExportMacro => Tool == HubTool.Macro && Options is null && Macro?.IsValid() == true && Value is null && Pid == 0 && Started == 0,
        HubAction.DeleteMacro or HubAction.Play => Tool == HubTool.Macro && Options is null && Macro is null && Value is { Length: > 0 and <= 60 } && Pid == 0 && Started == 0,
        HubAction.Color or HubAction.CopyColor => Tool == HubTool.Picker && Options is null && Macro is null && HubOptions.IsColor(Value) && Pid == 0 && Started == 0,
        HubAction.EndProcess => Tool == HubTool.System && Options is null && Macro is null && Value is null && Pid > 4 && Started > 0,
        _ => Options is null && Macro is null && Value is null && Pid == 0 && Started == 0 && Action switch
        {
            HubAction.Open => Tool is HubTool.Switcher or HubTool.Picker or HubTool.Ruler or HubTool.Macro or HubTool.Pin,
            HubAction.Stop => Tool is HubTool.Macro or HubTool.Repeater or HubTool.Picker or HubTool.Ruler or HubTool.Switcher,
            HubAction.Rearm => Tool == HubTool.Repeater,
            HubAction.PauseRecording => Tool == HubTool.Macro,
            HubAction.PinAllOff => Tool == HubTool.Pin,
            HubAction.Processes => Tool == HubTool.System,
            _ => false
        }
    };
}

public sealed record HubWindow(long Handle, string Title, string App, int Pid);
public sealed record NetworkAdapter(string Id, string Name, string Kind);
public sealed record HubSample(DateTimeOffset At, double? Cpu, double? Ram, double? Down, double? Up, double? Gpu = null);
public sealed record HubDisk(string Name, long Size, long Free);
public sealed record HubProcess(int Pid, string Name, long Started, long? Ram, double? Cpu, string? Path, string? Icon = null);
public sealed record HubMetrics(NetworkAdapter[] Adapters, string? ActiveAdapter, long Received, long Sent,
    HubSample[] History, long? TotalRam, long? FreeRam, string? Processor, int Threads,
    HubDisk[] Disks, HubProcess[] Processes, string? GpuName = null, long? GpuMemory = null, long? GpuUsed = null,
    int? Cores = null, int? CpuMHz = null, double?[]? LogicalCpu = null, double? DiskRead = null, double? DiskWrite = null);
public sealed record HubStatus(Dictionary<HubTool, string> Errors, HubMetrics? Metrics, HubWindow[] Pinned,
    string? SelectedColor, string? Measurement, string? MacroState, MacroStep[] Recording, AutoClickerStatus Repeater);
