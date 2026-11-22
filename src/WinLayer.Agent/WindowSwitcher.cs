using System.Runtime.InteropServices;
using WinLayer.Core;

namespace WinLayer.Agent;

internal sealed class WindowSwitcher : HubOverlay
{
    private readonly HubWindow[] _windows;
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Rechercher une fenêtre" };
    private readonly ListView _list;
    private readonly Panel _preview = new() { Dock = DockStyle.Fill };
    private readonly Label _previewMessage = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label _windowTitle = new() { Dock = DockStyle.Top, Height = 48, AutoEllipsis = true, Padding = new Padding(10, 8, 10, 4) };
    private readonly Label _hint = new() { Dock = DockStyle.Bottom, Height = 27, TextAlign = ContentAlignment.MiddleLeft };
    private int _selectedIndex;
    private bool _selecting;
    private readonly ImageList _icons = new() { ImageSize = new(32, 32), ColorDepth = ColorDepth.Depth32Bit };
    private readonly Action<string> _feedback;
    private readonly Action<HubWindow>? _selected;
    private IntPtr _thumbnail;
    private int _previewRevision;

    public WindowSwitcher(HubOptions options, HubWindow[] windows, Action<string> feedback, Action<HubWindow>? selected = null)
    {
        Text = "WinLayer — Sélecteur de fenêtres";
        AccessibleName = "Sélecteur de fenêtres";
        _feedback = feedback;
        _selected = selected;
        BackColor = Color.FromArgb(29, 31, 55);
        var accent = ColorTranslator.FromHtml(options.Color);
        var panel = Color.FromArgb(36, 39, 68);
        ForeColor = Color.FromArgb(242, 241, 255);
        Padding = new Padding(20, 17, 20, 13);
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.BackColor = panel;
        _search.ForeColor = ForeColor;
        _search.AccessibleName = "Rechercher une fenêtre";
        _hint.ForeColor = Color.FromArgb(189, 194, 224);
        _windows = windows;
        _list = new ListView
        {
            Dock = DockStyle.Fill, MultiSelect = false, HideSelection = false, FullRowSelect = true,
            BackColor = panel, ForeColor = ForeColor, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            AccessibleName = "Fenêtres ouvertes",
            View = options.Layout == SwitcherLayout.Cards ? View.Tile : options.Layout == SwitcherLayout.List ? View.Details : View.LargeIcon,
            SmallImageList = _icons, LargeImageList = _icons, TileSize = new Size(235, 54)
        };
        _list.Columns.Add("Fenêtre", 260);
        _list.Columns.Add("Application", 125);
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_list.SelectedIndices.Count == 1)
                _selectedIndex = _list.SelectedIndices[0];
            UpdatePreview();
        };
        _list.DoubleClick += async (_, _) => await SelectWindowAsync();
        _search.TextChanged += (_, _) => Filter();

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var scale = DeviceDpi / 96d;
        var width = Math.Min(area.Width, (int)(options.Width * scale));
        var height = Math.Min(area.Height, (int)(460 * scale));
        Bounds = new(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
        var headline = new Label
        {
            Dock = DockStyle.Top, Text = selected is null ? "Changer de fenêtre" : "Choisir la fenêtre cible",
            Height = 40, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 15, FontStyle.Bold)
        };
        headline.Disposed += (_, _) => headline.Font.Dispose();
        var previewPanel = new Panel { Dock = DockStyle.Right, Width = Math.Clamp(width / 2, 225, 430), Padding = new Padding(9, 0, 0, 0) };
        previewPanel.Visible = width >= 620;
        _preview.BackColor = Color.FromArgb(20, 22, 39);
        _previewMessage.Text = "Aperçu indisponible";
        _previewMessage.ForeColor = _hint.ForeColor;
        _preview.Controls.Add(_previewMessage);
        _windowTitle.BackColor = panel;
        _windowTitle.ForeColor = ForeColor;
        previewPanel.Controls.Add(_preview);
        previewPanel.Controls.Add(_windowTitle);
        var marker = new Panel { Height = 3, Dock = DockStyle.Top, BackColor = accent };
        previewPanel.Controls.Add(marker);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 3) };
        body.Controls.Add(_list);
        body.Controls.Add(previewPanel);
        Controls.Add(body);
        Controls.Add(_hint);
        Controls.Add(_search);
        Controls.Add(headline);
        _icons.Images.Add("default", SystemIcons.Application);
        Filter();
        Shown += (_, _) =>
        {
            _search.Focus();
            if (_list.View == View.Details)
            {
                _list.Columns[1].Width = Math.Clamp(_list.ClientSize.Width / 3, 85, 140);
                _list.Columns[0].Width = Math.Max(110, _list.ClientSize.Width - _list.Columns[1].Width - SystemInformation.VerticalScrollBarWidth);
            }
            UpdatePreview();
            _ = LoadIconsAsync();
        };
    }

    private void Filter()
    {
        var query = _search.Text.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var window in _windows)
                if (query.All(part => window.Title.Contains(part, StringComparison.CurrentCultureIgnoreCase) ||
                    window.App.Contains(part, StringComparison.CurrentCultureIgnoreCase)))
                    _list.Items.Add(new ListViewItem([window.Title, window.App]) { Tag = window, ImageKey = _icons.Images.ContainsKey(window.App) ? window.App : "default" });
            _selectedIndex = 0;
            if (_list.Items.Count > 0)
            {
                _list.Items[0].Selected = true;
                _list.Items[0].Focused = true;
            }
            _hint.Text = _list.Items.Count == 0 ? "Aucune fenêtre ne correspond · Échap pour fermer" :
                $"{_list.Items.Count} fenêtre(s)  ·  ↑ ↓ Parcourir   Entrée Ouvrir   Échap Fermer";
        }
        finally { _list.EndUpdate(); }
        UpdatePreview();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter)
        {
            _ = SelectWindowAsync();
            return true;
        }
        if ((_search.Focused && keyData is Keys.Down or Keys.Up || keyData is Keys.Tab or (Keys.Shift | Keys.Tab)) &&
            _list.Items.Count > 0)
        {
            var step = keyData is Keys.Up or (Keys.Shift | Keys.Tab) ? -1 : 1;
            _selectedIndex = (_selectedIndex + step + _list.Items.Count) % _list.Items.Count;
            var item = _list.Items[_selectedIndex];
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task SelectWindowAsync()
    {
        if (_selecting || _list.Items.Count <= _selectedIndex || _list.Items[_selectedIndex].Tag is not HubWindow window)
            return;

        _selecting = true;
        try
        {
            var activated = HubWindows.Activate(window);
            if (!activated && HubWindows.IsSame(window))
            {
                await Task.Delay(75);
                if (IsDisposed)
                    return;
                activated = HubWindows.Activate(window);
            }
            if (!activated)
            {
                const string message = "Cette fenêtre est indisponible. Sélectionnez-en une autre.";
                _feedback(message);
                _hint.Text = message;
                return;
            }
            _selected?.Invoke(window);
            Close();
        }
        catch (Exception error)
        {
            WindowsSettings.Log("Activation de la fenêtre.", error);
            _hint.Text = "Impossible d’ouvrir cette fenêtre.";
        }
        finally { _selecting = false; }
    }

    private async Task LoadIconsAsync()
    {
        foreach (var window in _windows.DistinctBy(window => window.App).Take(32))
        {
            if (IsDisposed)
                return;
            try
            {
                using var icon = await Task.Run(() => HubWindows.WindowIcon(window));
                if (IsDisposed)
                    return;
                if (icon is null)
                    continue;
                _ = _icons.Handle;
                using var bitmap = icon.ToBitmap();
                _icons.Images.Add(window.App, bitmap);
                foreach (ListViewItem item in _list.Items)
                    if (item.Tag is HubWindow entry && entry.App == window.App)
                        item.ImageKey = window.App;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // An app may close before its icon arrives.
            }
        }
    }

    private async void UpdatePreview()
    {
        var revision = ++_previewRevision;
        RemovePreview();
        _previewMessage.Visible = true;
        if (_list.Items.Count <= _selectedIndex || _list.Items[_selectedIndex].Tag is not HubWindow window)
        {
            _windowTitle.Text = "Aucune fenêtre";
            return;
        }
        _windowTitle.Text = $"{window.App} · {window.Title}";
        if (!_preview.Visible || !IsHandleCreated)
            return;
        var hwnd = Handle;
        var bounds = RectangleToClient(_preview.RectangleToScreen(_preview.ClientRectangle));
        try
        {
            var preview = await Task.Run(() => RegisterThumbnail(hwnd, window, bounds));
            if (IsDisposed || revision != _previewRevision)
            {
                if (preview != IntPtr.Zero)
                    DwmUnregisterThumbnail(preview);
                return;
            }
            _thumbnail = preview;
            _previewMessage.Visible = preview == IntPtr.Zero;
        }
        catch (Exception error)
        {
            WindowsSettings.Log("Miniature du sélecteur.", error);
        }
    }

    private static IntPtr RegisterThumbnail(IntPtr hwnd, HubWindow window, Rectangle bounds)
    {
        if (!HubWindows.IsSame(window) || DwmRegisterThumbnail(hwnd, new(window.Handle), out var preview) != 0)
            return IntPtr.Zero;
        bounds.Inflate(-10, -10);
        if (DwmQueryThumbnailSourceSize(preview, out var source) == 0 && source.Width > 0 && source.Height > 0)
        {
            var scale = Math.Min((double)bounds.Width / source.Width, (double)bounds.Height / source.Height);
            var width = (int)(source.Width * scale);
            var height = (int)(source.Height * scale);
            bounds = new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
        }
        var layout = new Thumbnail
        {
            Flags = 1 | 4 | 8,
            Destination = new() { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom },
            Opacity = 255,
            Visible = 1
        };
        if (DwmUpdateThumbnailProperties(preview, in layout) == 0)
            return preview;
        DwmUnregisterThumbnail(preview);
        return IntPtr.Zero;
    }

    private void RemovePreview()
    {
        var previous = _thumbnail;
        _thumbnail = IntPtr.Zero;
        if (previous != IntPtr.Zero)
            _ = Task.Run(() => DwmUnregisterThumbnail(previous));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RemovePreview();
            _icons.Dispose();
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Thumbnail
    {
        public uint Flags;
        public HubWindows.NativeRect Destination, Source;
        public byte Opacity;
        public int Visible, SourceClientOnly;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out Size size);
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, in Thumbnail layout);
}
