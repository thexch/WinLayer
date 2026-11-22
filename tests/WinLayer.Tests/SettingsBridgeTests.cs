using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinLayer.Core;

namespace WinLayer.Tests;

[TestClass]
public sealed class SettingsBridgeTests
{
    [TestMethod]
    [DataRow("status", Command.Status)]
    [DataRow("refreshBattery", Command.RefreshBattery)]
    [DataRow("checkUpdates", Command.CheckUpdates)]
    [DataRow("pauseAutoClicker", Command.PauseAutoClicker)]
    [DataRow("rearmAutoClicker", Command.RearmAutoClicker)]
    public void MapsOnlyAllowlistedAgentCommands(string action, Command command)
    {
        Assert.IsTrue(SettingsBridge.TryRead($$"""{"id":4,"action":"{{action}}"}""", out var id, out var request));
        Assert.AreEqual(4, id);
        Assert.AreEqual(new Request(command), request);
    }

    [TestMethod]
    [DataRow("""{"id":1,"action":"quit"}""")]
    [DataRow("""{"id":1,"action":"openSettings"}""")]
    [DataRow("""{"id":1,"action":"status","command":1}""")]
    [DataRow("""{"id":1,"action":"status","action":"save"}""")]
    [DataRow("""{"id":0,"action":"status"}""")]
    [DataRow("""{"id":1,"action":"save","preferences":{}}""")]
    [DataRow("""{"id":1,"action":"installUpdate","version":"2.0.0-preview"}""")]
    public void RefusesUnknownCommandsAndInvalidPayloads(string message)
    {
        Assert.IsFalse(SettingsBridge.TryRead(message, out _, out var request));
        Assert.IsNull(request);
    }

    [TestMethod]
    public void SaveRequiresCompleteAndBoundedPreferences()
    {
        const string message = """
            {"id":5,"action":"save","preferences":{"appearance":"dark","startWithWindows":true,
              "automaticUpdateChecks":false,"battery":{"enabled":true,"refreshMinutes":5,"alerts":true,"lowThreshold":20}}}
            """;
        Assert.IsTrue(SettingsBridge.TryRead(message, out _, out var request));
        Assert.AreEqual(new Preferences
        {
            Appearance = Appearance.Dark, StartWithWindows = true, AutomaticUpdateChecks = false,
            Battery = new() { Enabled = true }
        }, request?.Preferences);
        Assert.IsFalse(SettingsBridge.TryRead(message.Replace("20", "51"), out _, out _));
        Assert.IsFalse(SettingsBridge.TryRead(message.Replace("\"dark\"", "2"), out _, out _));
        Assert.IsFalse(SettingsBridge.TryRead(message.PadRight(4097), out _, out _));
    }

    [TestMethod]
    public void AutoClickerRequestIsTypedBoundedAndCannotRideOtherCommands()
    {
        const string message = """
            {"id":5,"action":"saveAutoClicker","clickerOptions":{"enabled":true,"kind":"double","cps":200,
              "shortcut":{"key":67,"modifiers":3},"mode":"hold","position":"fixed","x":-42,"y":90,"limit":"duration"}}
            """;
        Assert.IsTrue(SettingsBridge.TryRead(message, out _, out var request));
        Assert.AreEqual(Command.SaveAutoClicker, request?.Command);
        Assert.AreEqual(new ClickShortcut(67, 3), request?.ClickerOptions?.Shortcut);
        Assert.AreEqual(ClickMode.Hold, request?.ClickerOptions?.Mode);
        Assert.IsFalse(SettingsBridge.TryRead(message.Replace("200", "201"), out _, out _));
        Assert.IsFalse(SettingsBridge.TryRead(message.Replace("saveAutoClicker", "status"), out _, out _));
        Assert.IsFalse(SettingsBridge.TryRead(message.Replace("\"double\"", "3"), out _, out _));
    }

    [TestMethod]
    public void ReadyRemainsInTheHostAndInstallCarriesAnExactVersion()
    {
        Assert.IsTrue(SettingsBridge.TryRead("""{"id":1,"action":"ready"}""", out _, out var ready));
        Assert.IsNull(ready);
        Assert.IsTrue(SettingsBridge.TryRead("""{"id":2,"action":"installUpdate","version":"2.0.0"}""", out _, out var update));
        Assert.AreEqual(new Request(Command.InstallUpdate, Version: "2.0.0", ReportProgress: true), update);
    }

    [TestMethod]
    public async Task UpdateProgressFramesKeepThePipeOpenUntilTheFinalResponse()
    {
        using var stream = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await LocalProtocol.WriteAsync(stream, new Response(Progress: 42), timeout.Token);
        await LocalProtocol.WriteAsync(stream, new Response(Progress: 101), timeout.Token);
        await LocalProtocol.WriteAsync(stream, new Response(Progress: 100), timeout.Token);
        await LocalProtocol.WriteAsync(stream, new Response(Error: "Update failed."), timeout.Token);
        stream.Position = 0;
        var progress = new List<int>();

        var final = await LocalProtocol.ReadResponseAsync(stream, progress.Add, timeout.Token);

        CollectionAssert.AreEqual(new[] { 42, 100 }, progress);
        Assert.AreEqual("Update failed.", final.Error);
    }

    [TestMethod]
    public void ReplyUsesCamelCaseAndStringEnumsForTheBrowser()
    {
        var snapshot = new AgentStatus(new(), new([new("id", "HyperX", BatteryState.Available, 72, BatterySource.HyperXUsb)]),
            new(UpdateState.NotChecked), "2.0.0", null, Page.Battery, 3);
        using var document = JsonDocument.Parse(SettingsBridge.Write(19, new(snapshot)));
        var envelope = document.RootElement;
        var status = envelope.GetProperty("response").GetProperty("status");
        Assert.AreEqual(19, envelope.GetProperty("id").GetInt32());
        Assert.AreEqual("battery", status.GetProperty("requestedPage").GetString());
        Assert.AreEqual("notChecked", status.GetProperty("updates").GetProperty("state").GetString());
        var reading = status.GetProperty("battery").GetProperty("devices")[0];
        Assert.AreEqual("available", reading.GetProperty("state").GetString());
        Assert.AreEqual("hyperXUsb", reading.GetProperty("source").GetString());
    }
}
