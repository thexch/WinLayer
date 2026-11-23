using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinLayer.Core;

namespace WinLayer.Tests;

[TestClass]
public sealed class UtilityTests
{
    [TestMethod]
    [DataRow("batteri", "battery")]
    [DataRow("opacite", "taskbar-opacity")]
    [DataRow("opacité", "pin-opacity")]
    [DataRow("toujours devant", "pin")]
    [DataRow("enregistrer une action", "macro")]
    [DataRow("screenshot", "ocr")]
    [DataRow("ma souris clique toute seule", "clicker")]
    [DataRow("cpu", "cpu")]
    public void RegistryFindsToolsAndSettings(string query, string id)
    {
        var candidates = ActionCatalog.Entries
            .Select(item => (item.Id, Score: ActionCatalog.Match(query, item.Label, item.Keywords)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score).Take(5).Select(item => item.Id).ToArray();
        CollectionAssert.Contains(candidates, id);
    }

    [TestMethod]
    public void CatalogUsesUniqueIdsAndExistingRoutes()
    {
        Assert.AreEqual(ActionCatalog.Entries.Count, ActionCatalog.Entries.Select(item => item.Id).Distinct().Count());
        foreach (var entry in ActionCatalog.Entries)
        {
            Assert.IsTrue(entry.Tool is not null || Enum.TryParse<Page>(entry.Page, true, out _), entry.Id);
            Assert.IsTrue(new UtilityRequest(UtilityAction.Execute, entry.Id).IsValid(), entry.Id);
        }
    }

    [TestMethod]
    public void RejectsDuplicateShortcutsAndUnreadableThemes()
    {
        var options = new UtilityOptions();
        Assert.IsTrue(options.IsValid());
        Assert.IsFalse((options with { Ocr = options.Pin }).IsValid());
        Assert.IsFalse((options with { RadialItems = ["missing"] }).IsValid());
        Assert.IsFalse((options with { Corners = [new(), new("ocr", 199), new(), new()] }).IsValid());
        var theme = new AppTheme("Forêt", "#101E1C", "#192F2B", "#E3F7ED", "#8FE1BF", "#385D50");
        Assert.IsTrue(theme.IsValid());
        Assert.IsFalse((theme with { Ink = theme.Surface }).IsValid());
        Assert.IsTrue((options with { Themes = [theme], Theme = theme.Name }).IsValid());
    }

    [TestMethod]
    public void UtilityBridgeAcceptsCatalogIdsAndRejectsArbitraryCommands()
    {
        const string execute = """{"id":10,"action":"utility","utility":{"action":"execute","id":"ocr"}}""";
        Assert.IsTrue(SettingsBridge.TryRead(execute, out _, out var request));
        Assert.AreEqual(new UtilityRequest(UtilityAction.Execute, "ocr"), request?.Utility);
        Assert.IsFalse(SettingsBridge.TryRead(execute.Replace("ocr", "C:\\\\Windows\\\\explorer.exe"), out _, out _));
        Assert.IsFalse(SettingsBridge.TryRead(execute.Replace("execute", "undo"), out _, out _));
        Assert.IsFalse(SettingsBridge.TryRead(execute.Replace("\"execute\"", "0"), out _, out _));
        Assert.IsFalse(SettingsBridge.TryRead(execute.Replace("\"id\":\"ocr\"", "\"id\":\"ocr\",\"arguments\":\"arg\""), out _, out _));
    }

    [TestMethod]
    public async Task OptionsAndBoundedUndoSurviveRestart()
    {
        var temp = Directory.CreateTempSubdirectory("WinLayerUtils-");
        try
        {
            var file = Path.Combine(temp.FullName, "preferences.json");
            await File.WriteAllTextAsync(file, """{"appearance":"Dark","battery":{"enabled":true,"refreshMinutes":9}}""");
            var store = new PreferencesStore(file);
            var previous = await store.LoadAsync();
            Assert.IsNull(previous.Utilities);
            Assert.AreEqual(9, previous.Battery.RefreshMinutes);
            var restored = new UtilityOptions { Favorites = ["battery", "ocr"], Language = "en" };
            var undo = Enumerable.Repeat(new PreferenceUndo("Préférences", Utilities: new UtilityOptions()), 10).ToArray();
            await store.SaveAsync(previous with { Utilities = restored, Undo = undo });
            var readBack = await store.LoadAsync();
            Assert.AreEqual(Appearance.Dark, readBack.Appearance);
            CollectionAssert.AreEqual(restored.Favorites, readBack.Utilities!.Favorites);
            Assert.AreEqual("en", readBack.Utilities.Language);
            Assert.HasCount(8, readBack.Undo!);
            Assert.IsFalse(readBack.Undo![0].Utilities!.OcrHistory);
            var serialized = SettingsBridge.Write(4, new(new(readBack, new([]), new(UpdateState.Current), "4.0.0", null, Page.Utilities, 1,
                Utilities: new("Outils", null, ["ocr"]), RequestedTarget: "themes")));
            using var reply = JsonDocument.Parse(serialized);
            Assert.AreEqual("utilities", reply.RootElement.GetProperty("response").GetProperty("status").GetProperty("requestedPage").GetString());
        }
        finally { temp.Delete(recursive: true); }
    }

    [TestMethod]
    public void AcceptsOnlyExplicitLocalLauncherIdsAndDrivePaths()
    {
        var launcher = new LocalLauncher("local:" + Guid.NewGuid().ToString("N"), "Bloc-notes", @"C:\Windows\notepad.exe");
        Assert.IsTrue(launcher.IsValid());
        Assert.IsFalse((launcher with { Path = @"\\server\share\file.exe" }).IsValid());
        Assert.IsFalse((launcher with { Path = @"C:\folder\file.txt:script" }).IsValid());
        Assert.IsFalse((launcher with { Id = "local:unknown" }).IsValid());
    }
}
