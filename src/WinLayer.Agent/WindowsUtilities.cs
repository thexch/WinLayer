using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using WinLayer.Core;

namespace WinLayer.Agent;

internal sealed partial class WindowsUtilities(GlobalHotkeys hotkeys, WindowsHub hub, Action<Page, string?> navigate,
    Func<HubPreferences?> library) : IDisposable
{
    private readonly DropShelf _shelf = new();
    private readonly List<Form> _pins = [];
    private readonly List<string> _recent = [];
    private readonly System.Windows.Forms.Timer _corners = new() { Interval = 90 };
    private readonly System.Windows.Forms.Timer _hideOsd = new();
    private UtilityOptions _options = new();
    private Form? _launcher;
    private Form? _picker;
    private Form? _ocr;
    private Form? _ocrHistory;
    private Form? _converter;
    private Form? _osd;
    private LockNotifications? _keyboard;
    private string? _error;
    private bool _disposed;
    private int _corner = -1;
    private long _entered;
    private long _lastTrigger;
    private bool _fired;
    private Rectangle _cornerScreen;

    private (ClickShortcut Key, string Id)[] Bindings(UtilityOptions options) =>
        [(options.Ocr, "ocr"), (options.Pin, "screen-pin"), (options.Shelf, "shelf"), (options.Palette, "palette"), (options.Radial, "radial")];

    public void Initialize(UtilityOptions options)
    {
        _corners.Tick += (_, _) => CheckCorners();
        _hideOsd.Tick += (_, _) => { _hideOsd.Stop(); _osd?.Close(); };
        foreach (var (key, _) in Bindings(options))
        {
            try { hotkeys.Reserve(this, key, OnShortcut); }
            catch (InvalidOperationException error) { _error = error.Message; }
        }
        Apply(options);
    }

    private void OnShortcut(ClickShortcut key)
    {
        var id = Bindings(_options).FirstOrDefault(binding => binding.Key == key).Id;
        if (id is not null) Run(id);
    }

    public void Reserve(UtilityOptions options)
    {
        foreach (var (key, _) in Bindings(options)) hotkeys.Reserve(this, key, OnShortcut);
    }

    public void Retain() => hotkeys.Retain(this, Bindings(_options).Select(binding => binding.Key).ToArray());

    public void Apply(UtilityOptions options)
    {
        var loadRecent = options.PersistRecent && !_options.PersistRecent;
        _options = options;
        UtilityWindows.Theme = options.Themes.FirstOrDefault(theme => theme.Name == options.Theme);
        if (loadRecent) LoadRecent();
        _corners.Enabled = options.Corners.Any(corner => corner.Action.Length > 0);
        _corner = -1;
        if (options.Caps || options.Num || options.Scroll)
        {
            try { _keyboard ??= new LockNotifications(OnLock); }
            catch (System.ComponentModel.Win32Exception error)
            {
                _error = "Les notifications clavier sont indisponibles.";
                WindowsSettings.Log("Raw input WinLayer.", error);
            }
        }
        else
        {
            _keyboard?.Dispose();
            _keyboard = null;
            _hideOsd.Stop();
            _osd?.Close();
        }
    }

    public UtilityStatus Snapshot(string? undo) => new(undo, _error, _recent.ToArray());

    private IEnumerable<ActionEntry> Entries() => ActionCatalog.Entries.Concat((library()?.Macros ?? []).Select(macro =>
        new ActionEntry("macro:" + macro.Name, macro.Name, "Macro", "jouer exécuter séquence", "macro", Tool: "macro")))
        .Concat(_options.Launchers.Select(item => new ActionEntry(item.Id, item.Label, "Raccourcis locaux",
            "ouvrir application fichier dossier", "folder", Tool: "local")));

    public async void Run(string id)
    {
        if (_disposed) return;
        try { await ExecuteAsync(id); }
        catch (Exception error)
        {
            _error = error is InvalidOperationException ? error.Message : "L’action a échoué. Consultez le journal WinLayer.";
            WindowsSettings.Log("Utilitaire WinLayer.", error);
        }
    }

    public async Task ExecuteAsync(string id)
    {
        var entry = Entries().FirstOrDefault(action => action.Id == id) ?? throw new InvalidOperationException("Cette action n’est plus disponible.");
        _error = null;
        if (entry.Page is not null && Enum.TryParse<Page>(entry.Page, true, out var page))
            navigate(page, entry.Target);
        else
        {
            if (_picker is not null)
            {
                _picker.Close();
                if (entry.Tool is "ocr" or "capturePin") return;
            }
            switch (entry.Tool)
            {
                case "ocr":
                case "capturePin":
                    SelectRegion(entry.Tool == "ocr");
                    break;
                case "ocrHistory": ShowOcrHistory(); break;
                case "desktop": RunShellCommand("ToggleDesktop"); break;
                case "lock":
                    if (UtilityWindows.Confirm("Verrouiller Windows", "Verrouiller votre session Windows ?", "Verrouiller"))
                        if (LockWorkStation() == 0) throw new InvalidOperationException("Windows n’a pas pu verrouiller la session.");
                    break;
                case "local": Launch(_options.Launchers.Single(item => item.Id == id)); break;
                case "shelf":
                    _shelf.Toggle(_options.ShelfHours, new(_options.ShelfWidth, _options.ShelfHeight), _options.ShelfPosition);
                    break;
                case "palette":
                case "radial":
                    ToggleLauncher(entry.Tool == "radial");
                    return;
                case "converter":
                    if (_converter is null)
                    {
                        _converter = ImageConverter.Create();
                        _converter.FormClosed += (_, _) => _converter = null;
                        _converter.Show();
                    }
                    else _converter.Activate();
                    break;
                case "record": await hub.RunAsync(new(HubTool.Macro, HubAction.Open)); break;
                case "switch": await hub.RunAsync(new(HubTool.Switcher, HubAction.Open)); break;
                case "color": await hub.RunAsync(new(HubTool.Picker, HubAction.Open)); break;
                case "ruler": await hub.RunAsync(new(HubTool.Ruler, HubAction.Open)); break;
                case "macro": await hub.RunAsync(new(HubTool.Macro, HubAction.Play, Value: entry.Label)); break;
                default: throw new InvalidOperationException("Cette action n’est pas disponible.");
            }
        }
        _recent.Remove(id);
        _recent.Insert(0, id);
        if (_recent.Count > 12) _recent.RemoveAt(_recent.Count - 1);
        if (_options.PersistRecent) WriteRecent();
    }

    private static string RecentPath => Path.Combine(WindowsSettings.DataDirectory, "recent-actions.json");

    private void LoadRecent()
    {
        try
        {
            if (!File.Exists(RecentPath) || new FileInfo(RecentPath).Length > 8192) return;
            var saved = JsonSerializer.Deserialize<string[]>(File.ReadAllText(RecentPath)) ?? [];
            var existing = Entries().Select(item => item.Id).ToHashSet();
            var merged = _recent.Concat(saved).Where(existing.Contains).Distinct().Take(12).ToArray();
            _recent.Clear();
            _recent.AddRange(merged);
        }
        catch (Exception error) { WindowsSettings.Log("Historique des actions WinLayer.", error); }
    }

    private void WriteRecent()
    {
        try
        {
            Directory.CreateDirectory(WindowsSettings.DataDirectory);
            File.WriteAllText(RecentPath + ".tmp", JsonSerializer.Serialize(_recent));
            File.Move(RecentPath + ".tmp", RecentPath, true);
        }
        catch (Exception error) { WindowsSettings.Log("Historique des actions WinLayer.", error); }
    }

    private void SelectRegion(bool ocr)
    {
        _launcher?.Close();
        var picker = new CaptureRegion(image =>
        {
            try
            {
                if (ocr)
                {
                    _ocr?.Close();
                    _ocr = UtilityCapture.Ocr(image, _options.Language, _options.AutoCopy, _options.OcrHistory ? SaveOcr : null);
                    _ocr.FormClosed += (_, _) => _ocr = null;
                    _ocr.Show();
                }
                else
                {
                    if (_pins.Count >= 8) _pins[0].Close();
                    var pin = UtilityCapture.Pin(image, Cursor.Position, ClosePins);
                    _pins.Add(pin);
                    pin.FormClosed += (_, _) => _pins.Remove(pin);
                    pin.Show();
                }
            }
            catch (Exception error)
            {
                image.Dispose();
                _error = "Impossible d’afficher la capture.";
                WindowsSettings.Log("Affichage de capture.", error);
            }
        });
        _picker = picker;
        picker.FormClosed += (_, _) => _picker = null;
        picker.Show();
    }

    private static string HistoryPath => Path.Combine(WindowsSettings.DataDirectory, "ocr-history.json");

    private static string[] ReadOcr() => File.Exists(HistoryPath) && new FileInfo(HistoryPath).Length < 500_000
        ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(HistoryPath)) ?? [] : [];

    private static void SaveOcr(string text)
    {
        var path = HistoryPath;
        var items = ReadOcr();
        Directory.CreateDirectory(WindowsSettings.DataDirectory);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new[] { text[..Math.Min(10_000, text.Length)] }.Concat(items).Take(12)));
        File.Move(path + ".tmp", path, true);
    }

    private void ShowOcrHistory()
    {
        if (_ocrHistory is not null) { _ocrHistory.Activate(); return; }
        var form = UtilityWindows.Create("Historique Screen OCR", new Size(680, 480));
        var browser = new ListBox { Dock = DockStyle.Top, Height = 140, AccessibleName = "Captures précédentes",
            BackColor = UtilityWindows.Surface, ForeColor = UtilityWindows.Ink };
        var text = new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            AccessibleName = "Texte de la capture", BackColor = UtilityWindows.Surface, ForeColor = UtilityWindows.Ink };
        var history = ReadOcr();
        browser.Items.AddRange(history.Select((item, index) =>
        {
            var caption = item.ReplaceLineEndings(" ");
            return $"{index + 1}  ·  {caption[..Math.Min(caption.Length, 80)]}";
        }).Cast<object>().ToArray());
        browser.SelectedIndexChanged += (_, _) => text.Text = browser.SelectedIndex < 0 ? "" : history[browser.SelectedIndex];
        if (browser.Items.Count > 0) browser.SelectedIndex = 0;
        var toolbar = UtilityWindows.Toolbar();
        toolbar.Controls.AddRange([
            UtilityWindows.Button("Copier", () => { if (text.TextLength > 0) Clipboard.SetText(text.Text); }),
            UtilityWindows.Button("Effacer l’historique", () =>
            {
                if (!UtilityWindows.Confirm("Historique Screen OCR", "Effacer le texte de toutes les captures conservées ?", "Effacer", form)) return;
                File.Delete(HistoryPath);
                File.Delete(HistoryPath + ".tmp");
                history = [];
                browser.Items.Clear();
                text.Clear();
            })
        ]);
        form.Controls.Add(text);
        form.Controls.Add(browser);
        form.Controls.Add(toolbar);
        _ocrHistory = form;
        form.FormClosed += (_, _) => _ocrHistory = null;
        form.Show();
    }

    private static void RunShellCommand(string command)
    {
        var type = Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!;
        var shell = Activator.CreateInstance(type)!;
        try { type.InvokeMember(command, BindingFlags.InvokeMethod, null, shell, null); }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    private static void Launch(LocalLauncher item)
    {
        var path = Path.GetFullPath(item.Path);
        var directory = Directory.Exists(path);
        if (!directory && !File.Exists(path)) throw new InvalidOperationException("Ce fichier ou dossier est introuvable.");
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType is DriveType.Network or DriveType.Unknown)
            throw new InvalidOperationException("Seuls les lecteurs locaux sont pris en charge.");

        if (!directory)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".exe" or ".txt" or ".pdf" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp"))
                throw new InvalidOperationException("Ce type de fichier ne peut pas être ouvert depuis WinLayer.");
            if (extension == ".exe" && !UtilityWindows.Confirm(item.Label, $"Ouvrir cette application ?{Environment.NewLine}{path}", "Ouvrir")) return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    private void ClosePins()
    {
        foreach (var form in _pins.ToArray()) form.Close();
    }

    private void ToggleLauncher(bool radial)
    {
        if (_launcher is not null)
        {
            var wasRadial = _launcher is RadialOverlay;
            _launcher.Close();
            if (radial == wasRadial) return;
        }
        var actions = Entries().ToArray();
        var byId = actions.ToDictionary(action => action.Id);
        var order = _options.Favorites.Concat(_recent).Distinct().Where(byId.ContainsKey).Select(id => byId[id]);
        var radialItems = _options.RadialItems.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
        void Execute(ActionEntry action)
        {
            _launcher?.Close();
            Run(action.Id);
        }
        var form = radial && radialItems.Length > 0
            ? new RadialOverlay(radialItems, ColorTranslator.FromHtml(_options.RadialAccent), Execute)
            : Palette(actions, order, Execute);
        _launcher = form;
        form.FormClosed += (_, _) => _launcher = null;
        form.Show();
    }

    private static Form Palette(ActionEntry[] actions, IEnumerable<ActionEntry> priority, Action<ActionEntry> run)
    {
        var form = UtilityWindows.Create("Palette de commandes", new Size(625, 460));
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Rechercher une commande, une macro ou un réglage…", MaxLength = 120,
            AccessibleName = "Rechercher une commande", Font = new Font("Segoe UI", 14), BackColor = UtilityWindows.Surface, ForeColor = UtilityWindows.Ink };
        var list = new ListBox { Dock = DockStyle.Fill, BackColor = UtilityWindows.Surface, ForeColor = UtilityWindows.Ink,
            IntegralHeight = false, AccessibleName = "Résultats des commandes" };
        var glyphs = new Dictionary<string, Bitmap>();
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.ItemHeight = 29;
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= list.Items.Count || list.Items[e.Index] is not ActionEntry action) return;
            e.DrawBackground();
            if (!glyphs.TryGetValue(action.Icon, out var glyph))
                glyphs.Add(action.Icon, glyph = UtilityWindows.Glyph(action.Icon));
            e.Graphics.DrawImageUnscaled(glyph, e.Bounds.Left + 6, e.Bounds.Top + 5);
            TextRenderer.DrawText(e.Graphics, action.Caption, list.Font,
                new Rectangle(e.Bounds.Left + 32, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 40), e.Bounds.Height),
                e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        };
        void SelectItem() { if (list.SelectedItem is ActionEntry action) run(action); }
        void Refresh()
        {
            var query = search.Text.Trim();
            var matches = query.Length == 0 ? priority.Concat(actions).DistinctBy(action => action.Id) :
                actions.Select(action => (Action: action, Score: ActionCatalog.Match(query, action.Label, action.Keywords + " " + action.Category)))
                .Where(result => result.Score > 0).OrderByDescending(result => result.Score).Select(result => result.Action);
            list.Items.Clear();
            list.Items.AddRange(matches.Take(40).Cast<object>().ToArray());
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        search.TextChanged += (_, _) => Refresh();
        form.KeyDown += (_, e) =>
        {
            if (form.IsDisposed) return;
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SelectItem(); }
            else if (search.Focused && list.Items.Count > 0 && e.KeyCode is Keys.Down or Keys.Up)
            {
                list.SelectedIndex = Math.Clamp(list.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1), 0, list.Items.Count - 1);
                e.SuppressKeyPress = true;
            }
        };
        form.Shown += (_, _) => search.Focus();
        form.Deactivate += (_, _) => form.Close();
        list.DoubleClick += (_, _) => SelectItem();
        form.Controls.Add(list);
        form.Controls.Add(search);
        var searchFont = search.Font;
        form.Disposed += (_, _) =>
        {
            searchFont.Dispose();
            foreach (var glyph in glyphs.Values) glyph.Dispose();
        };
        Refresh();
        return form;
    }

    private void CheckCorners()
    {
        try
        {
            var now = Environment.TickCount64;
            var pointer = Cursor.Position;
            var screen = Screen.FromPoint(pointer);
            var bounds = screen.Bounds;
            var left = pointer.X < bounds.Left + _options.CornerPixels;
            var right = pointer.X >= bounds.Right - _options.CornerPixels;
            var top = pointer.Y < bounds.Top + _options.CornerPixels;
            var bottom = pointer.Y >= bounds.Bottom - _options.CornerPixels;
            var corner = top && left ? 0 : top && right ? 1 : bottom && left ? 2 : bottom && right ? 3 : -1;
            if (_options.CornerModifier && (Control.ModifierKeys & Keys.Shift) == 0) corner = -1;
            if (corner != _corner || bounds != _cornerScreen)
            {
                _cornerScreen = bounds;
                _corner = corner;
                _entered = now;
                _fired = false;
            }
            if (corner < 0 || _fired || _picker is not null || _launcher is not null) return;
            var config = _options.Corners[corner];
            if (config.Action.Length == 0 || now - _entered < config.Delay || now - _lastTrigger < _options.CornerCooldown) return;
            _fired = true;
            var active = HubWindows.Active;
            if (_options.ExcludeFullscreen && HubWindows.Bounds(active.ToInt64()) is { } foreground && foreground.Contains(bounds))
                return;
            if (_options.CornerExclusions.Length > 0 && active != 0)
            {
                var foregroundWindow = HubWindows.Describe(active);
                var excluded = _options.CornerExclusions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (foregroundWindow is not null && excluded.Any(value => foregroundWindow.Title.Contains(value, StringComparison.OrdinalIgnoreCase) || HubWindows.Excluded(foregroundWindow.App, value)))
                    return;
            }
            _lastTrigger = now;
            Run(config.Action);
        }
        catch (Exception error) { WindowsSettings.Log("Coin actif.", error); }
    }

    private void OnLock(Keys key)
    {
        var name = key switch
        {
            Keys.CapsLock when _options.Caps => "Caps Lock",
            Keys.NumLock when _options.Num => "Num Lock",
            Keys.Scroll when _options.Scroll => "Scroll Lock",
            _ => null
        };
        if (name is null) return;
        if (_options.HideOsdFullscreen && HubWindows.Bounds(HubWindows.Active.ToInt64()) is { } bounds &&
            Screen.AllScreens.Any(screen => bounds.Contains(screen.Bounds))) return;
        _osd?.Close();
        var on = Control.IsKeyLocked(key);
        var form = new LockOsd(_options, name, on);
        _osd = form;
        form.FormClosed += (_, _) => _osd = null;
        form.Show();
        _hideOsd.Stop();
        _hideOsd.Interval = 1000 * _options.OsdSeconds;
        _hideOsd.Start();
    }

    [LibraryImport("user32.dll")]
    private static partial int LockWorkStation();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        hotkeys.Retain(this);
        _corners.Dispose();
        _hideOsd.Dispose();
        _keyboard?.Dispose();
        _osd?.Close();
        _launcher?.Close();
        _picker?.Close();
        _ocr?.Close();
        _ocrHistory?.Close();
        _converter?.Close();
        _shelf.Dispose();
        ClosePins();
    }
}

internal sealed class RadialOverlay : HubOverlay
{
    private readonly Color _accent;
    private readonly Button[] _buttons;

    public RadialOverlay(ActionEntry[] items, Color accent, Action<ActionEntry> execute)
    {
        AccessibleName = "Menu radial";
        _accent = accent;
        Size = new Size(410, 410);
        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;
        Location = new(Math.Clamp(cursor.X - Width / 2, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(cursor.Y - Height / 2, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        BackColor = UtilityWindows.Canvas;
        using var circle = new GraphicsPath();
        circle.AddEllipse(ClientRectangle);
        Region = new Region(circle);
        _buttons = items.Select((item, index) =>
        {
            var angle = Math.PI * (2d * index / items.Length - 0.5);
            var button = UtilityWindows.Button(item.Label, () => execute(item));
            button.AutoSize = false;
            button.Size = new Size(94, 54);
            button.Location = new((int)(Width / 2 + 142 * Math.Cos(angle) - 47), (int)(Height / 2 + 142 * Math.Sin(angle) - 27));
            var glyph = UtilityWindows.Glyph(item.Icon);
            button.Image = glyph;
            button.TextImageRelation = TextImageRelation.ImageAboveText;
            button.Disposed += (_, _) => glyph.Dispose();
            button.FlatAppearance.BorderColor = accent;
            button.AccessibleName = item.Label;
            button.TabIndex = index;
            Controls.Add(button);
            return button;
        }).ToArray();
        var close = UtilityWindows.Button("×", Close);
        close.AccessibleName = "Fermer le menu radial";
        close.AutoSize = false;
        close.Size = new Size(36, 36);
        close.Location = new Point((Width - close.Width) / 2, (Height - close.Height) / 2);
        close.TabIndex = _buttons.Length;
        Controls.Add(close);
        Shown += (_, _) => UtilityWindows.FadeIn(this, 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var ring = new Pen(Color.FromArgb(95, _accent), 1.5f);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawEllipse(ring, 14, 14, Width - 28, Height - 28);
        e.Graphics.DrawEllipse(ring, 148, 148, 114, 114);
    }

    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Down or Keys.Right or Keys.Up or Keys.Left)
        {
            var index = Array.IndexOf(_buttons, ActiveControl as Button);
            var step = keyData is Keys.Up or Keys.Left ? -1 : 1;
            _buttons[index < 0 ? 0 : (index + step + _buttons.Length) % _buttons.Length].Focus();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

internal sealed class LockOsd : HubOverlay
{
    public LockOsd(UtilityOptions options, string name, bool on)
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Width = (options.CompactOsd ? 180 : 230) * options.OsdSize / 100;
        Height = (options.CompactOsd ? 44 : 64) * options.OsdSize / 100;
        Location = new(area.Left + (area.Width - Width) / 2, options.OsdPosition switch
        {
            0 => area.Top + 24,
            2 => area.Top + (area.Height - Height) / 2,
            _ => area.Bottom - Height - 34
        });
        var theme = options.Themes.FirstOrDefault(item => item.Name == options.Theme);
        BackColor = theme is null ? UtilityWindows.Canvas : ColorTranslator.FromHtml(theme.Surface);
        ForeColor = theme is null ? UtilityWindows.Ink : ColorTranslator.FromHtml(theme.Ink);
        Opacity = options.OsdOpacity / 100d;
        var label = new Label { Dock = DockStyle.Fill, Text = $"{name}  ·  {(on ? "Activé" : "Désactivé")}",
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = ForeColor };
        Controls.Add(label);
        Shown += (_, _) => UtilityWindows.FadeIn(this, options.OsdOpacity / 100d);
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var settings = base.CreateParams; settings.ExStyle |= 0x08000000 | 0x20; return settings; }
    }
}

internal sealed partial class LockNotifications : NativeWindow, IDisposable
{
    private readonly Action<Keys> _changed;
    public LockNotifications(Action<Keys> changed)
    {
        _changed = changed;
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        if (RegisterRawInputDevices(new RawDevice { Page = 1, Usage = 6, Flags = 0x100, Target = Handle }, 1, (uint)Marshal.SizeOf<RawDevice>()) == 0)
        {
            var error = new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            DestroyHandle();
            throw error;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x00FF)
        {
            var headerSize = (uint)Marshal.SizeOf<RawHeader>();
            var length = (uint)Marshal.SizeOf<RawKeyboard>();
            if (GetRawInputData(message.LParam, 0x10000003, out var data, ref length, headerSize) == length &&
                data.Header.Type == 1 && (data.Flags & 1) != 0 && (Keys)data.Key is Keys.CapsLock or Keys.NumLock or Keys.Scroll)
            {
                try { _changed((Keys)data.Key); }
                catch (Exception error) { WindowsSettings.Log("Indicateur clavier.", error); }
            }
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        RegisterRawInputDevices(new RawDevice { Page = 1, Usage = 6, Flags = 1 }, 1, (uint)Marshal.SizeOf<RawDevice>());
        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawDevice { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RawHeader { public uint Type, Size; public IntPtr Device, Param; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard { public RawHeader Header; public ushort Scan, Flags, Reserved, Key; public uint Message, Extra; }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int RegisterRawInputDevices(in RawDevice device, uint count, uint size);
    [LibraryImport("user32.dll")]
    private static partial uint GetRawInputData(IntPtr input, uint command, out RawKeyboard result, ref uint size, uint headerSize);
}
