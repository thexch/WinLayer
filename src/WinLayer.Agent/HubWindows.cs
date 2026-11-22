using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WinLayer.Core;

namespace WinLayer.Agent;

internal static class HubWindows
{
    internal static IntPtr WindowAt(Point point) => GetAncestor(WindowFromPoint(point), 2);

    internal static IntPtr Active => GetForegroundWindow();

    internal static HubWindow[] List(string exclusions) => Enumerate(exclusions, visibleOnly: true);

    internal static HubWindow[] Enumerate(string exclusions, bool visibleOnly)
    {
        var windows = new List<HubWindow>();
        EnumWindows((handle, _) =>
        {
            if (windows.Count >= 200)
                return false;

            if ((!visibleOnly || IsWindowVisible(handle)) && GetWindow(handle, 4) == IntPtr.Zero &&
                (GetWindowLongW(handle, -20) & 0x80) == 0 &&
                (DwmGetWindowAttribute(handle, 14, out int cloaked, 4) != 0 || cloaked == 0) &&
                Describe(handle) is { } window && !Excluded(window.App, exclusions))
                windows.Add(window);

            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }

    internal static HubWindow? Describe(IntPtr handle)
    {
        GetWindowThreadProcessId(handle, out var pid);
        if (pid == 0 || pid == Environment.ProcessId)
            return null;
        var title = new StringBuilder(256);
        if (GetWindowTextW(handle, title, title.Capacity) == 0)
            return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (process.ProcessName == "WinLayer.Settings")
                return null;
            return new(handle.ToInt64(), title.ToString(), process.ProcessName, (int)pid);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    internal static bool Excluded(string app, string exclusions) =>
        exclusions.Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(value => string.Equals(value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value, app, StringComparison.OrdinalIgnoreCase));

    internal static Icon? WindowIcon(HubWindow window)
    {
        var handle = new IntPtr(window.Handle);
        if (!IsSame(window))
            return null;
        SendMessageTimeoutW(handle, 0x007F, new IntPtr(2), IntPtr.Zero, 0x2, 25, out var image);
        if (image == IntPtr.Zero)
            image = GetClassLongPtrW(handle, -34);
        if (image == IntPtr.Zero)
            image = GetClassLongPtrW(handle, -14);
        if (image == IntPtr.Zero)
            return null;
        using var borrowed = Icon.FromHandle(image);
        return (Icon)borrowed.Clone();
    }

    internal static bool IsSame(HubWindow window)
    {
        GetWindowThreadProcessId(new(window.Handle), out var pid);
        return pid == window.Pid;
    }

    internal static bool Activate(HubWindow window)
    {
        if (!IsSame(window))
            return false;
        var handle = new IntPtr(window.Handle);
        if (IsIconic(handle))
            ShowWindowAsync(handle, 9);
        return SetForegroundWindow(handle) || Active == handle;
    }

    internal static Rectangle? Bounds(long hwnd)
    {
        var handle = new IntPtr(hwnd);
        if (IsIconic(handle))
            return null;
        if (DwmGetWindowAttribute(handle, 9, out NativeRect rect, 16) != 0 && !GetWindowRect(handle, out rect))
            return null;
        var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width > 0 && bounds.Height > 0 ? bounds : null;
    }

    internal static Rectangle? Client(long hwnd)
    {
        var handle = new IntPtr(hwnd);
        var origin = Point.Empty;
        if (!GetClientRect(handle, out var rect) || !ClientToScreen(handle, ref origin) || rect.Right <= 0 || rect.Bottom <= 0)
            return null;
        return new Rectangle(origin.X, origin.Y, rect.Right, rect.Bottom);
    }

    internal static bool Topmost(long hwnd) => (GetWindowLongW(new(hwnd), -20) & 8) != 0;
    internal static bool SetTopmost(long hwnd, bool topmost) =>
        SetWindowPos(new(hwnd), new(topmost ? -1 : -2), 0, 0, 0, 0, 0x0013);
    internal static bool Exists(long hwnd) => IsWindow(new(hwnd));
    internal static bool IsVisible(long hwnd) => IsWindowVisible(new(hwnd));
    internal static IntPtr Taskbar => FindWindowW("Shell_TrayWnd", null);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate bool WindowCallback(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam,
        uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern IntPtr GetClassLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder title, int capacity);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr order, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern IntPtr FindWindowW(string className, string? name);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out NativeRect rectangle, int size);
}

internal abstract class HubOverlay : Form
{
    protected HubOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        DoubleBuffered = true;
        Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

internal sealed class DecorativeOverlay : HubOverlay
{
    private readonly byte _alpha;

    public DecorativeOverlay(Color color, int opacity)
    {
        BackColor = color;
        _alpha = (byte)Math.Round(255 * opacity / 100d);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetLayeredWindowAttributes(Handle, 0, _alpha, 2);
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x80000 | 0x20 | 0x80;
            return parameters;
        }
    }

    internal void Place(Rectangle? bounds, int border = 0, int radius = 0)
    {
        if (bounds is not { Width: > 0, Height: > 0 } area)
        {
            Hide();
            return;
        }

        if (Bounds != area)
        {
            Bounds = area;
            if (radius > 0)
            {
                var diameter = Math.Min(radius * 2, Math.Min(area.Width, area.Height));
                var width = area.Width - diameter;
                var height = area.Height - diameter;
                using var outline = new System.Drawing.Drawing2D.GraphicsPath();
                outline.AddArc(0, 0, diameter, diameter, 180, 90);
                outline.AddArc(width, 0, diameter, diameter, 270, 90);
                outline.AddArc(width, height, diameter, diameter, 0, 90);
                outline.AddArc(0, height, diameter, diameter, 90, 90);
                outline.CloseFigure();
                var previous = Region;
                Region = new Region(outline);
                previous?.Dispose();
            }
            else if (border > 0)
            {
                using var hole = new Region(new Rectangle(Point.Empty, area.Size));
                hole.Exclude(new Rectangle(border, border, Math.Max(0, area.Width - 2 * border), Math.Max(0, area.Height - 2 * border)));
                var previous = Region;
                Region = hole.Clone();
                previous?.Dispose();
            }
        }
        if (!Visible)
            Show();
    }

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);
}
