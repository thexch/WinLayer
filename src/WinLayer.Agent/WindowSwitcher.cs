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

    public WindowSwitcher(HubOptions options, Action<string> feedback, Action<HubWindow>? selected = null)
    {
        AccessibleName = "Sélecteur de fenêtres";
        _feedback = feedback;
        _selected = selected;
        BackColor = Color.FromArgb(29, 31, 55);
        Padding = new Padding(18);
        _windows = HubWindows.List(options.Exclusions);
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
        foreach (var window in _windows.DistinctBy(window => window.App))
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(window.Pid);
                if (process.MainModule?.FileName is { } path)
                {
                    using var icon = Icon.ExtractAssociatedIcon(path);
                    if (icon is not null)
                        _icons.Images.Add(window.App, icon);
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                // Protected or closed applications still have a title and may be selected.
            }
        }
        Filter();
        Shown += (_, _) => { _search.Focus(); UpdatePreview(); };
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
                    _list.Items.Add(new ListViewItem([window.Title, window.App]) { Tag = window, ImageKey = window.App });
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

    private void UpdatePreview()
    {
        RemovePreview();
        if (!_preview.Visible || !IsHandleCreated || _list.SelectedItems.Count != 1 ||
            _list.SelectedItems[0].Tag is not HubWindow window || !HubWindows.IsSame(window) ||
            DwmRegisterThumbnail(Handle, new(window.Handle), out _thumbnail) != 0)
            return;
        var bounds = _preview.Bounds;
        var layout = new Thumbnail
        {
            Flags = 1 | 4 | 8,
            Destination = new() { Left = bounds.Left + 8, Top = bounds.Top + 8, Right = bounds.Right - 8, Bottom = bounds.Bottom - 8 },
            Opacity = 255,
            Visible = 1
        };
        if (DwmUpdateThumbnailProperties(_thumbnail, in layout) != 0)
            RemovePreview();
    }

    private void RemovePreview()
    {
        if (_thumbnail != IntPtr.Zero)
            DwmUnregisterThumbnail(_thumbnail);
        _thumbnail = IntPtr.Zero;
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
