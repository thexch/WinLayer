using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinLayer.Core;
using WinLayer.Modules;

namespace WinLayer.Tests;

[TestClass]
public sealed class HubTests
{
    [TestMethod]
    public void DefaultsAreReadyWithoutApplyingATaskbarEffect()
    {
        foreach (var tool in Enum.GetValues<HubTool>())
        {
            var options = new HubPreferences().Get(tool);
            Assert.IsTrue(options.IsValid(), $"{tool}");
            Assert.IsTrue(options.Enabled);
        }
        Assert.IsNull(new Preferences().Normalize().Hub);
        Assert.AreEqual(TaskbarEffect.Windows, new HubPreferences().Get(HubTool.Taskbar).TaskbarEffect);
    }

    [TestMethod]
    public void LegacyTaskbarSwitchSelectsTheEquivalentEffect()
    {
        var prior = HubOptions.Default(HubTool.Taskbar) with { TaskbarEffect = null, Enabled = false };
        var preferences = new HubPreferences { Tools = new() { [HubTool.Taskbar] = prior } };
        Assert.AreEqual(TaskbarEffect.Windows, preferences.Get(HubTool.Taskbar).TaskbarEffect);
        preferences.Tools[HubTool.Taskbar] = prior with { Enabled = true };
        Assert.AreEqual(TaskbarEffect.Accent, preferences.Get(HubTool.Taskbar).TaskbarEffect);
        preferences.Tools[HubTool.Taskbar] = prior with { TaskbarEffect = TaskbarEffect.Frame };
        Assert.AreEqual(TaskbarEffect.Frame, preferences.Get(HubTool.Taskbar).TaskbarEffect);
    }

    [TestMethod]
    public void PaletteStyleAndMacroBoundsAreValidated()
    {
        var options = HubOptions.Default(HubTool.Taskbar);
        Assert.IsFalse((options with { Color = "#GGGGGG" }).IsValid());
        Assert.IsFalse((options with { Styles = [new("duplicate", "#000000", 60, 2), new("DUPLICATE", "#222222", 80, 3)] }).IsValid());
        Assert.IsFalse((options with { TrayMetrics = [TrayMetric.Cpu, TrayMetric.Cpu] }).IsValid());
        Assert.IsFalse((options with { Palettes = [new("Wrong", ["abc"])] }).IsValid());

        var macro = new MacroPreset("Démo", [new(MacroKind.Wait, 400), new(MacroKind.Left, 30, 2000, 9000),
            new(MacroKind.Key, 120, Key: 0x43, Modifiers: 2)]);
        Assert.IsTrue(macro.IsValid());
        Assert.IsFalse((macro with { Steps = [new(MacroKind.Left, 0, -1, 100)] }).IsValid());
        Assert.IsFalse((macro with { Steps = [new(MacroKind.Key, 0, Key: 255)] }).IsValid());
    }

    [TestMethod]
    public void HubBridgeRejectsUnexpectedActionsParametersAndOversizedMacros()
    {
        Assert.IsTrue(SettingsBridge.TryRead("""{"id":4,"action":"status","view":"system"}""", out _, out var response));
        Assert.AreEqual(new Request(Command.Status, Page.System), response);
        Assert.IsFalse(SettingsBridge.TryRead("""{"id":4,"action":"ready","view":"system"}""", out _, out _));
        Assert.IsFalse(SettingsBridge.TryRead("""{"id":4,"action":"hub","hub":{"tool":"pin","action":"endProcess","pid":101,"started":123}}""",
            out _, out _));
        Assert.IsFalse(SettingsBridge.TryRead("""{"id":4,"action":"hub","hub":{"tool":"system","action":"endProcess","pid":101}}""",
            out _, out _));
        Assert.IsFalse(SettingsBridge.TryRead("""{"id":4,"action":"hub","hub":{"tool":"picker","action":"color","value":"other"}}""",
            out _, out _));
        Assert.IsTrue(SettingsBridge.TryRead("""
            {"id":4,"action":"hub","hub":{"tool":"macro","action":"saveMacro",
               "macro":{"name":"Démo","steps":[{"kind":"key","key":67,"modifiers":2,"delay":100}]}}}
            """, out _, out var hub));
        Assert.AreEqual(0x43, hub?.Hub?.Macro?.Steps[0].Key);
    }

    [TestMethod]
    public void NetworkAndCpuDeltasRejectCounterResets()
    {
        Assert.AreEqual((100d, 200d), HubCalculations.NetworkRate(300, 500, 100, 100, 2));
        Assert.AreEqual((null, null), HubCalculations.NetworkRate(80, 500, 100, 100, 2));
        Assert.AreEqual((null, null), HubCalculations.NetworkRate(300, 500, 100, 100, 0));
        Assert.AreEqual(75d, HubCalculations.Utilization(50, 200, 25, 100));
        Assert.IsNull(HubCalculations.Utilization(50, 90, 25, 100));
    }

    [TestMethod]
    public void CoordinatesAndColorFormatsPreserveMeasurements()
    {
        Assert.AreEqual("3 × 4 px · 5 px", HubCalculations.Measure(-1600, 30, -1597, 26, RulerMode.Free));
        Assert.AreEqual("3 px", HubCalculations.Measure(-1600, 30, -1597, 26, RulerMode.Horizontal));
        Assert.AreEqual("4 px", HubCalculations.Measure(-1600, 30, -1597, 26, RulerMode.Vertical));
        Assert.AreEqual("#00FF00", HubCalculations.FormatColor("#00ff00", ColorFormat.Hex));
        Assert.AreEqual("rgb(0, 255, 0)", HubCalculations.FormatColor("#00ff00", ColorFormat.Rgb));
        Assert.AreEqual("hsl(120, 100%, 50%)", HubCalculations.FormatColor("#00ff00", ColorFormat.Hsl));
        Assert.AreEqual("hsl(0, 0%, 100%)", HubCalculations.FormatColor("#ffffff", ColorFormat.Hsl));
    }

    [TestMethod]
    public async Task HubSettingsPreservePriorPreferencesAcrossARealStoreRoundTrip()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var file = Path.Combine(directory.FullName, "preferences.json");
            await File.WriteAllTextAsync(file, """{"appearance":"Dark","battery":{"enabled":true},"autoClicker":{"cps":25}}""");
            var store = new PreferencesStore(file);
            var previous = await store.LoadAsync();
            Assert.IsTrue(previous.Battery.Enabled);
            Assert.AreEqual(25, previous.AutoClicker.Cps);
            var options = HubOptions.Default(HubTool.Picker) with { Enabled = true, ColorFormat = ColorFormat.Hsl, Zoom = 20 };
            await store.SaveAsync(previous with { Hub = new()
            {
                Tools = new() { [HubTool.Picker] = options },
                Colors = ["#CC00AA", "#CC00AA", "invalid"],
                Favorites = ["#990044"]
            } });
            var saved = await store.LoadAsync();
            Assert.AreEqual(previous.Battery, saved.Battery);
            Assert.AreEqual(previous.AutoClicker, saved.AutoClicker);
            Assert.AreEqual(previous.Appearance, saved.Appearance);
            Assert.IsNotNull(saved.Hub);
            Assert.IsTrue(saved.Hub.Get(HubTool.Picker).Enabled);
            Assert.AreEqual(ColorFormat.Hsl, saved.Hub.Get(HubTool.Picker).ColorFormat);
            CollectionAssert.AreEqual(new[] { "#CC00AA" }, saved.Hub.Colors);
        }
        finally { directory.Delete(recursive: true); }
    }
}
