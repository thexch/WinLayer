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

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var agent = Process.Start(executable!, "--background")!;
        try
        {
            var firstStatus = await AwaitAgentAsync(agent, timeout.Token);
            Assert.IsNotNull(firstStatus);
            Assert.AreEqual(UpdateState.NotChecked, firstStatus.Updates.State);
            Assert.AreEqual(version, firstStatus.Version);
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

    private static async Task<Process> AwaitSettingsWindowAsync(CancellationToken stop)
    {
        while (true)
        {
            stop.ThrowIfCancellationRequested();
            foreach (var candidate in Process.GetProcessesByName("WinLayer.Settings"))
            {
                if (candidate.MainWindowHandle != IntPtr.Zero)
                    return candidate;
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
