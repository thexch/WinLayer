namespace WinLayer.Core;

public enum ClickKind { Left, Right, Middle, Double }
public enum ClickMode { Toggle, Hold }
public enum ClickPosition { Cursor, Fixed }
public enum ClickLimit { Unlimited, Count, Duration }
public enum ClickState { Idle, Active, Paused }

[Flags]
public enum ShortcutModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }

// Virtual-key codes and modifier flags use the same values as RegisterHotKey.
public readonly record struct ClickShortcut(int Key, int Modifiers = 0)
{
    public bool IsValid() =>
        (Modifiers & ~15) == 0 &&
        (Key is >= 0x70 and <= 0x87 and not 0x7B ||
         (Key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A &&
          ((ShortcutModifiers)Modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Windows)) != 0));
}

public sealed record AutoClickerOptions
{
    public bool Enabled { get; init; }
    public ClickKind Kind { get; init; }
    public int Cps { get; init; } = 10;
    public ClickShortcut Shortcut { get; init; } = new(0x75);
    public ClickShortcut Emergency { get; init; } = new(0x79,
        (int)(ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift));
    public ClickMode Mode { get; init; }
    public ClickPosition Position { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public ClickLimit Limit { get; init; }
    public int Count { get; init; } = 100;
    public int DurationSeconds { get; init; } = 10;

    public bool IsValid() => Enum.IsDefined(Kind) && Cps is >= 1 and <= 200 &&
        Shortcut.IsValid() && Emergency.IsValid() && Shortcut != Emergency &&
        Enum.IsDefined(Mode) && Enum.IsDefined(Position) && Enum.IsDefined(Limit) &&
        Count is >= 1 and <= 1_000_000 && DurationSeconds is >= 1 and <= 86_400;
}

public sealed record AutoClickerStatus(ClickState State, string? Error = null);
