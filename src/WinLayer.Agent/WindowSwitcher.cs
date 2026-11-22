using System.Runtime.InteropServices;
using WinLayer.Core;

namespace WinLayer.Agent;

internal sealed class WindowSwitcher : HubOverlay
{
    private readonly HubWindow[] _windows;
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Rechercher une fenêtre" };
    private readonly ListView _list;
    private readonly Panel _preview = new() { Dock = DockStyle.Right, Width = 225 };
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
        Padding = new Padding(18);
        _windows = windows;
        _list = new ListView
        {
            Dock = DockStyle.Fill, MultiSelect = false, HideSelection = false, FullRowSelect = true,
            BackColor = Color.FromArgb(35, 38, 65), ForeColor = Color.White,
            AccessibleName = "Fenêtres ouvertes",
            View = options.Layout == SwitcherLayout.Cards ? View.Tile : options.Layout == SwitcherLayout.List ? View.Details : View.LargeIcon,
            SmallImageList = _icons, LargeImageList = _icons, TileSize = new Size(235, 54)
        };
        _list.Columns.Add("Fenêtre", 255);
        _list.Columns.Add("Application", 105);
        _list.SelectedIndexChanged += (_, _) => UpdatePreview();
        _list.DoubleClick += (_, _) => SelectWindow();
        _search.TextChanged += (_, _) => Filter();
        _preview.BackColor = ColorTranslator.FromHtml(options.Color);
        Controls.Add(_list);
        Controls.Add(_preview);
        Controls.Add(_search);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var width = Math.Min(area.Width, options.Width);
        var height = Math.Min(area.Height, 430);
        Bounds = new(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
        if (width < 600)
            _preview.Visible = false;
        _icons.Images.Add("default", SystemIcons.Application);
        Filter();
        Shown += (_, _) =>
        {
            _search.Focus();
            UpdatePreview();
            _ = LoadIconsAsync();
        };
    }

    private void Filter()
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var window in _windows)
                if (window.Title.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase) ||
                    window.App.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase))
                    _list.Items.Add(new ListViewItem([window.Title, window.App]) { Tag = window, ImageKey = _icons.Images.ContainsKey(window.App) ? window.App : "default" });
            if (_list.Items.Count > 0)
                _list.Items[0].Selected = true;
        }
        finally { _list.EndUpdate(); }
        UpdatePreview();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter)
        {
            SelectWindow();
            return true;
        }
        if (_search.Focused && (keyData is Keys.Down or Keys.Up) && _list.Items.Count > 0)
        {
            var index = _list.SelectedIndices.Count == 1 ? _list.SelectedIndices[0] : 0;
            var item = _list.Items[Math.Clamp(index + (keyData == Keys.Down ? 1 : -1), 0, _list.Items.Count - 1)];
            item.Selected = true;
            item.EnsureVisible();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SelectWindow()
    {
        if (_list.SelectedItems.Count != 1 || _list.SelectedItems[0].Tag is not HubWindow window)
            return;

        if (!HubWindows.Activate(window))
            _feedback("Windows n’a pas activé cette fenêtre. Elle a pu se fermer ou refuser le changement de focus.");
        else
            _selected?.Invoke(window);
        Close();
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
        if (!_preview.Visible || !IsHandleCreated || _list.SelectedItems.Count != 1 ||
            _list.SelectedItems[0].Tag is not HubWindow window)
            return;
        var hwnd = Handle;
        var bounds = _preview.Bounds;
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
        var layout = new Thumbnail
        {
            Flags = 1 | 4 | 8,
            Destination = new() { Left = bounds.Left + 8, Top = bounds.Top + 8, Right = bounds.Right - 8, Bottom = bounds.Bottom - 8 },
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

    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, in Thumbnail layout);
}
