using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinLayer.Core;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace WinLayer.Tests;

[TestClass]
public sealed class CoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WinLayer.Tests", Guid.NewGuid().ToString("N"));

    [TestMethod]
    public async Task MissingPreferencesUseOptInStartupAndBatteryDefaults()
    {
        var preferences = await new PreferencesStore(FilePath).LoadAsync();
        Assert.IsFalse(preferences.StartWithWindows);
        Assert.IsFalse(preferences.Battery.Enabled);
        Assert.IsTrue(preferences.Battery.Alerts);
        Assert.IsTrue(preferences.BatteryTrayIcons);
        Assert.IsTrue(preferences.AutomaticUpdateChecks);
    }

    [TestMethod]
    public async Task PreferencesSurviveReloadWithNormalizedBatteryBounds()
    {
        var store = new PreferencesStore(FilePath);
        var preferences = new Preferences
        {
            StartWithWindows = true,
            Appearance = Appearance.Dark,
            AutomaticUpdateChecks = false,
            BatteryTrayIcons = false,
            Battery = new() { Enabled = true, Alerts = false, RefreshMinutes = 1, LowThreshold = 100 }
        };

        await store.SaveAsync(preferences);
        var reloaded = await new PreferencesStore(FilePath).LoadAsync();

        Assert.AreEqual(preferences with
        {
            Battery = preferences.Battery with { RefreshMinutes = 2, LowThreshold = 50 }
        }, reloaded);
        Assert.IsFalse(File.Exists(FilePath + ".tmp"));
    }

    [TestMethod]
    public async Task CorruptPreferencesAreReportedAndPreserved()
    {
        Directory.CreateDirectory(_directory);
        const string malformed = "{ unreadable";
        await File.WriteAllTextAsync(FilePath, malformed);

        await Assert.ThrowsAsync<JsonException>(() => new PreferencesStore(FilePath).LoadAsync());

        Assert.AreEqual(malformed, await File.ReadAllTextAsync(FilePath));
    }

    [TestMethod]
    public async Task CancelledSavePreservesPreviousPreferences()
    {
        var store = new PreferencesStore(FilePath);
        await store.SaveAsync(new() { Appearance = Appearance.Dark });

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync(new(),
            new CancellationToken(canceled: true)));

        Assert.AreEqual(Appearance.Dark, (await store.LoadAsync()).Appearance);
        Assert.IsFalse(File.Exists(FilePath + ".tmp"));
    }

    [TestMethod]
    public async Task ProtocolRoundTripsPreferences()
    {
        using var stream = new MemoryStream();
        var request = new Request(Command.Save, Preferences: new() { Appearance = Appearance.Light });

        await LocalProtocol.WriteAsync(stream, request, CancellationToken.None);
        stream.Position = 0;

        Assert.AreEqual(request, await LocalProtocol.ReadAsync<Request>(stream, CancellationToken.None));
    }

    [TestMethod]
    public async Task ProtocolRejectsOversizedMessagesBeforeReadingThePayload()
    {
        using var stream = new MemoryStream([0, 0, 32, 0]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => LocalProtocol.ReadAsync<Request>(stream, CancellationToken.None));
    }

    private string FilePath => Path.Combine(_directory, "preferences.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
