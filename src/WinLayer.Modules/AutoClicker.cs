using WinLayer.Core;

namespace WinLayer.Modules;

public interface IAutoClickerDriver
{
    TimeSpan Elapsed { get; }
    bool WaitUntil(TimeSpan elapsed, CancellationToken cancellation);
    bool CanClick { get; }
    void Click(AutoClickerOptions options, CancellationToken cancellation);
}

public enum ClickHotkeyAction { Ignore, Start, Stop, Pause }

public static class AutoClicker
{
    public static ClickHotkeyAction OnHotkey(AutoClickerOptions options, ClickState state, ClickShortcut shortcut)
    {
        if (!options.Enabled || state == ClickState.Paused)
            return ClickHotkeyAction.Ignore;
        if (shortcut == options.Emergency)
            return ClickHotkeyAction.Pause;
        if (shortcut != options.Shortcut)
            return ClickHotkeyAction.Ignore;
        if (state == ClickState.Idle)
            return ClickHotkeyAction.Start;
        return options.Mode == ClickMode.Toggle ? ClickHotkeyAction.Stop : ClickHotkeyAction.Ignore;
    }

    public static bool ReleasedShortcut(int key, ClickShortcut shortcut) =>
        key == shortcut.Key || key switch
        {
            0x10 or 0xA0 or 0xA1 => ((ShortcutModifiers)shortcut.Modifiers).HasFlag(ShortcutModifiers.Shift),
            0x11 or 0xA2 or 0xA3 => ((ShortcutModifiers)shortcut.Modifiers).HasFlag(ShortcutModifiers.Control),
            0x12 or 0xA4 or 0xA5 => ((ShortcutModifiers)shortcut.Modifiers).HasFlag(ShortcutModifiers.Alt),
            0x5B or 0x5C => ((ShortcutModifiers)shortcut.Modifiers).HasFlag(ShortcutModifiers.Windows),
            _ => false
        };

    public static void Run(AutoClickerOptions options, IAutoClickerDriver driver, CancellationToken cancellation)
    {
        var interval = TimeSpan.FromSeconds(1d / options.Cps);
        var deadline = options.Limit == ClickLimit.Duration
            ? TimeSpan.FromSeconds(options.DurationSeconds) : TimeSpan.MaxValue;
        int? remaining = options.Limit == ClickLimit.Count ? options.Count : null;
        var due = TimeSpan.Zero;
        while (remaining != 0)
        {
            if (due >= deadline || cancellation.IsCancellationRequested ||
                !driver.WaitUntil(due, cancellation) || driver.Elapsed >= deadline || !driver.CanClick)
                return;

            driver.Click(options, cancellation);
            remaining--;
            due += interval;
            // An interrupted scheduler may miss clicks. Do not send a burst to catch up.
            var now = driver.Elapsed;
            if (due <= now)
                due = now + interval;
        }
    }
}
