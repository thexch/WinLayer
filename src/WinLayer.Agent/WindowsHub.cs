using WinLayer.Core;
using WinLayer.Modules;

namespace WinLayer.Agent;

internal sealed class WindowsHub : IDisposable
{
    private readonly GlobalHotkeys _hotkeys;
    private readonly SynchronizationContext _ui;
    private readonly WindowsAutoClicker _repeat;
    private readonly MacroSession _macros;
    private readonly HubMonitoring _monitor = new();
    private readonly System.Windows.Forms.Timer _placement = new() { Interval = 1000 };
    private readonly Dictionary<HubTool, string> _errors = [];
    private readonly Dictionary<HubTool, object> _owners = Enum.GetValues<HubTool>().ToDictionary(tool => tool, _ => new object());
    private readonly Dictionary<long, (HubWindow Window, DecorativeOverlay? Border)> _pins = [];
    private readonly Func<string, Task> _saveColor;
    private HubPreferences _preferences = new();
    private HubOverlay? _overlay;
    private HubTool? _overlayTool;
    private DecorativeOverlay? _taskbar;
    private string? _color;
    private string? _measurement;
    private bool _disposed;

    public WindowsHub(GlobalHotkeys hotkeys, SynchronizationContext ui, Func<string, Task> saveColor)
    {
        _hotkeys = hotkeys;
        _ui = ui;
        _saveColor = saveColor;
        _repeat = new(ui, hotkeys, () => _preferences.Get(HubTool.Repeater));
        _macros = new(message => _errors[HubTool.Macro] = message, ui);
        _placement.Tick += (_, _) => PlaceDecorations();
    }

    public HubStatus Snapshot() => new(new(_errors), _monitor.Status, _pins.Values.Select(value => value.Window).ToArray(),
        _color, _measurement, _macros.State, _macros.Steps, _repeat.Status,
        _color is null ? null : HubCalculations.FormatColor(_color, ColorFormat.Rgb),
        _color is null ? null : HubCalculations.FormatColor(_color, ColorFormat.Hsl));

    public void Initialize(HubPreferences preferences)
    {
        _preferences = new() { Macros = preferences.Macros, Colors = preferences.Colors, Favorites = preferences.Favorites };
        foreach (var (tool, options) in preferences.Tools)
        {
            try { Reserve(tool, options); Apply(tool, preferences); }
            catch (Exception error)
            {
                if (_errors.Count < 9)
                    _errors[tool] = $"Réactivation impossible · {error.Message}";
                if (tool == HubTool.Repeater)
                    _repeat.RejectStartup(options.Repeat with { Enabled = options.Enabled }, error.Message);
                WindowsSettings.Log($"Module {tool}.", error);
            }
            finally { Retain(tool); }
        }
        _preferences = preferences;
        // A failed reservation cannot leave an active automation with only one shortcut.
        foreach (var (tool, _) in preferences.Tools)
            Retain(tool);
    }

    public void Reserve(HubTool tool, HubOptions options)
    {
        if (!options.Enabled)
            return;

        if (tool == HubTool.Repeater)
        {
            var keys = new ClickShortcut(options.OutputKey, options.OutputModifiers);
            if (keys == options.Repeat.Shortcut || keys == options.Repeat.Emergency || _hotkeys.IsRegistered(keys))
                throw new InvalidOperationException("La touche à répéter est réservée à un raccourci.");
            _repeat.Reserve(options.Repeat with { Enabled = true });
        }
        else if (HasShortcut(tool))
        {
            var repeat = _preferences.Get(HubTool.Repeater);
            if (repeat.Enabled && new ClickShortcut(repeat.OutputKey, repeat.OutputModifiers) == options.Shortcut)
                throw new InvalidOperationException("Cette combinaison est utilisée comme sortie de Key Repeater.");
            if (tool == HubTool.Macro && options.Shortcut == options.Repeat.Emergency)
                throw new InvalidOperationException("Les raccourcis de lecture et d’arrêt doivent être différents.");
            _hotkeys.Reserve(_owners[tool], options.Shortcut, shortcut => Dispatch(tool, shortcut));
            if (tool == HubTool.Macro)
                _hotkeys.Reserve(_owners[tool], options.Repeat.Emergency, shortcut => Dispatch(tool, shortcut));
        }
    }

    public void Retain(HubTool tool)
    {
        if (tool == HubTool.Repeater)
        {
            _repeat.Prune();
            return;
        }
        if (!HasShortcut(tool))
            return;
        var options = _preferences.Get(tool);
        var owner = _owners[tool];
        if (options.Enabled && _hotkeys.Owns(owner, options.Shortcut) &&
            (tool != HubTool.Macro || _hotkeys.Owns(owner, options.Repeat.Emergency)))
            _hotkeys.Retain(owner, tool == HubTool.Macro ? [options.Shortcut, options.Repeat.Emergency] : [options.Shortcut]);
        else
            _hotkeys.Retain(owner);
    }

    public void Apply(HubTool tool, HubPreferences preferences)
    {
        if (_disposed)
            return;
        var options = preferences.Get(tool);
        _preferences = preferences;
        _errors.Remove(tool);
        if (!options.Enabled && _overlayTool == tool)
            _overlay?.Close();
        switch (tool)
        {
            case HubTool.Repeater:
                _repeat.Stop();
                _repeat.Apply(options.Repeat with { Enabled = options.Enabled });
                break;
            case HubTool.Macro:
                _macros.Stop();
                if (_overlayTool == tool)
                    _overlay?.Close();
                break;
            case HubTool.Taskbar:
                _taskbar?.Dispose();
                _taskbar = null;
                if (options.Enabled)
                    _taskbar = new(ColorTranslator.FromHtml(options.Color), options.Opacity);
                SetPlacement();
                break;
            case HubTool.Pin:
                _ruleSuppressed.Clear();
                if (!options.Enabled)
                    UnpinAll();
                else
                {
                    foreach (var old in _pins.Values.ToArray())
                    {
                        old.Border?.Dispose();
                        if (HubWindows.Excluded(old.Window.App, options.Exclusions))
                            Unpin(old.Window.Handle);
                        else
                            _pins[old.Window.Handle] = (old.Window, MakeBorder(options));
                    }
                }
                SetPlacement();
                break;
            case HubTool.Network:
            case HubTool.System:
                _monitor.Configure(preferences.Get(HubTool.Network), preferences.Get(HubTool.System));
                break;
        }
    }

    public void UpdateLibrary(HubPreferences preferences) => _preferences = preferences;

    private void Dispatch(HubTool tool, ClickShortcut pressed)
    {
        try
        {
            _errors.Remove(tool);
            OnHotkey(tool, pressed);
        }
        catch (Exception error)
        {
            _errors[tool] = error.Message;
            WindowsSettings.Log($"Raccourci {tool}.", error);
        }
    }

    private void OnHotkey(HubTool tool, ClickShortcut pressed)
    {
        var options = _preferences.Get(tool);
        if (!options.Enabled)
            return;
        if (tool == HubTool.Macro && pressed == options.Repeat.Emergency)
        {
            _macros.Stop();
            if (_overlayTool == HubTool.Macro)
                _overlay?.Close();
        }
        else if (pressed == options.Shortcut)
        {
            if (tool == HubTool.Macro)
            {
                if (_macros.State is not null)
                    _macros.Stop();
                else if (FindMacro(options.SelectedMacro) is { } macro && HubWindows.Describe(HubWindows.Active) is { } target)
                    Play(target, macro);
                else
                    _errors[tool] = "Choisissez une macro et placez une fenêtre cible au premier plan.";
            }
            else
                Open(tool);
        }
    }

    public void SettingsView(Page page) => _monitor.SetView(page);

    public async Task RunAsync(HubRequest request)
    {
        var tool = request.Tool;
        var options = _preferences.Get(tool);
        if (!options.Enabled && request.Action != HubAction.PinAllOff)
            throw new InvalidOperationException("Activez d’abord cet outil.");
        _errors.Remove(tool);
        switch (request.Action)
        {
            case HubAction.CopyColor:
                Clipboard.SetText(HubCalculations.FormatColor(request.Value!, options.ColorFormat));
                break;
            case HubAction.ExportMacro:
                using (var dialog = new SaveFileDialog
                {
                    AddExtension = true, DefaultExt = "json", Filter = "Macro WinLayer (*.json)|*.json",
                    Title = "Exporter une macro WinLayer"
                })
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                        File.WriteAllText(dialog.FileName, SettingsBridge.WriteMacro(request.Macro!));
                }
                break;
            case HubAction.Open:
                Open(tool);
                break;
            case HubAction.Stop:
                if (tool == HubTool.Macro) _macros.Stop();
                if (tool == HubTool.Repeater) _repeat.Pause();
                if (_overlayTool == tool) _overlay?.Close();
                break;
            case HubAction.Rearm:
                _repeat.Rearm();
                break;
            case HubAction.Play:
                var macro = FindMacro(request.Value!) ?? throw new InvalidOperationException("La macro n’existe plus.");
                ShowOverlay(tool, new WindowSwitcher(options, message => _errors[tool] = message, window => Play(window, macro)));
                break;
            case HubAction.PauseRecording:
                _macros.Pause();
                break;
            case HubAction.Processes:
                await _monitor.RefreshProcessesAsync();
                break;
            case HubAction.EndProcess:
                _monitor.EndProcess(request.Pid, request.Started);
                await _monitor.RefreshProcessesAsync();
                break;
            case HubAction.PinAllOff:
                _ruleSuppressed.UnionWith(_pins.Keys);
                UnpinAll();
                break;
        }
    }

    private MacroPreset? FindMacro(string name) => _preferences.Macros.FirstOrDefault(macro =>
        string.Equals(macro.Name, name, StringComparison.OrdinalIgnoreCase));

    private void Play(HubWindow target, MacroPreset macro)
    {
        var options = _preferences.Get(HubTool.Macro);
        if (HubWindows.Excluded(target.App, options.Exclusions))
        {
            _errors[HubTool.Macro] = "Cette application est exclue.";
            return;
        }
        if (macro.Steps.Any(step => step.Kind == MacroKind.Key && _hotkeys.IsRegistered(new(step.Key, step.Modifiers))))
        {
            _errors[HubTool.Macro] = "Une étape utilise un raccourci réservé par WinLayer.";
            return;
        }
        _repeat.Stop();
        _macros.Play(target, macro, options);
    }

    private void Open(HubTool tool)
    {
        var options = _preferences.Get(tool);
        switch (tool)
        {
            case HubTool.Switcher:
                ShowOverlay(tool, new WindowSwitcher(options, message => _errors[tool] = message));
                break;
            case HubTool.Picker:
            case HubTool.Ruler:
                _overlay?.Close();
                ShowOverlay(tool, new ScreenOverlay(options, tool == HubTool.Ruler,
                    value =>
                    {
                        if (tool == HubTool.Ruler)
                            _measurement = value;
                        else
                        {
                            _color = value;
                            _ui.Post(async _ =>
                            {
                                try { await _saveColor(value); }
                                catch (Exception error) { WindowsSettings.Log("Historique des couleurs.", error); }
                            }, null);
                        }
                    }));
                break;
            case HubTool.Macro:
                _repeat.Stop();
                _macros.Record();
                break;
            case HubTool.Pin:
                if (HubWindows.Describe(HubWindows.Active) is { } active)
                    TogglePin(active, options);
                else
                    _errors[tool] = "Placez d’abord la fenêtre à épingler au premier plan.";
                break;
        }
    }

    private void ShowOverlay(HubTool tool, HubOverlay overlay)
    {
        _overlay?.Close();
        _overlay = overlay;
        _overlayTool = tool;
        overlay.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_overlay, overlay))
            {
                _overlay = null;
                _overlayTool = null;
            }
            overlay.Dispose();
        };
        overlay.Show();
        overlay.Activate();
    }

    private void TogglePin(HubWindow window, HubOptions options)
    {
        if (_pins.ContainsKey(window.Handle))
        {
            _ruleSuppressed.Add(window.Handle);
            Unpin(window.Handle);
            SetPlacement();
            return;
        }
        if (HubWindows.Excluded(window.App, options.Exclusions))
        {
            _errors[HubTool.Pin] = "Cette application est exclue.";
            return;
        }
        _ruleSuppressed.Remove(window.Handle);
        Pin(window, options);
        SetPlacement();
    }

    private void Pin(HubWindow window, HubOptions options)
    {
        if (_pins.Count >= 40)
            return;
        if (HubWindows.Topmost(window.Handle))
        {
            _errors[HubTool.Pin] = "Cette fenêtre est déjà au premier plan par Windows ou son application.";
            return;
        }
        if (!HubWindows.SetTopmost(window.Handle, true))
        {
            _errors[HubTool.Pin] = "Windows a refusé de modifier cette fenêtre.";
            return;
        }
        _pins.Add(window.Handle, (window, MakeBorder(options)));
    }

    private static DecorativeOverlay? MakeBorder(HubOptions options) => options.Indicator
        ? new(ColorTranslator.FromHtml(options.Color), options.Opacity) : null;

    private void Unpin(long handle)
    {
        if (!_pins.Remove(handle, out var entry))
            return;
        entry.Border?.Dispose();
        if (HubWindows.IsSame(entry.Window) && !HubWindows.SetTopmost(handle, false))
            _errors[HubTool.Pin] = "Windows a refusé de retirer l’épinglage d’une fenêtre.";
    }

    private void UnpinAll()
    {
        foreach (var handle in _pins.Keys.ToArray())
            Unpin(handle);
        SetPlacement();
    }

    private void SetPlacement()
    {
        if (_disposed)
            return;
        var options = _preferences.Get(HubTool.Pin);
        if (_taskbar is not null || _pins.Count > 0 || options.Enabled && options.PinRules.Length > 0)
        {
            _placement.Start();
            PlaceDecorations();
        }
        else
            _placement.Stop();
    }

    private void PlaceDecorations()
    {
        try
        {
            if (_taskbar is not null)
            {
                var hwnd = HubWindows.Taskbar.ToInt64();
                var bar = HubWindows.Bounds(hwnd);
                var style = _preferences.Get(HubTool.Taskbar);
                var size = style.Size;
                if (bar is { } bounds && HubWindows.IsVisible(hwnd) && Math.Min(bounds.Width, bounds.Height) >= size + 3 &&
                    Screen.AllScreens.Any(screen => Rectangle.Intersect(screen.Bounds, bounds).Width > 5 &&
                        Rectangle.Intersect(screen.Bounds, bounds).Height >= size + 3) &&
                    (HubWindows.Bounds(HubWindows.Active.ToInt64()) is not { } active || !active.Contains(bounds)))
                {
                    var screen = Screen.FromRectangle(bounds).Bounds;
                    var inset = Math.Min(style.Inset, (Math.Max(bounds.Width, bounds.Height) - 1) / 2);
                    _taskbar.Place(bounds.Width >= bounds.Height
                        ? new Rectangle(bounds.X + inset, bounds.Y <= screen.Top + 4 ? bounds.Bottom - size : bounds.Top, bounds.Width - 2 * inset, size)
                        : new Rectangle(bounds.X <= screen.Left + 4 ? bounds.Right - size : bounds.Left, bounds.Y + inset, size, bounds.Height - 2 * inset),
                        radius: style.Radius);
                }
                else
                    _taskbar.Place(null);
            }

            var options = _preferences.Get(HubTool.Pin);
            _ruleSuppressed.RemoveWhere(handle => !HubWindows.Exists(handle));
            if (options.Enabled && options.PinRules.Length > 0 && HubWindows.Describe(HubWindows.Active) is { } foreground &&
                !_pins.ContainsKey(foreground.Handle) && !_ruleSuppressed.Contains(foreground.Handle) &&
                HubWindows.Excluded(foreground.App, options.PinRules) && !HubWindows.Excluded(foreground.App, options.Exclusions) &&
                !HubWindows.Topmost(foreground.Handle))
                Pin(foreground, options);

            foreach (var (handle, entry) in _pins.ToArray())
                if (!HubWindows.IsSame(entry.Window))
                    Unpin(handle);
                else
                    entry.Border?.Place(HubWindows.Bounds(handle), options.Size);

            if (_taskbar is null && _pins.Count == 0 && (!options.Enabled || options.PinRules.Length == 0))
                _placement.Stop();
        }
        catch (Exception error) { WindowsSettings.Log("Positionnement des surcouches.", error); }
    }

    private readonly HashSet<long> _ruleSuppressed = [];
    private static bool HasShortcut(HubTool tool) => tool is HubTool.Switcher or HubTool.Macro or HubTool.Picker or HubTool.Ruler or HubTool.Pin;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _overlay?.Close();
        _taskbar?.Dispose();
        _placement.Dispose();
        _repeat.Dispose();
        _macros.Dispose();
        _monitor.Dispose();
        UnpinAll();
        foreach (var owner in _owners.Values)
            _hotkeys.Retain(owner);
    }
}
