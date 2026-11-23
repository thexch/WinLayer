using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using WinLayer.Core;
using Appearance = WinLayer.Core.Appearance;

namespace WinLayer.Settings;

internal sealed class SettingsWindow : Form
{
    private const string Host = "winlayer.local";
    private const string Entry = $"https://{Host}/index.html";
    private readonly CancellationTokenSource _closing = new();
    private readonly WebView2 _browser = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly StartupView _loading = new();
    private readonly System.Windows.Forms.Timer _startupDeadline = new() { Interval = 20_000 };
    private Task<Response?>? _openingStatus;
    private long _openingStarted;
    private long? _navigationRevision;
    private bool _startupFailed;
    private bool _receiving;
    private bool _closed;
    private Appearance _captionAppearance;
    private FormWindowState _lastVisibleState = FormWindowState.Maximized;

    public SettingsWindow()
    {
        Text = "WinLayer — Ouverture";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(640, 500);
        ClientSize = new Size(1100, 735);
        Controls.Add(_browser);
        Controls.Add(_loading);
        _loading.BringToFront();
        _startupDeadline.Tick += (_, _) => ShowError("Le démarrage des réglages a pris trop de temps. Fermez cette fenêtre et réessayez.");
        ApplyAppearance();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyAppearance();
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (IsHandleCreated && message.Msg is 0x001A or 0x031A)
            ApplyAppearance();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _loading.Start();
        _startupDeadline.Start();
        _openingStarted = Stopwatch.GetTimestamp();
        _openingStatus = ReadOpeningStatusAsync();
        try
        {
            var assets = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!File.Exists(Path.Combine(assets, "index.html")))
            {
                ShowError("Les fichiers des réglages sont manquants. Réinstallez WinLayer.");
                return;
            }

            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinLayerData", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: data,
                options: new CoreWebView2EnvironmentOptions { ExclusiveUserDataFolderAccess = true });
            if (_closed || _startupFailed)
                return;

            await _browser.EnsureCoreWebView2Async(environment);
            if (_closed || _startupFailed)
                return;

            if (_openingStatus is { IsCompletedSuccessfully: true } && _openingStatus.Result?.Status is { } initial)
            {
                _captionAppearance = initial.Preferences.Appearance;
                ApplyAppearance();
            }
            var core = _browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.NavigationStarting += (_, args) => args.Cancel = !IsEntry(args.Uri);
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess && args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    ShowError("Impossible de charger les réglages. Fermez cette fenêtre et réessayez.");
            };
            core.FrameNavigationStarting += (_, args) => args.Cancel = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.ProcessFailed += (_, args) =>
            {
                if (args.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or
                    CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                    ShowError("Les réglages ont été interrompus. Fermez cette fenêtre et réessayez.");
            };
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, args) =>
            {
                if ((!IsLocal(args.Request.Uri) && !(args.ResourceContext == CoreWebView2WebResourceContext.Image &&
                    args.Request.Uri.StartsWith("data:image/png;base64,", StringComparison.Ordinal))) || args.Request.Method != "GET")
                    args.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
            };
            core.WebMessageReceived += Receive;
            core.SetVirtualHostNameToFolderMapping(Host, assets, CoreWebView2HostResourceAccessKind.Deny);
            core.Navigate(Entry);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowError("Microsoft Edge WebView2 Runtime est requis. Installez le runtime Evergreen, puis relancez les réglages.");
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError(error.ToString());
            ShowError("Les réglages n’ont pas pu démarrer. Fermez cette fenêtre et réessayez.");
        }
    }

    private async Task<Response?> ReadOpeningStatusAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            return await LocalProtocol.SendAsync(new(Command.Status), timeout.Token);
        }
        catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException
            or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private async Task<Response> SendToAgentAsync(Request request, int id, CancellationToken cancellationToken)
    {
        if (request.Command == Command.Status)
        {
            var openingStatus = _openingStatus;
            _openingStatus = null;
            if (openingStatus is not null && request.Page == Page.Dashboard &&
                Stopwatch.GetElapsedTime(_openingStarted) < TimeSpan.FromSeconds(3) &&
                await openingStatus is { Status: not null } snapshot)
                return snapshot;
        }
        else
            _openingStatus = null;

        return await LocalProtocol.SendAsync(request, cancellationToken, request.ReportProgress
            ? value => { if (!_closed) Reply(id, new(Progress: value)); }
            : null, RequestsOverlay(request) ? GrantAgentFocus : null);
    }

    private void ApplyAppearance()
    {
        if (IsHandleCreated)
            CaptionTheme.Apply(Handle, _captionAppearance);
        _loading.ApplyTheme(_captionAppearance);
        BackColor = _loading.BackColor;
        if (!_closed)
            _browser.DefaultBackgroundColor = _loading.BackColor;
    }

    private static bool IsLocal(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host == Host && uri.IsDefaultPort && uri.UserInfo.Length == 0;

    private static bool IsEntry(string address) => string.Equals(address, Entry, StringComparison.Ordinal);

    private async void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_closed || !IsEntry(args.Source))
            return;

        var valid = SettingsBridge.TryRead(args.WebMessageAsJson, out var id, out var request);
        if (id == 0)
            return;
        if (!valid)
        {
            Reply(id, new(Error: "Requête de réglages invalide."));
            return;
        }
        if (request is null)
        {
            // React has mounted its local shell. Agent data can arrive after the shell becomes visible.
            if (!_startupFailed && !_browser.Visible)
            {
                _startupDeadline.Stop();
                _loading.Stop();
                _browser.Visible = true;
                _loading.Visible = false;
                Text = "WinLayer";
            }
            Reply(id, new());
            return;
        }
        if (_receiving)
        {
            Reply(id, new(Error: "Une action est déjà en cours."));
            return;
        }

        _receiving = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
            timeout.CancelAfter(request.Command switch
            {
                Command.InstallUpdate => TimeSpan.FromMinutes(5.5),
                Command.CheckUpdates => TimeSpan.FromMinutes(2),
                Command.Hub when request.Hub?.Action == HubAction.ExportMacro => TimeSpan.FromMinutes(2),
                _ => TimeSpan.FromSeconds(8)
            });

            Response response;
            try
            {
                response = await SendToAgentAsync(request, id, timeout.Token);
            }
            catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException
                or UnauthorizedAccessException or JsonException)
            {
                response = new(Error: "L’agent ne répond pas. WinLayer réessaie automatiquement.");
            }

            if (_closed)
                return;

            if (response.Status is { } status)
            {
                ActivateForNavigation(status.NavigationRevision);
                if (_captionAppearance != status.Preferences.Appearance)
                {
                    _captionAppearance = status.Preferences.Appearance;
                    ApplyAppearance();
                }
            }

            if (Reply(id, response) && request.Command == Command.InstallUpdate &&
                response is { Error: null, Status.Updates.State: UpdateState.Installing })
                Close();
        }
        finally
        {
            _receiving = false;
        }
    }

    private static bool RequestsOverlay(Request request) =>
        request.Utility is { Action: UtilityAction.Execute or UtilityAction.Export or UtilityAction.Import or UtilityAction.AddFile or UtilityAction.AddFolder } ||
        request.Hub is { Action: HubAction.Play } or
            { Action: HubAction.Open, Tool: HubTool.Switcher or HubTool.Picker or HubTool.Ruler or HubTool.Macro };

    private static void GrantAgentFocus(SafePipeHandle pipe)
    {
        // A button in the foreground WebView can explicitly pass foreground permission to its local agent.
        if (GetNamedPipeServerProcessId(pipe, out var processId) && processId > 0)
            AllowSetForegroundWindow(processId);
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);

    private bool Reply(int id, Response response)
    {
        try
        {
            if (_browser.CoreWebView2 is not { } browser)
                return false;

            string message;
            try
            {
                message = SettingsBridge.Write(id, response);
            }
            catch (JsonException error)
            {
                System.Diagnostics.Trace.TraceError(error.ToString());
                browser.PostWebMessageAsJson(SettingsBridge.Write(id,
                    new(Error: "Les réglages reçus n’ont pas pu être affichés.")));
                return false;
            }

            browser.PostWebMessageAsJson(message);
            return true;
        }
        catch (InvalidOperationException error)
        {
            System.Diagnostics.Trace.TraceError(error.ToString());
            return false;
        }
    }

    private void ActivateForNavigation(long revision)
    {
        if (_navigationRevision is { } previous && previous != revision)
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = _lastVisibleState;
            Activate();
        }

        _navigationRevision = revision;
    }

    private void ShowError(string message)
    {
        if (_closed)
            return;

        _startupFailed = true;
        _startupDeadline.Stop();
        Text = "WinLayer — Réglages indisponibles";
        _browser.Visible = false;
        _loading.ShowError(message);
        _loading.Visible = true;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized)
            _lastVisibleState = WindowState;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closed = true;
        _startupDeadline.Dispose();
        _loading.Stop();
        _closing.Cancel();
        _closing.Dispose();
        if (_browser.CoreWebView2 is { } core)
            core.WebMessageReceived -= Receive;
        _browser.Dispose();
        base.OnFormClosed(e);
    }
}
