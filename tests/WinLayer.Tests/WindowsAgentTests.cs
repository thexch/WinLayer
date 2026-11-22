using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinLayer.Core;

namespace WinLayer.Tests;

[TestClass]
public sealed class WindowsAgentTests
{
    [TestMethod]
    public async Task PublishedAgentRunsInBackgroundRejectsDuplicatesAndQuitsCleanly()
    {
        var executable = Environment.GetEnvironmentVariable("WINLAYER_SMOKE_EXE");
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(executable))
            Assert.Inconclusive("Requires the published agent and an isolated Windows session.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var agent = Process.Start(executable!, "--background")!;
        try
        {
            var firstStatus = await AwaitAgentAsync(agent, timeout.Token);
            Assert.IsNotNull(firstStatus);
            Assert.AreEqual(UpdateState.Unavailable, firstStatus.Updates.State);
            Assert.IsFalse(agent.HasExited);

            using var duplicate = Process.Start(executable!, "--background")!;
            await duplicate.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, duplicate.ExitCode);
            Assert.IsFalse(agent.HasExited);

            var quit = await LocalProtocol.SendAsync(new(Command.Quit), timeout.Token);
            Assert.IsNull(quit.Error);
            await agent.WaitForExitAsync(timeout.Token);
            Assert.AreEqual(0, agent.ExitCode);
        }
        finally
        {
            if (!agent.HasExited)
                agent.Kill();
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
