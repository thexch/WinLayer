using System.Diagnostics;
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
        Preferences? previousPreferences = null;
        Preferences? migratedPreferences = null;
        if (updatePackage is not null)
            (previousPreferences, migratedPreferences) = await UpgradeFromV1Async(executable!, updatePackage, timeout.Token);
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

    private static async Task<(Preferences Original, Preferences Migrated)> UpgradeFromV1Async(
        string executable, string updatePackage, CancellationToken stop)
    {
        using var oldAgent = Process.Start(executable, "--background")!;
        var previous = await AwaitAgentAsync(oldAgent, stop);
        Assert.IsNotNull(previous);
        Assert.AreEqual("1.0.0", previous.Version);
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
