using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinLayer.Core;

namespace WinLayer.Tests;

[TestClass]
public sealed class WindowsAgentTests
{
    [TestMethod]
    public async Task InstalledAgentSurvivesSettingsClosureRejectsDuplicatesAndQuitsCleanly()
    {
        var executable = Environment.GetEnvironmentVariable("WINLAYER_SMOKE_EXE");
        var version = Environment.GetEnvironmentVariable("WINLAYER_SMOKE_VERSION");
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(version))
            Assert.Inconclusive("Requires the installed version and an isolated Windows session.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var updatePackage = Environment.GetEnvironmentVariable("WINLAYER_SMOKE_UPGRADE_PACKAGE");
        var upgradeVersion = Environment.GetEnvironmentVariable("WINLAYER_SMOKE_UPGRADE_VERSION");
        Preferences? previousPreferences = null;
        Preferences? migratedPreferences = null;
        if (updatePackage is not null)
            (previousPreferences, migratedPreferences) = await UpgradeAsync(executable!, updatePackage, upgradeVersion ?? "1.0.0", timeout.Token);
        using var agent = Process.Start(executable!, "--background")!;
        try
        {
            var firstStatus = await AwaitAgentAsync(agent, timeout.Token);
            Assert.IsNotNull(firstStatus);
            Assert.AreEqual(UpdateState.NotChecked, firstStatus.Updates.State);
            Assert.AreEqual(version, firstStatus.Version);
            if (migratedPreferences is not null)
                Assert.AreEqual(migratedPreferences, firstStatus.Preferences);
            Assert.IsFalse(agent.HasExited);

            var preferences = firstStatus.Preferences;
            var enabled = await LocalProtocol.SendAsync(new(Command.Save, Preferences: preferences with
            {
                Battery = preferences.Battery with { Enabled = true }
            }), timeout.Token);
            Assert.IsNull(enabled.Error);
            BatteryStatus? scan;
            do
            {
                await Task.Delay(100, timeout.Token);
                scan = (await LocalProtocol.SendAsync(new(Command.Status), timeout.Token)).Status?.Battery;
            } while (scan?.UpdatedAt is null);
            Assert.IsNull(scan.Error, "Both hardware providers must be able to enumerate on Windows.");
            var disabledAgain = await LocalProtocol.SendAsync(new(Command.Save, Preferences: preferences), timeout.Token);
            Assert.IsNull(disabledAgain.Error);

            using var duplicate = Process.Start(executable!, "--background")!;
            await duplicate.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, duplicate.ExitCode);
            Assert.IsFalse(agent.HasExited);

            var opened = await LocalProtocol.SendAsync(new(Command.OpenSettings, Page.Battery), timeout.Token);
            Assert.IsNull(opened.Error);
            Assert.AreEqual(Page.Battery, opened.Status?.RequestedPage);
            using var settings = await AwaitSettingsWindowAsync(timeout.Token);
            Assert.IsTrue(settings.CloseMainWindow());
            await settings.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, settings.ExitCode);
            await VerifyAutoClickerWhileSettingsClosedAsync(timeout.Token);
            await VerifyHubWhileSettingsClosedAsync(timeout.Token);
            var afterClosing = await LocalProtocol.SendAsync(new(Command.Status), timeout.Token);
            Assert.AreEqual(version, afterClosing.Status?.Version);
            Assert.IsFalse(agent.HasExited);

            if (previousPreferences is not null)
            {
                var restored = await LocalProtocol.SendAsync(new(Command.Save, Preferences: previousPreferences), timeout.Token);
                Assert.IsNull(restored.Error);
            }

            var quit = await LocalProtocol.SendAsync(new(Command.Quit), timeout.Token);
            Assert.IsNull(quit.Error);
            await agent.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, agent.ExitCode);
        }
        finally
        {
            if (!agent.HasExited)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await LocalProtocol.SendAsync(new(Command.Quit), cleanup.Token);
                    await agent.WaitForExitAsync(cleanup.Token);
                }
                catch (Exception) when (!agent.HasExited)
                {
                    agent.Kill(entireProcessTree: true);
                }
            }
        }
    }

    private static async Task VerifyHubWhileSettingsClosedAsync(CancellationToken stop)
    {
        async Task<Response> Send(HubRequest request)
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(stop);
            operation.CancelAfter(TimeSpan.FromSeconds(8));
            try { return await LocalProtocol.SendAsync(new(Command.Hub, Hub: request), operation.Token); }
            catch (Exception error) { throw new InvalidOperationException($"Native hub operation {request.Tool}/{request.Action} failed.", error); }
        }
        var options = Enum.GetValues<HubTool>().ToDictionary(tool => tool, tool => HubOptions.Default(tool) with { Enabled = true });
        var picker = options[HubTool.Picker];
        var completed = false;
        try
        {
            foreach (var (tool, value) in options)
            {
                var response = await Send(new(tool, HubAction.Configure, value));
                Assert.IsNull(response.Error, $"{tool}");
                Assert.IsTrue(response.Status?.Preferences.Hub?.Get(tool).Enabled);
            }
            Assert.IsFalse(CanRegister(picker.Shortcut));
            var conflict = await Send(new(HubTool.Ruler, HubAction.Configure, options[HubTool.Ruler] with { Shortcut = picker.Shortcut }));
            Assert.IsNotNull(conflict.Error);
            Assert.AreEqual(options[HubTool.Ruler].Shortcut, conflict.Status?.Preferences.Hub?.Get(HubTool.Ruler).Shortcut);

            var macro = new MacroPreset("Agent smoke", [new(MacroKind.Wait, 100)]);
            var saved = await Send(new(HubTool.Macro, HubAction.SaveMacro, Macro: macro));
            Assert.IsNull(saved.Error);
            Assert.IsTrue(saved.Status?.Preferences.Hub?.Macros.Any(item => item.Name == macro.Name));
            Assert.IsNull((await Send(new(HubTool.Switcher, HubAction.Open))).Error);
            var visibleDeadline = Stopwatch.StartNew();
            while (FindWindowW(null, "WinLayer — Sélecteur de fenêtres") == IntPtr.Zero && visibleDeadline.Elapsed < TimeSpan.FromSeconds(6))
                await Task.Delay(100, stop);
            var visible = FindWindowW(null, "WinLayer — Sélecteur de fenêtres") != IntPtr.Zero;
            var afterOpening = await LocalProtocol.SendAsync(new(Command.Status), stop);
            Assert.IsTrue(visible, afterOpening.Status?.Hub?.Errors.GetValueOrDefault(HubTool.Switcher) ?? "Switcher window not found.");
            Assert.IsNull((await Send(new(HubTool.Switcher, HubAction.Stop))).Error);

            using var samples = CancellationTokenSource.CreateLinkedTokenSource(stop);
            samples.CancelAfter(TimeSpan.FromSeconds(12));
            HubMetrics? metrics;
            do
            {
                await Task.Delay(250, samples.Token);
                var response = await LocalProtocol.SendAsync(new(Command.Status, Page.System), samples.Token);
                metrics = response.Status?.Hub?.Metrics;
            } while (metrics is null || metrics.TotalRam is null || !metrics.History.Any(sample => sample.Cpu is not null));
            Assert.IsTrue(metrics.TotalRam > 0);
            Assert.IsTrue(metrics.FreeRam > 0);
            Assert.IsTrue(metrics.Threads > 0);
            var processes = await Send(new(HubTool.System, HubAction.Processes));
            Assert.IsNull(processes.Error);
            Assert.IsTrue(processes.Status?.Hub?.Metrics?.Processes.Any(process => process.Pid == Environment.ProcessId));
            completed = true;
        }
        finally
        {
            if (completed)
            {
                await Send(new(HubTool.Macro, HubAction.DeleteMacro, Value: "Agent smoke"));
                foreach (var (tool, value) in options.Reverse())
                    await Send(new(tool, HubAction.Configure, value with { Enabled = false }));
            }
        }
        Assert.IsTrue(CanRegister(picker.Shortcut));
    }

    private static async Task VerifyAutoClickerWhileSettingsClosedAsync(CancellationToken stop)
    {
        var options = new AutoClickerOptions { Enabled = true, Shortcut = new(0x82, 3), Emergency = new(0x83, 3) };
        Assert.IsTrue(CanRegister(options.Shortcut));
        var enabled = await LocalProtocol.SendAsync(new(Command.SaveAutoClicker, ClickerOptions: options), stop);
        Assert.IsNull(enabled.Error);
        Assert.AreEqual(options, enabled.Status?.Preferences.AutoClicker);
        Assert.AreEqual(ClickState.Idle, enabled.Status?.AutoClicker?.State);
        Assert.IsFalse(CanRegister(options.Shortcut));
        Assert.IsFalse(CanRegister(options.Emergency));

        // Reserve the candidate's emergency shortcut in another process, outside the agent's window.
        var competing = new ClickShortcut(0x84, 3);
        var candidate = options with { Shortcut = new(0x85, 3), Emergency = competing };
        var rejected = await Task.Run(() =>
        {
            const int competingId = 15;
            Assert.IsTrue(RegisterHotKey(IntPtr.Zero, competingId, (uint)competing.Modifiers, (uint)competing.Key));
            try
            {
                return LocalProtocol.SendAsync(new(Command.SaveAutoClicker, ClickerOptions: candidate), stop)
                    .GetAwaiter().GetResult();
            }
            finally
            {
                Assert.IsTrue(UnregisterHotKey(IntPtr.Zero, competingId));
            }
        }, stop);
        Assert.IsNotNull(rejected.Error);
        Assert.AreEqual(options, rejected.Status?.Preferences.AutoClicker);
        Assert.IsTrue(CanRegister(candidate.Shortcut));
        Assert.IsFalse(CanRegister(options.Shortcut));

        var paused = await LocalProtocol.SendAsync(new(Command.PauseAutoClicker), stop);
        Assert.AreEqual(ClickState.Paused, paused.Status?.AutoClicker?.State);
        var rearmed = await LocalProtocol.SendAsync(new(Command.RearmAutoClicker), stop);
        Assert.AreEqual(ClickState.Idle, rearmed.Status?.AutoClicker?.State);
        var general = await LocalProtocol.SendAsync(new(Command.Save, Preferences: new Preferences
        {
            Appearance = enabled.Status!.Preferences.Appearance,
            StartWithWindows = enabled.Status.Preferences.StartWithWindows,
            Battery = enabled.Status.Preferences.Battery,
            BatteryTrayIcons = enabled.Status.Preferences.BatteryTrayIcons,
            AutomaticUpdateChecks = enabled.Status.Preferences.AutomaticUpdateChecks
        }), stop);
        Assert.IsNull(general.Error);
        Assert.AreEqual(options, general.Status?.Preferences.AutoClicker);

        var disabled = await LocalProtocol.SendAsync(new(Command.SaveAutoClicker, ClickerOptions: new()), stop);
        Assert.IsNull(disabled.Error);
        Assert.IsTrue(CanRegister(options.Shortcut));
        Assert.IsTrue(CanRegister(options.Emergency));
    }

    private static bool CanRegister(ClickShortcut shortcut)
    {
        const int id = 14;
        if (!RegisterHotKey(IntPtr.Zero, id, (uint)shortcut.Modifiers, (uint)shortcut.Key))
            return false;

        Assert.IsTrue(UnregisterHotKey(IntPtr.Zero, id));
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr FindWindowW(string? className, string title);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    private static async Task<(Preferences Original, Preferences Migrated)> UpgradeAsync(
        string executable, string updatePackage, string oldVersion, CancellationToken stop)
    {
        using var oldAgent = Process.Start(executable, "--background")!;
        var previous = await AwaitAgentAsync(oldAgent, stop);
        Assert.IsNotNull(previous);
        Assert.AreEqual(oldVersion, previous.Version);
        var migrated = previous.Preferences with
        {
            Appearance = Appearance.Dark,
            StartWithWindows = true,
            AutomaticUpdateChecks = false,
            Battery = previous.Preferences.Battery with { Alerts = false, LowThreshold = 32, RefreshMinutes = 11 }
        };
        var save = await LocalProtocol.SendAsync(new(Command.Save, Preferences: migrated), stop);
        Assert.IsNull(save.Error);
        Assert.AreEqual(migrated, save.Status?.Preferences);
        if (oldVersion == "2.4.0")
        {
            var clicker = migrated.AutoClicker with { Cps = 77 };
            var savedClicker = await LocalProtocol.SendAsync(new(Command.SaveAutoClicker, ClickerOptions: clicker), stop);
            Assert.IsNull(savedClicker.Error);
            migrated = migrated with { AutoClicker = clicker };
        }
        var quit = await LocalProtocol.SendAsync(new(Command.Quit), stop);
        Assert.IsNull(quit.Error);
        await oldAgent.WaitForExitAsync(stop);

        var root = Path.GetDirectoryName(Path.GetDirectoryName(executable))!;
        var start = new ProcessStartInfo(Path.Combine(root, "Update.exe"));
        foreach (var argument in new[] { "apply", "--silent", "--norestart", "--package", updatePackage })
            start.ArgumentList.Add(argument);
        using var updater = Process.Start(start)!;
        await updater.WaitForExitAsync(stop);
        Assert.AreEqual(0, updater.ExitCode);
        return (previous.Preferences, migrated);
    }

    private static async Task<Process> AwaitSettingsWindowAsync(CancellationToken stop)
    {
        while (true)
        {
            stop.ThrowIfCancellationRequested();
            foreach (var candidate in Process.GetProcessesByName("WinLayer.Settings"))
            {
                if (candidate.MainWindowHandle != IntPtr.Zero)
                {
                    var title = candidate.MainWindowTitle;
                    if (title == "WinLayer")
                        return candidate;
                    Assert.AreNotEqual("WinLayer — Réglages indisponibles", title, "The WebView2 host reported a startup error.");
                }
                candidate.Dispose();
            }

            await Task.Delay(100, stop);
        }
    }

    private static async Task<AgentStatus?> AwaitAgentAsync(Process agent, CancellationToken stop)
    {
        while (!agent.HasExited)
        {
            stop.ThrowIfCancellationRequested();
            try
            {
                var response = await LocalProtocol.SendAsync(new(Command.Status), stop);
                return response.Status;
            }
            catch (TimeoutException)
            {
                await Task.Delay(200, stop);
            }
        }

        return null;
    }
}
