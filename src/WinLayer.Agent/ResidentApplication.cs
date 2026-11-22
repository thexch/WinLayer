using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using Velopack;
using Velopack.Sources;
using WinLayer.Core;
using WinLayer.Modules;

namespace WinLayer.Agent;

internal sealed class ResidentApplication : ApplicationContext
{
    private readonly CancellationTokenSource _stop = new();
    private readonly PreferencesStore _store = new(Path.Combine(WindowsSettings.DataDirectory, "preferences.json"));
    private readonly SynchronizationContext _ui = SynchronizationContext.Current!;
    private readonly UpdateManager _updates = new(new GithubSource("https://github.com/thexch/WinLayer",
        accessToken: null, prerelease: false));
    private readonly SemaphoreSlim _updateGate = new(1);
    private readonly NotifyIcon _tray;
    private readonly Icon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _statusMenu = new() { Enabled = false };
    private readonly DeviceBatteryModule _battery;
    private readonly WindowsAutoClicker _clicker;
    private readonly Task _server;
    private readonly Task _autoCheck;
    private Process? _settings;
    private TaskbarBatteryStrip? _batteryTray;
    private Preferences _preferences = new();
    private BatteryStatus _batteryStatus = new([]);
    private UpdateStatus _updateStatus;
    private UpdateInfo? _available;
    private string? _notifiedVersion;
    private string? _error;
    private Page _notificationPage;
    private Page _requestedPage;
    private long _navigationRevision;
    private bool _quitting;

    public ResidentApplication(bool open, Page page, Stopwatch startup, bool measure)
    {
        _clicker = new(_ui);
        _updateStatus = _updates.IsInstalled ? new(UpdateState.NotChecked) : new(UpdateState.Unavailable);
        _battery = new([() => new WindowsBluetoothProvider(), () => new WindowsHyperXProvider()],
            status => _ui.Post(_ => SetBatteryStatus(status), null),
            device => _ui.Post(_ =>
            {
                if (!_quitting && _preferences.Battery is { Enabled: true, Alerts: true } options &&
                    device.Percentage <= options.LowThreshold)
                    Notify("Batterie faible", $"{device.Name} · {device.Percentage} %", Page.Battery);
            }, null),
            error => WindowsSettings.Log("Lecture Device Battery.", error));

        var iconResource = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinLayer.ico");
        using (iconResource)
            _icon = new Icon(iconResource!, SystemInformation.SmallIconSize);

        _menu.Items.Add("Ouvrir les paramètres", null, (_, _) => OpenSettings(Page.Dashboard));
        _menu.Items.Add("Device Battery", null, (_, _) => OpenSettings(Page.Battery));
        _menu.Items.Add("Auto Clicker", null, (_, _) => OpenSettings(Page.AutoClicker));
        var pauseClicker = new ToolStripMenuItem("Mettre Auto Clicker en pause", null, (_, _) => _clicker.Pause());
        _menu.Items.Add(pauseClicker);
        _menu.Opening += (_, _) => pauseClicker.Visible =
            _preferences.AutoClicker.Enabled && _clicker.Status.State != ClickState.Paused;
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_statusMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Quitter WinLayer", null, async (_, _) => await StopAsync());
        _menu.Opening += (_, _) => UpdateMenu();
        _tray = new NotifyIcon { Text = "WinLayer", Icon = _icon, ContextMenuStrip = _menu, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenSettings(Page.Dashboard);
        };
        _tray.BalloonTipClicked += (_, _) => OpenSettings(_notificationPage);
        if (measure)
            WindowsSettings.Log($"tray-ready-ms={startup.Elapsed.TotalMilliseconds:F1}");

        _server = ServeAsync();
        _autoCheck = CheckAutomaticallyAsync();
        if (open)
            OpenSettings(page);
    }

    private AgentStatus Snapshot() => new(_preferences, _batteryStatus, _updateStatus,
        _updates.CurrentVersion?.ToString() ??
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0",
        _error, _requestedPage, _navigationRevision, _clicker.Status);

    private async Task ServeAsync()
    {
        try
        {
            try
            {
                _preferences = await _store.LoadAsync(_stop.Token);
                WindowsSettings.SetAutoStart(_preferences.StartWithWindows);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                _error = "Les préférences ou le démarrage automatique n’ont pas pu être lus. Consultez le journal local.";
                WindowsSettings.Log("Initialisation des préférences.", error);
            }

            try
            {
                _clicker.Reserve(_preferences.AutoClicker);
                _clicker.Apply(_preferences.AutoClicker);
            }
            catch (InvalidOperationException error)
            {
                _clicker.RejectStartup(_preferences.AutoClicker, error.Message);
            }
            finally
            {
                _clicker.Prune();
            }

            await _battery.ApplyAsync(_preferences.Battery);
            while (!_stop.IsCancellationRequested)
            {
                Command? completed = null;
                var silentUpdate = false;
                using (var pipe = new NamedPipeServerStream(LocalProtocol.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
                {
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    var updateHandedOff = false;
                    try
                    {
                        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                        readTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                        var request = await LocalProtocol.ReadAsync<Request>(pipe, readTimeout.Token);
                        silentUpdate = request.Command == Command.InstallUpdate && request.ReportProgress;
                        var response = await HandleAsync(request, silentUpdate ? pipe : null);
                        updateHandedOff = request.Command == Command.InstallUpdate && response.Error is null;
                        using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                        writeTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                        await LocalProtocol.WriteAsync(pipe, response, writeTimeout.Token);
                        if (response.Error is null)
                            completed = request.Command;
                    }
                    catch (Exception error) when (!_stop.IsCancellationRequested)
                    {
                        // A client can close its window or time out while the agent is answering.
                        if (updateHandedOff)
                            _updateStatus = new(UpdateState.Available, _available?.TargetFullRelease.Version.ToString());
                        if (error is not (IOException or OperationCanceledException or JsonException))
                            WindowsSettings.Log("Requête locale.", error);
                    }
                }

                if (completed == Command.InstallUpdate)
                    _ = FinishUpdateAsync(silentUpdate);
                else if (completed == Command.Quit)
                    _ = StopAsync();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _error = "La communication locale a été interrompue. Quittez puis relancez WinLayer.";
            WindowsSettings.Log("Canal de communication de l’agent.", error);
            Notify("WinLayer", _error, Page.General);
        }
    }

    private async Task<Response> HandleAsync(Request request, Stream? progressStream)
    {
        if (_quitting)
            return new(Error: "WinLayer est en cours d’arrêt.");

        try
        {
            switch (request.Command)
            {
                case Command.Status:
                case Command.Quit:
                    break;
                case Command.OpenSettings when Enum.IsDefined(request.Page):
                    OpenSettings(request.Page);
                    break;
                case Command.Save when request.Preferences is not null:
                    await SaveAsync(request.Preferences.Normalize() with { AutoClicker = _preferences.AutoClicker });
                    break;
                case Command.SaveAutoClicker when request.ClickerOptions is { } clicker && clicker.IsValid():
                    return await SaveClickerAsync(clicker);
                case Command.PauseAutoClicker:
                    _clicker.Pause();
                    break;
                case Command.RearmAutoClicker:
                    try
                    {
                        _clicker.Rearm();
                    }
                    catch (InvalidOperationException error)
                    {
                        return new(Snapshot(), error.Message);
                    }
                    break;
                case Command.RefreshBattery:
                    if (!_battery.RequestRefresh())
                        return new(Snapshot(), "Activez Device Battery avant l’actualisation.");
                    break;
                case Command.CheckUpdates:
                    await CheckUpdatesAsync();
                    break;
                case Command.InstallUpdate when _available is not null &&
                    _updateStatus.State == UpdateState.Available &&
                    _available.TargetFullRelease.Version.ToString() == request.Version:
                    await DownloadAsync(request.Version, progressStream);
                    break;
                default:
                    return new(Snapshot(), "Cette action est indisponible. Actualisez les réglages et réessayez.");
            }

            return new(Snapshot());
        }
        catch (Exception error) when (!_stop.IsCancellationRequested)
        {
            WindowsSettings.Log($"Action {request.Command}.", error);
            return new(Snapshot(), request.Command == Command.InstallUpdate
                ? "Le téléchargement de la mise à jour a échoué. Vous pouvez le relancer."
                : "L’action a échoué. Consultez le journal local.");
        }
    }

    private async Task<Response> SaveClickerAsync(AutoClickerOptions options)
    {
        try
        {
            try
            {
                _clicker.Reserve(options);
            }
            catch (InvalidOperationException error)
            {
                return new(Snapshot(), error.Message);
            }

            var preferences = _preferences with { AutoClicker = options };
            await _store.SaveAsync(preferences, _stop.Token);
            if (!_quitting)
            {
                _preferences = preferences;
                _clicker.Apply(options);
            }
            return new(Snapshot());
        }
        finally
        {
            _clicker.Prune();
        }
    }

    private async Task SaveAsync(Preferences preferences)
    {
        var startupChanged = preferences.StartWithWindows != _preferences.StartWithWindows;
        if (startupChanged || _error is not null)
            WindowsSettings.SetAutoStart(preferences.StartWithWindows);
        try
        {
            await _store.SaveAsync(preferences, _stop.Token);
        }
        catch
        {
            if (startupChanged)
                WindowsSettings.SetAutoStart(_preferences.StartWithWindows);
            throw;
        }

        _preferences = preferences;
        _error = null;
        await _battery.ApplyAsync(preferences.Battery);
        UpdateBatteryTray();
    }

    private async Task CheckAutomaticallyAsync()
    {
        if (!_updates.IsInstalled)
            return;
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), _stop.Token);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
            do
            {
                if (_preferences.AutomaticUpdateChecks)
                    await CheckUpdatesAsync();
            }
            while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private async Task CheckUpdatesAsync()
    {
        if (!_updates.IsInstalled)
            return;

        await _updateGate.WaitAsync(_stop.Token);
        try
        {
            if (_updateStatus.State == UpdateState.Installing)
                return;

            _available = await _updates.CheckForUpdatesAsync().WaitAsync(_stop.Token);
            if (_available is null)
            {
                _updateStatus = new(UpdateState.Current);
                return;
            }

            var target = _available.TargetFullRelease;
            if (target.Version.IsPrerelease || string.IsNullOrWhiteSpace(target.SHA256))
                throw new InvalidDataException("A stable update with a SHA-256 checksum is required.");

            var version = target.Version.ToString();
            _updateStatus = new(UpdateState.Available, version);
            if (_notifiedVersion != version)
            {
                _notifiedVersion = version;
                Notify("Mise à jour disponible", $"WinLayer {version} est disponible.", Page.Updates);
            }
        }
        catch (Exception error) when (!_stop.IsCancellationRequested)
        {
            _available = null;
            _updateStatus = new(UpdateState.Error, Info: "GitHub Releases est inaccessible ou la version proposée est invalide.");
            WindowsSettings.Log("Recherche de mises à jour.", error);
        }
        finally
        {
            _updateGate.Release();
        }
    }

    private async Task DownloadAsync(string version, Stream? progressStream)
    {
        await _updateGate.WaitAsync(_stop.Token);
        try
        {
            var update = _available;
            if (update?.TargetFullRelease.Version.ToString() != version || _updateStatus.State != UpdateState.Available)
                throw new InvalidOperationException("The confirmed update is no longer available.");

            _updateStatus = new(UpdateState.Installing, version);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                await DownloadWithProgressAsync(update, progressStream, timeout.Token);
            }
            catch
            {
                _updateStatus = new(UpdateState.Available, version);
                throw;
            }
        }
        finally
        {
            _updateGate.Release();
        }
    }

    private async Task DownloadWithProgressAsync(UpdateInfo update, Stream? stream, CancellationToken cancellationToken)
    {
        if (stream is null)
        {
            await _updates.DownloadUpdatesAsync(update, cancelToken: cancellationToken);
            return;
        }

        var progress = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        var relay = RelayProgressAsync(stream, progress.Reader, cancellationToken);
        try
        {
            await _updates.DownloadUpdatesAsync(update, value => progress.Writer.TryWrite(value), cancellationToken);
        }
        finally
        {
            progress.Writer.TryComplete();
            await relay;
        }
    }

    private static async Task RelayProgressAsync(Stream stream, ChannelReader<int> progress, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var value in progress.ReadAllAsync(cancellationToken))
            {
                using var replyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                replyTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                await LocalProtocol.WriteAsync(stream, new Response(Progress: value), replyTimeout.Token);
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            // Closing the settings window disconnects the client. The final reply controls handoff.
        }
    }

    private async Task FinishUpdateAsync(bool silent)
    {
        var target = _available!.TargetFullRelease;
        try
        {
            if (_settings is { HasExited: false })
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await _settings.WaitForExitAsync(timeout.Token);
            }

            if (_quitting)
                return;

            _updates.WaitExitThenApplyUpdates(target, silent: silent, restart: true);
            await StopAsync();
        }
        catch (OperationCanceledException) when (_quitting)
        {
        }
        catch (Exception error)
        {
            _updateStatus = new(UpdateState.Available, target.Version.ToString(), "L’installation a été interrompue.");
            WindowsSettings.Log("Installation de la mise à jour.", error);
            Notify("Installation interrompue", "Ouvrez les réglages pour réessayer.", Page.Updates);
        }
    }

    private void OpenSettings(Page page)
    {
        if (_quitting || _updateStatus.State == UpdateState.Installing)
            return;

        try
        {
            _requestedPage = page;
            _navigationRevision++;
            if (_settings is { HasExited: false })
                return;

            _settings?.Dispose();
            var path = Environment.GetEnvironmentVariable("WINLAYER_SETTINGS_EXE") ??
                Path.Combine(AppContext.BaseDirectory, "WinLayer.Settings.exe");
            var start = new ProcessStartInfo(path) { UseShellExecute = false };
            start.ArgumentList.Add(page.ToString());
            _settings = Process.Start(start);
        }
        catch (Exception error)
        {
            WindowsSettings.Log("Ouverture des réglages.", error);
            Notify("WinLayer", "Ouverture des réglages impossible. Consultez le journal local.", Page.General);
        }
    }

    private void SetBatteryStatus(BatteryStatus status)
    {
        if (_quitting || (!_preferences.Battery.Enabled && status.UpdatedAt is not null))
            return;
        _batteryStatus = status;
        UpdateBatteryTray();
        UpdateMenu();
    }

    private void UpdateBatteryTray()
    {
        var devices = _preferences.Battery.Enabled && _preferences.BatteryTrayIcons
            ? _batteryStatus.Devices.Select(BatteryTrayDisplay.FromDevice)
                .OfType<BatteryTrayDisplay>().ToArray()
            : [];
        if (devices.Length == 0)
        {
            _batteryTray?.Dispose();
            _batteryTray = null;
            return;
        }

        _batteryTray ??= new TaskbarBatteryStrip(() => OpenSettings(Page.Battery));
        _batteryTray.Update(devices);
    }

    private void UpdateMenu()
    {
        var count = _batteryStatus.Devices.Count(device => device.State == BatteryState.Available);
        var batterySummary = !_preferences.Battery.Enabled ? "Device Battery en veille" :
            _batteryStatus.Error is not null ? "Lecture de batterie indisponible" :
            $"{count} batterie(s) lisible(s)";
        _statusMenu.Text = $"Agent actif · {batterySummary}";
        _tray.Text = $"WinLayer · {batterySummary}";
    }

    private void Notify(string title, string message, Page page)
    {
        _notificationPage = page;
        _tray.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
    }

    private async Task StopAsync()
    {
        if (_quitting)
            return;

        _quitting = true;
        _clicker.Dispose();
        _tray.Visible = false;
        _batteryTray?.Dispose();
        _batteryTray = null;
        try
        {
            await _stop.CancelAsync();
            if (_settings is { HasExited: false })
                _settings.CloseMainWindow();
            await Task.WhenAll(_server, _autoCheck);
        }
        catch (Exception error)
        {
            WindowsSettings.Log("Arrêt de l’agent.", error);
        }
        finally
        {
            await _battery.DisposeAsync();
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _batteryTray?.Dispose();
            _clicker.Dispose();
            _tray.Dispose();
            _menu.Dispose();
            _icon.Dispose();
            _settings?.Dispose();
            _updateGate.Dispose();
            _stop.Dispose();
        }
        base.Dispose(disposing);
    }
}
