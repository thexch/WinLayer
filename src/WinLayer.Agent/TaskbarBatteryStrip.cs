using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinLayer.Core;

namespace WinLayer.Agent;

internal sealed partial class TaskbarBatteryStrip : NativeWindow, IDisposable
{
    private readonly Action _openBattery;
    private readonly System.Windows.Forms.Timer _layoutTimer = new() { Interval = 1500 };
    private BatteryTrayDisplay[] _devices = [];
    private nint _parent;
    private Point _position;
    private bool _visible;
    private bool _dirty = true;
    private uint _dpi;
    private int _availableWidth;
    private int _height;
    private Color _foreground;
    private Size _rendered;

    internal TaskbarBatteryStrip(Action openBattery)
    {
        _openBattery = openBattery;
        _layoutTimer.Tick += (_, _) => Relayout();
        _layoutTimer.Start();
    }

    public void Update(BatteryTrayDisplay[] devices)
    {
        _dirty |= !_devices.AsSpan().SequenceEqual(devices);
        _devices = devices;
        Relayout();
    }

    private void Relayout()
    {
        try
        {
            PositionStrip();
        }
        catch (ExternalException)
        {
            _dirty = true;
            Hide();
        }
    }

    private void PositionStrip()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        var notificationArea = taskbar == 0 ? 0 : FindWindowEx(taskbar, 0, "TrayNotifyWnd", null);
        var origin = new NativePoint();
        if (taskbar == 0 || notificationArea == 0 || IsWindowVisible(taskbar) == 0 ||
            GetClientRect(taskbar, out var client) == 0 || ClientToScreen(taskbar, ref origin) == 0 ||
            GetWindowRect(notificationArea, out var tray) == 0)
        {
            Hide();
            return;
        }

        var dpi = Math.Max(96, GetDpiForWindow(taskbar));
        var scale = dpi / 96f;
        var bounds = new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        var band = TaskbarBatteryLayout.FindBand(bounds, Rectangle.FromLTRB(tray.Left, tray.Top, tray.Right, tray.Bottom),
            (int)Math.Ceiling(5 * scale));
        if (band.IsEmpty || band.Width < 32 * scale)
        {
            Hide();
            return;
        }

        if (_parent != taskbar || Handle == 0)
        {
            Hide();
            if (Handle != 0)
                DestroyHandle();
            try
            {
                CreateHandle(new CreateParams
                {
                    Parent = taskbar,
                    Caption = "WinLayer — Batteries des périphériques",
                    Style = 0x40000000 | 0x04000000, // WS_CHILD | WS_CLIPSIBLINGS
                    ExStyle = 0x00080000 | 0x08000000, // WS_EX_LAYERED | WS_EX_NOACTIVATE
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return;
            }

            _parent = taskbar;
            _dirty = true;
        }

        var foreground = ReadTaskbarForeground();
        if (_dirty || _dpi != dpi || _availableWidth != band.Width || _height != band.Height || _foreground != foreground)
        {
            using var bitmap = DrawStrip(band.Width, band.Height, scale, foreground);
            if (bitmap is null || !UpdateBitmap(bitmap))
            {
                Hide();
                return;
            }

            _rendered = bitmap.Size;
            _dpi = dpi;
            _availableWidth = band.Width;
            _height = band.Height;
            _foreground = foreground;
            _dirty = false;
        }

        var position = new Point(band.Right - _rendered.Width - origin.X, band.Top - origin.Y);
        if (!_visible || position != _position)
        {
            if (SetWindowPos(Handle, 0, position.X, position.Y, _rendered.Width, _rendered.Height,
                0x0010 | 0x0040) != 0) // SWP_NOACTIVATE | SWP_SHOWWINDOW, top of the taskbar's children.
            {
                _position = position;
                _visible = true;
            }
        }
    }

    private Bitmap? DrawStrip(int capacity, int height, float scale, Color foreground)
    {
        using var scratch = new Bitmap(1, 1);
        using var measure = Graphics.FromImage(scratch);
        using var font = new Font("Segoe UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        var inset = (int)Math.Ceiling(5 * scale);
        var glyphSize = (int)Math.Ceiling(13 * scale);
        var gap = (int)Math.Ceiling(5 * scale);
        var spacing = (int)Math.Ceiling(11 * scale);
        var width = inset;
        var rows = new List<(BatteryTrayDisplay Device, string Percent, int X)>();

        foreach (var device in _devices)
        {
            var percent = device.Percentage.ToString(CultureInfo.InvariantCulture) + "%";
            var textWidth = (int)Math.Ceiling(measure.MeasureString(percent, font, PointF.Empty, format).Width);
            var rowWidth = glyphSize + gap + textWidth;
            var x = rows.Count == 0 ? width : width + spacing;
            if (x + rowWidth + inset > capacity)
                break;
            rows.Add((device, percent, x));
            width = x + rowWidth;
        }

        if (rows.Count == 0)
            return null;

        var bitmap = new Bitmap(width + inset, height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var ink = new SolidBrush(foreground);
        var textTop = (height - font.GetHeight(graphics)) / 2;
        foreach (var row in rows)
        {
            var symbolTop = (height - glyphSize) / 2f;
            DrawGlyph(graphics, new(row.X, symbolTop, glyphSize, glyphSize), foreground, row.Device.Glyph);
            graphics.DrawString(row.Percent, font, ink, new PointF(row.X + glyphSize + gap, textTop), format);
        }

        return bitmap;
    }

    private static void DrawGlyph(Graphics graphics, RectangleF bounds, Color foreground, BatteryTrayGlyph glyph)
    {
        var save = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 13, bounds.Height / 13);
        using var pen = new Pen(foreground, 1.15f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (glyph)
        {
            case BatteryTrayGlyph.Headset:
                graphics.DrawArc(pen, 1, 1, 11, 10, 180, 180);
                graphics.DrawRectangle(pen, 1, 6, 2.5f, 5);
                graphics.DrawRectangle(pen, 9.5f, 6, 2.5f, 5);
                break;
            case BatteryTrayGlyph.Mouse:
                graphics.DrawEllipse(pen, 3, 0.8f, 7, 11.4f);
                graphics.DrawLine(pen, 6.5f, 3, 6.5f, 5);
                break;
            case BatteryTrayGlyph.Gamepad:
                graphics.DrawPolygon(pen, [new PointF(3, 3.5f), new(10, 3.5f), new(12, 9), new(10, 10.5f),
                    new(8, 8.5f), new(5, 8.5f), new(3, 10.5f), new(1, 9)]);
                graphics.DrawLine(pen, 3, 6.5f, 5, 6.5f);
                graphics.DrawLine(pen, 4, 5.5f, 4, 7.5f);
                graphics.DrawLine(pen, 9, 6, 9.6f, 6);
                break;
            case BatteryTrayGlyph.Keyboard:
                graphics.DrawRectangle(pen, 0.8f, 3, 11.4f, 7);
                for (var x = 3; x <= 9; x += 3)
                    graphics.DrawLine(pen, x, 5.5f, x + 0.35f, 5.5f);
                graphics.DrawLine(pen, 4, 7.5f, 9, 7.5f);
                break;
            default:
                graphics.DrawRectangle(pen, 1, 3.5f, 9, 6);
                graphics.DrawLine(pen, 11.5f, 5.5f, 11.5f, 7.5f);
                graphics.DrawLine(pen, 3, 6.5f, 7, 6.5f);
                break;
        }
        graphics.Restore(save);
    }

    private bool UpdateBitmap(Bitmap bitmap)
    {
        var dc = CreateCompatibleDC(0);
        if (dc == 0)
            return false;

        try
        {
            var handle = bitmap.GetHbitmap(Color.FromArgb(0));
            try
            {
                var oldBitmap = SelectObject(dc, handle);
                if (oldBitmap == 0 || oldBitmap == -1)
                    return false;

                try
                {
                    var size = new NativeSize { Width = bitmap.Width, Height = bitmap.Height };
                    var source = new NativePoint();
                    var blend = new BlendFunction { ConstantAlpha = 255, AlphaFormat = 1 };
                    return UpdateLayeredWindow(Handle, 0, 0, in size, dc, in source, 0, in blend, 2) != 0;
                }
                finally
                {
                    SelectObject(dc, oldBitmap);
                }
            }
            finally
            {
                DeleteObject(handle);
            }
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    private static Color ReadTaskbarForeground()
    {
        if (SystemInformation.HighContrast)
            return SystemColors.WindowText;
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0) is 1 ? Color.FromArgb(28, 29, 33) : Color.FromArgb(250, 250, 252);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Color.White;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0202) // WM_LBUTTONUP
            _openBattery();
        base.WndProc(ref message);
    }

    private void Hide()
    {
        if (_visible && Handle != 0)
            ShowWindow(Handle, 0);
        _visible = false;
    }

    public void Dispose()
    {
        _layoutTimer.Dispose();
        if (Handle != 0)
            DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction { public byte Operation, Flags, ConstantAlpha, AlphaFormat; }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string className, string? title);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint after, string className, string? title);

    [LibraryImport("user32.dll")]
    private static partial int IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowRect(nint window, out NativeRect rect);

    [LibraryImport("user32.dll")]
    private static partial int GetClientRect(nint window, out NativeRect rect);

    [LibraryImport("user32.dll")]
    private static partial int ClientToScreen(nint window, ref NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    private static partial int UpdateLayeredWindow(nint window, nint target, nint destination, in NativeSize size,
        nint source, in NativePoint point, uint colorKey, in BlendFunction blend, uint flags);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint handle);

    [LibraryImport("gdi32.dll")]
    private static partial int DeleteObject(nint handle);

    [LibraryImport("gdi32.dll")]
    private static partial int DeleteDC(nint dc);
}
