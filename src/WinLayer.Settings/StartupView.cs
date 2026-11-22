using System.Diagnostics;
using System.Drawing.Drawing2D;
using Appearance = WinLayer.Core.Appearance;

namespace WinLayer.Settings;

internal sealed class StartupView : Control
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 33 };
    private readonly Stopwatch _clock = new();
    private readonly Font _titleFont = new("Segoe UI", 27, FontStyle.Bold);
    private readonly Font _messageFont = new("Segoe UI", 12);
    private Color _ink;
    private Color _muted;
    private Color _accent;
    private Color _track;
    private string? _error;

    public StartupView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Fill;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = "Ouverture des réglages WinLayer";
        _animation.Tick += (_, _) => Invalidate(Ring());
    }

    public void ApplyTheme(Appearance appearance)
    {
        var dark = CaptionTheme.IsDark(appearance);
        BackColor = SystemInformation.HighContrast ? SystemColors.Window : dark ? Color.FromArgb(20, 23, 43) : Color.FromArgb(244, 245, 252);
        _ink = SystemInformation.HighContrast ? SystemColors.WindowText : dark ? Color.FromArgb(241, 239, 254) : Color.FromArgb(25, 29, 52);
        _muted = SystemInformation.HighContrast ? SystemColors.WindowText : dark ? Color.FromArgb(180, 189, 220) : Color.FromArgb(80, 89, 120);
        _accent = SystemInformation.HighContrast ? SystemColors.Highlight : dark ? Color.FromArgb(183, 170, 255) : Color.FromArgb(75, 64, 171);
        _track = SystemInformation.HighContrast ? SystemColors.GrayText : dark ? Color.FromArgb(49, 55, 82) : Color.FromArgb(220, 224, 238);
        Invalidate();
    }

    public void Start()
    {
        if (_error is not null || !SystemInformation.IsMenuAnimationEnabled)
            return;
        _clock.Start();
        _animation.Start();
    }

    public void Stop()
    {
        _animation.Stop();
        _clock.Stop();
    }

    public void ShowError(string error)
    {
        Stop();
        _error = error;
        AccessibleName = error;
        AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var middle = ClientSize.Height / 2;
        if (_error is null)
        {
            var ring = Ring();
            ring.Inflate(-Scale(5), -Scale(5));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var track = new Pen(_track, Scale(3));
            using var accent = new Pen(_accent, Scale(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawEllipse(track, ring);
            e.Graphics.DrawArc(accent, ring, -90 + (float)(_clock.ElapsedMilliseconds % 1050) * 360 / 1050, 94);
        }

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(e.Graphics, "WinLayer", _titleFont,
            new Rectangle(0, middle - Scale(21), ClientSize.Width, Scale(46)), _ink, flags);
        TextRenderer.DrawText(e.Graphics, _error ?? "Ouverture des réglages…", _messageFont,
            new Rectangle(Scale(24), middle + Scale(28), Math.Max(1, ClientSize.Width - Scale(48)), Scale(58)),
            _muted, flags | TextFormatFlags.WordBreak);
    }

    private Rectangle Ring() => new((ClientSize.Width - Scale(56)) / 2, ClientSize.Height / 2 - Scale(85), Scale(56), Scale(56));
    private int Scale(int value) => LogicalToDeviceUnits(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation.Dispose();
            _titleFont.Dispose();
            _messageFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
