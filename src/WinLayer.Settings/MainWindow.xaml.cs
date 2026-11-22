using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinLayer.Core;
using Page = WinLayer.Core.Page;

namespace WinLayer.Settings;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _closing = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim _requests = new(1);
    private AgentStatus? _status;
    private bool _loadingStatus;
    private IReadOnlyList<BatteryDevice> _devices = [];
    private bool _busy;
    private bool _connected;
    private bool _closed;
    private bool _receivedPreferences;
    private string? _localFeedback;
    private DateTimeOffset? _refreshRequested;

    public MainWindow(Page page)
    {
        InitializeComponent();
        SystemTheme.IsChecked = true;
        SetTheme(Appearance.System);
        Navigation.SelectedIndex = (int)page;
        RenderAvailability();
        _timer.Tick += async (_, _) => await FetchStatusAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await FetchStatusAsync();
        };
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _timer.Stop();
        SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        _closing.Cancel();
        _closing.Dispose();
        base.OnClosed(e);
    }

    private async Task FetchStatusAsync()
    {
        if (_busy || _loadingStatus || _closed)
            return;

        _loadingStatus = true;
        try
        {
            await SendAsync(new(Command.Status));
        }
        finally
        {
            _loadingStatus = false;
        }
    }

    private async Task<Response?> SendAsync(Request request, string? workingMessage = null)
    {
        var isCommand = request.Command != Command.Status;
        if (isCommand)
        {
            _busy = true;
            _timer.Stop();
            SetFeedback(workingMessage);
            RenderAvailability();
        }
        var timeout = request.Command switch
        {
            Command.InstallUpdate => TimeSpan.FromMinutes(5.5),
            Command.CheckUpdates => TimeSpan.FromMinutes(2),
            _ => TimeSpan.FromSeconds(8)
        };

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
            deadline.CancelAfter(timeout);
            await _requests.WaitAsync(deadline.Token);
            Response response;
            try
            {
                response = await LocalProtocol.SendAsync(request, deadline.Token);
            }
            finally
            {
                _requests.Release();
            }
            if (_closed)
                return null;
            _connected = response.Status is not null;
            if (response.Status is { } status)
                Render(status, request.Command == Command.Save && response.Error is null);
            if (response.Error is not null)
                SetFeedback(response.Error);
            return response;
        }
        catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException
            or System.Text.Json.JsonException)
        {
            if (!_closed)
            {
                _connected = false;
                SetFeedback("L’agent ne répond pas. WinLayer réessaie automatiquement.");
            }
            return null;
        }
        finally
        {
            if (isCommand)
                _busy = false;
            if (!_closed)
            {
                RenderAvailability();
                if (isCommand)
                    _timer.Start();
            }
        }
    }

    private void Render(AgentStatus status, bool saved)
    {
        var previous = _status;
        _status = status;
        SetAgentPresence("Agent connecté");
        InstalledVersion.Text = $"Version {status.Version}";
        if (!_receivedPreferences || saved)
        {
            var preferences = status.Preferences;
            BatteryEnabled.IsChecked = preferences.Battery.Enabled;
            BatteryAlerts.IsChecked = preferences.Battery.Alerts;
            Interval.Value = preferences.Battery.RefreshMinutes;
            Threshold.Value = preferences.Battery.LowThreshold;
            AutoStart.IsChecked = preferences.StartWithWindows;
            AutoUpdates.IsChecked = preferences.AutomaticUpdateChecks;
            SystemTheme.IsChecked = preferences.Appearance == Appearance.System;
            LightTheme.IsChecked = preferences.Appearance == Appearance.Light;
            DarkTheme.IsChecked = preferences.Appearance == Appearance.Dark;
            SetTheme(preferences.Appearance);
            _receivedPreferences = true;
        }

        if (previous is null || previous.NavigationRevision != status.NavigationRevision)
        {
            Navigation.SelectedIndex = (int)status.RequestedPage;
            if (previous is not null)
            {
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                Activate();
            }
        }

        if (status.Error is not null)
            SetFeedback(status.Error);
        else if (previous?.Error is not null || _localFeedback == "L’agent ne répond pas. WinLayer réessaie automatiquement.")
            SetFeedback(null);

        var devices = status.Battery.Devices;
        var known = devices.Count(device => device.State == BatteryState.Available);
        BatterySummary.Text = !status.Preferences.Battery.Enabled ? "Module désactivé" :
            status.Battery.Error is not null ? "Bluetooth indisponible" :
            $"{known} niveau(x) disponible(s) sur {devices.Count} périphérique(s)";
        BatteryReadStatus.Text = DescribeBattery(status);
        if (!_devices.SequenceEqual(devices))
        {
            _devices = devices;
            Devices.ItemsSource = devices.Select(device => new DeviceRow(device.Name, device.State switch
            {
                BatteryState.Available => "Batterie lue via Bluetooth LE",
                BatteryState.Disconnected => "Déconnecté",
                BatteryState.Unsupported => "Service de batterie non exposé",
                _ => "Niveau inconnu"
            }, device.Percentage is int value ? $"{value} %" : "—")).ToArray();
        }

        if (_refreshRequested is { } requested && status.Battery.UpdatedAt >= requested)
        {
            _refreshRequested = null;
            SetFeedback(status.Battery.Error ?? "Liste actualisée.");
        }

        RenderUpdate(status.Updates);
    }

    private static string DescribeBattery(AgentStatus status)
    {
        if (!status.Preferences.Battery.Enabled)
            return "Activez le module pour afficher les périphériques.";
        if (status.Battery.Error is not null)
            return status.Battery.Error;
        if (status.Battery.UpdatedAt is not { } updated)
            return "Recherche des périphériques…";

        var updatedLabel = $"Dernière lecture à {updated.ToLocalTime():HH:mm:ss}.";
        return status.Battery.Devices.Count == 0
            ? $"Aucun périphérique Bluetooth LE associé. {updatedLabel}"
            : updatedLabel;
    }

    private void RenderUpdate(UpdateStatus updates)
    {
        (UpdateHeading.Text, UpdateDetail.Text) = updates.State switch
        {
            UpdateState.Unavailable => ("Installation Velopack requise", "Les versions de développement se mettent à jour par compilation."),
            UpdateState.NotChecked => ("Vérification en attente", "Recherchez la dernière version stable disponible."),
            UpdateState.Current => ("WinLayer est à jour", "La version installée est la plus récente disponible."),
            UpdateState.Available => ($"WinLayer {updates.Version} est disponible", updates.Info ?? "L’agent redémarrera après l’installation."),
            UpdateState.Installing => ($"Installation de WinLayer {updates.Version}", "WinLayer prépare le redémarrage."),
            _ => ("Vérification indisponible", updates.Info ?? "Réessayez dans quelques instants.")
        };

        InstallButton.Visibility = updates.State == UpdateState.Available ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderAvailability()
    {
        if (!_connected && !_busy)
            SetAgentPresence("Agent indisponible");
        var canEdit = _connected && !_busy;
        BatteryEnabled.IsEnabled = canEdit;
        BatteryOptionsPanel.IsEnabled = canEdit && BatteryEnabled.IsChecked == true;
        RefreshButton.IsEnabled = canEdit && _status?.Preferences.Battery.Enabled == true;
        GeneralOptions.IsEnabled = canEdit;
        CheckUpdatesButton.IsEnabled = canEdit && _status?.Updates.State is not (UpdateState.Unavailable or UpdateState.Installing);
        InstallButton.IsEnabled = canEdit;
    }

    private void SetAgentPresence(string text)
    {
        if (AgentPresence.Text == text)
            return;
        AgentPresence.Text = text;
        UIElementAutomationPeer.FromElement(AgentPresence)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void SetFeedback(string? text)
    {
        _localFeedback = text;
        Feedback.Text = text ?? " ";
        UIElementAutomationPeer.FromElement(Feedback)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void Navigate(object sender, SelectionChangedEventArgs e)
    {
        if (DashboardPage is null || e.Source != Navigation)
            return;

        DashboardPage.Visibility = Navigation.SelectedIndex == (int)Page.Dashboard ? Visibility.Visible : Visibility.Collapsed;
        BatteryPage.Visibility = Navigation.SelectedIndex == (int)Page.Battery ? Visibility.Visible : Visibility.Collapsed;
        GeneralPage.Visibility = Navigation.SelectedIndex == (int)Page.General ? Visibility.Visible : Visibility.Collapsed;
        UpdatesPage.Visibility = Navigation.SelectedIndex == (int)Page.Updates ? Visibility.Visible : Visibility.Collapsed;
        PageScroll.ScrollToTop();
    }

    private void OpenBattery(object sender, RoutedEventArgs e) => Navigation.SelectedIndex = (int)Page.Battery;
    private void OpenGeneral(object sender, RoutedEventArgs e) => Navigation.SelectedIndex = (int)Page.General;

    private async void ToggleBattery(object sender, RoutedEventArgs e)
    {
        var saved = await SaveBatteryOptionsAsync();
        if (!saved && !_closed)
        {
            BatteryEnabled.IsChecked = _status?.Preferences.Battery.Enabled;
            RenderAvailability();
        }
    }

    private async void SaveBattery(object sender, RoutedEventArgs e) => await SaveBatteryOptionsAsync();

    private async Task<bool> SaveBatteryOptionsAsync()
    {
        if (_status is not { } status || _busy)
            return false;

        var preferences = status.Preferences with
        {
            Battery = status.Preferences.Battery with
            {
                Enabled = BatteryEnabled.IsChecked == true,
                Alerts = BatteryAlerts.IsChecked == true,
                RefreshMinutes = (int)Interval.Value,
                LowThreshold = (int)Threshold.Value
            }
        };
        return await SavePreferencesAsync(preferences);
    }

    private async void SaveGeneral(object sender, RoutedEventArgs e)
    {
        if (_status is not { } status || _busy)
            return;

        await SavePreferencesAsync(status.Preferences with
        {
            Appearance = SelectedAppearance,
            StartWithWindows = AutoStart.IsChecked == true,
            AutomaticUpdateChecks = AutoUpdates.IsChecked == true
        });
    }

    private async Task<bool> SavePreferencesAsync(Preferences preferences)
    {
        var response = await SendAsync(new(Command.Save, Preferences: preferences), "Enregistrement des préférences…");
        if (response is not { Error: null, Status: not null } || _closed)
            return false;

        SetFeedback("Préférences enregistrées.");
        return true;
    }

    private async void Refresh(object sender, RoutedEventArgs e)
    {
        _refreshRequested = DateTimeOffset.Now;
        var response = await SendAsync(new(Command.RefreshBattery), "Actualisation demandée…");
        if (response is not { Error: null, Status: not null })
            _refreshRequested = null;
    }

    private async void CheckUpdates(object sender, RoutedEventArgs e)
    {
        if (await SendAsync(new(Command.CheckUpdates), "Recherche de mises à jour…") is { Error: null } && !_closed)
            SetFeedback(null);
    }

    private async void InstallUpdate(object sender, RoutedEventArgs e)
    {
        if (_status?.Updates.Version is not { } version)
            return;

        var confirmation = MessageBox.Show(this, $"Télécharger et installer WinLayer {version} ? Les réglages se fermeront et WinLayer redémarrera.",
            "Installer la mise à jour", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes || _busy)
            return;

        if (await SendAsync(new(Command.InstallUpdate, Version: version), $"Téléchargement de WinLayer {version}…")
            is { Error: null, Status.Updates.State: UpdateState.Installing } && !_closed)
            Close();
    }

    private Appearance SelectedAppearance => LightTheme?.IsChecked == true ? Appearance.Light :
        DarkTheme?.IsChecked == true ? Appearance.Dark : Appearance.System;

    private void ChangeTheme(object sender, RoutedEventArgs e) => SetTheme(SelectedAppearance);

    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (!_closed)
                SetTheme(SelectedAppearance);
        });
    }

    private static void SetTheme(Appearance appearance)
    {
        var dark = appearance == Appearance.Dark || (appearance == Appearance.System && IsSystemDark());
        string[] keys = ["Surface", "Sidebar", "Card", "Line", "Ink", "Muted", "Accent", "AccentInk", "AccentSoft"];
        if (SystemParameters.HighContrast)
        {
            Color[] system = [SystemColors.WindowColor, SystemColors.WindowColor, SystemColors.WindowColor, SystemColors.WindowTextColor,
                SystemColors.WindowTextColor, SystemColors.WindowTextColor, SystemColors.HighlightColor, SystemColors.HighlightTextColor,
                SystemColors.ControlColor];
            for (int i = 0; i < keys.Length; i++)
                Application.Current.Resources[keys[i]] = new SolidColorBrush(system[i]);
            return;
        }

        uint[] colors = dark
            ? [0x131B22, 0x19242D, 0x1E2A34, 0x3B4D5B, 0xF0F5F8, 0xB3C2CD, 0x74D9BA, 0x10271F, 0x263F40]
            : [0xF4F7F8, 0xEDF3F4, 0xFFFFFF, 0xCBD8DD, 0x1C2A35, 0x485D6A, 0x176B59, 0xFFFFFF, 0xD5EBE6];
        for (int i = 0; i < keys.Length; i++)
        {
            var color = colors[i];
            Application.Current.Resources[keys[i]] =
                new SolidColorBrush(Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color));
        }
    }

    private static bool IsSystemDark()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public sealed record DeviceRow(string Name, string Detail, string Level);
}
