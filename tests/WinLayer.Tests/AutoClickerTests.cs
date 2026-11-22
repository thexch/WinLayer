using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinLayer.Core;
using WinLayer.Modules;

namespace WinLayer.Tests;

[TestClass]
public sealed class AutoClickerTests
{
    [TestMethod]
    [DataRow(1, 1000d)]
    [DataRow(25, 40d)]
    [DataRow(200, 5d)]
    public void SchedulesExactCountAtRequestedCadence(int cps, double interval)
    {
        var driver = new FakeDriver();
        AutoClicker.Run(new() { Kind = ClickKind.Double, Limit = ClickLimit.Count, Count = 3, Cps = cps },
            driver, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { 0d, interval, 2 * interval },
            driver.Clicks.Select(click => click.Time.TotalMilliseconds).ToArray());
        Assert.IsTrue(driver.Clicks.All(click => click.Kind == ClickKind.Double));
    }

    [TestMethod]
    public void DurationAndLateWakeNeverEmitAfterDeadlineOrCatchUp()
    {
        var driver = new FakeDriver { DelayFirstWait = TimeSpan.FromMilliseconds(700) };
        AutoClicker.Run(new() { Cps = 10, Limit = ClickLimit.Duration, DurationSeconds = 1 },
            driver, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { 700d, 800d, 900d },
            driver.Clicks.Select(click => click.Time.TotalMilliseconds).ToArray());

        driver = new FakeDriver { DelayFirstWait = TimeSpan.FromSeconds(2) };
        AutoClicker.Run(new() { Limit = ClickLimit.Duration, DurationSeconds = 1 }, driver, CancellationToken.None);
        Assert.IsEmpty(driver.Clicks);
    }

    [TestMethod]
    public void ReleaseAndEmergencyCancellationStopAnUnlimitedRun()
    {
        var driver = new FakeDriver { ReleaseAfter = 2 };
        AutoClicker.Run(new() { Mode = ClickMode.Hold }, driver, CancellationToken.None);
        Assert.HasCount(2, driver.Clicks);

        using var emergency = new CancellationTokenSource();
        driver = new FakeDriver { AfterClick = emergency.Cancel };
        AutoClicker.Run(new(), driver, emergency.Token);
        Assert.HasCount(1, driver.Clicks);
    }

    [TestMethod]
    public void ToggleHoldAndEmergencyHaveIndependentTransitions()
    {
        var options = new AutoClickerOptions { Enabled = true };
        Assert.AreEqual(ClickHotkeyAction.Start, AutoClicker.OnHotkey(options, ClickState.Idle, options.Shortcut));
        Assert.AreEqual(ClickHotkeyAction.Stop, AutoClicker.OnHotkey(options, ClickState.Active, options.Shortcut));
        Assert.AreEqual(ClickHotkeyAction.Ignore, AutoClicker.OnHotkey(options with { Mode = ClickMode.Hold },
            ClickState.Active, options.Shortcut));
        Assert.AreEqual(ClickHotkeyAction.Pause, AutoClicker.OnHotkey(options, ClickState.Active, options.Emergency));
        Assert.AreEqual(ClickHotkeyAction.Ignore, AutoClicker.OnHotkey(options, ClickState.Paused, options.Shortcut));
        Assert.AreEqual(ClickHotkeyAction.Ignore, AutoClicker.OnHotkey(options with { Enabled = false },
            ClickState.Idle, options.Shortcut));

        var combination = new ClickShortcut(0x43, (int)(ShortcutModifiers.Control | ShortcutModifiers.Alt));
        Assert.IsTrue(AutoClicker.ReleasedShortcut(0x43, combination));
        Assert.IsTrue(AutoClicker.ReleasedShortcut(0xA3, combination));
        Assert.IsTrue(AutoClicker.ReleasedShortcut(0xA4, combination));
        Assert.IsFalse(AutoClicker.ReleasedShortcut(0xA0, combination));
        Assert.IsFalse(AutoClicker.ReleasedShortcut(0x46, combination));
    }

    [TestMethod]
    public void OptionsAcceptIndependentShortcutsAndRejectReservedOrInvalidValues()
    {
        var defaults = new AutoClickerOptions();
        Assert.IsTrue((defaults with { Shortcut = new(0x43, 3), Mode = ClickMode.Hold, Cps = 200 }).IsValid());
        Assert.IsFalse((defaults with { Emergency = defaults.Shortcut }).IsValid());
        Assert.IsFalse((defaults with { Shortcut = new(0x7B) }).IsValid());
        Assert.IsFalse((defaults with { Shortcut = new(0x43) }).IsValid());
        Assert.IsFalse((defaults with { Cps = 201 }).IsValid());
        Assert.IsFalse((defaults with { Count = 0 }).IsValid());
        Assert.IsFalse((defaults with { DurationSeconds = 0 }).IsValid());
        Assert.IsFalse((defaults with { Kind = (ClickKind)9 }).IsValid());
    }

    [TestMethod]
    public async Task LegacyPreferencesRoundTripWithAutoClickerSafelyDisabledByDefault()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "preferences.json");
            await File.WriteAllTextAsync(path, """{"appearance":"Dark","battery":{"enabled":true}}""");
            var store = new PreferencesStore(path);
            var options = new AutoClickerOptions
            {
                Enabled = true, Kind = ClickKind.Middle, Shortcut = new(0x43, 3),
                Cps = 77, Position = ClickPosition.Fixed, X = -520, Y = 120, Limit = ClickLimit.Count, Count = 30
            };
            var previous = await store.LoadAsync();
            Assert.IsFalse(previous.AutoClicker.Enabled);
            Assert.IsTrue(previous.Battery.Enabled);
            Assert.AreEqual(Appearance.Dark, previous.Appearance);
            var configured = previous with { AutoClicker = options };
            await store.SaveAsync(configured);
            Assert.AreEqual(configured, await store.LoadAsync());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class FakeDriver : IAutoClickerDriver
    {
        public TimeSpan Elapsed { get; private set; }
        public List<(TimeSpan Time, ClickKind Kind)> Clicks { get; } = new();
        public TimeSpan DelayFirstWait { get; init; }
        public int ReleaseAfter { get; init; } = int.MaxValue;
        public Action? AfterClick { get; init; }
        public bool CanClick => Clicks.Count < ReleaseAfter;

        public bool WaitUntil(TimeSpan elapsed, CancellationToken cancellation)
        {
            Elapsed = elapsed + (Clicks.Count == 0 ? DelayFirstWait : TimeSpan.Zero);
            return !cancellation.IsCancellationRequested;
        }

        public void Click(AutoClickerOptions options, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Clicks.Add((Elapsed, options.Kind));
            AfterClick?.Invoke();
        }
    }
}
