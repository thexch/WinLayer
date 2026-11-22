using WinLayer.Core;
using WinLayer.Modules;

namespace WinLayer.Agent;

internal sealed class ScreenOverlay : HubOverlay
{
    private readonly (Rectangle Area, Bitmap Bitmap)[] _frames;
    private readonly HubOptions _options;
    private readonly bool _ruler;
    private readonly Action<string> _result;
    private readonly Point _origin;
    private Point _cursor;
    private Point? _first;
    private Point? _second;
    private int _drag;

    public ScreenOverlay(HubOptions options, bool ruler, Action<string> result)
    {
        _options = options;
        _ruler = ruler;
        _result = result;
        AccessibleName = ruler ? "Screen Ruler" : "Color Picker";
        Bounds = SystemInformation.VirtualScreen;
        _origin = Bounds.Location;
        _cursor = Relative(Cursor.Position);
        Cursor = Cursors.Cross;
        var areas = Screen.AllScreens.Select(screen => screen.Bounds).ToArray();
        if (areas.Sum(area => (long)area.Width * area.Height) > 64_000_000)
            throw new InvalidOperationException("Le bureau est trop grand pour la capture en mémoire.");
        var frames = new List<(Rectangle, Bitmap)>();
        try
        {
            foreach (var area in areas)
            {
                var bitmap = new Bitmap(area.Width, area.Height);
                frames.Add((area, bitmap));
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
            }
            _frames = frames.ToArray();
        }
        catch
        {
            foreach (var (_, bitmap) in frames)
                bitmap.Dispose();
            throw;
        }
    }

    private Point Relative(Point point) => new(point.X - _origin.X, point.Y - _origin.Y);
    private Point Global(Point point) => new(point.X + _origin.X, point.Y + _origin.Y);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _cursor = e.Location;
        if (_drag == 1) _first = _cursor;
        if (_drag == 2) _second = _cursor;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        _cursor = e.Location;
        if (!_ruler)
        {
            SelectColor();
            return;
        }

        if (_first is { } first && _second is { } second)
        {
            _drag = Distance(first, _cursor) < Distance(second, _cursor) ? 1 : 2;
            Capture = true;
        }
        else if (_first is null)
            _first = _cursor;
        else
            _second = _cursor;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = 0;
        Capture = false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && !_ruler)
        {
            SelectColor();
            return true;
        }
        if (_ruler && keyData is Keys.C or Keys.Enter && _first is { } a && _second is { } b)
        {
            var value = HubCalculations.Measure(a.X, a.Y, b.X, b.Y, _options.RulerMode);
            TryCopy(value);
            _result(value);
            return true;
        }
        if (_ruler && keyData == Keys.Back)
        {
            _first = _second = null;
            Invalidate();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static double Distance(Point a, Point b) => Math.Abs((double)a.X - b.X) + Math.Abs((double)a.Y - b.Y);

    private (Rectangle Area, Bitmap Bitmap)? Frame()
    {
        var point = Global(_cursor);
        foreach (var frame in _frames)
            if (frame.Area.Contains(point))
                return frame;
        return null;
    }

    private void SelectColor()
    {
        if (Frame() is not { } frame)
            return;
        var global = Global(_cursor);
        var color = frame.Bitmap.GetPixel(global.X - frame.Area.X, global.Y - frame.Area.Y);
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _result(hex);
        if (_options.AutoCopy)
            TryCopy(HubCalculations.FormatColor(hex, _options.ColorFormat));
        Close();
    }

    private static void TryCopy(string value)
    {
        try { Clipboard.SetText(value); }
        catch (System.Runtime.InteropServices.ExternalException error) { WindowsSettings.Log("Presse-papiers occupé.", error); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        foreach (var (area, frame) in _frames)
            e.Graphics.DrawImageUnscaled(frame, Relative(area.Location));

        if (_ruler)
            DrawRuler(e.Graphics);
        else
            DrawPicker(e.Graphics);
    }

    private void DrawRuler(Graphics graphics)
    {
        if (_first is not { } first)
        {
            DrawLabel(graphics, "Cliquez sur le premier point · Échap pour fermer", _cursor);
            return;
        }
        var end = _second ?? _cursor;
        var projected = _options.RulerMode switch
        {
            RulerMode.Horizontal => new Point(end.X, first.Y),
            RulerMode.Vertical => new Point(first.X, end.Y),
            _ => end
        };
        using var shadow = new Pen(Color.Black, 5);
        using var accent = new Pen(Color.Aqua, 2);
        graphics.DrawLine(shadow, first, projected);
        graphics.DrawLine(accent, first, projected);
        foreach (var point in new[] { first, projected })
        {
            graphics.FillEllipse(Brushes.Black, point.X - 5, point.Y - 5, 10, 10);
            graphics.FillEllipse(Brushes.Aqua, point.X - 3, point.Y - 3, 6, 6);
        }
        var measure = HubCalculations.Measure(first.X, first.Y, end.X, end.Y, _options.RulerMode);
        DrawLabel(graphics, _second is null ? measure : $"{measure} · C pour copier", end);
    }

    private void DrawPicker(Graphics graphics)
    {
        if (Frame() is not { } frame)
            return;

        var global = Global(_cursor);
        var x = global.X - frame.Area.X;
        var y = global.Y - frame.Area.Y;
        var source = Rectangle.Intersect(new Rectangle(x - 4, y - 4, 9, 9), new Rectangle(Point.Empty, frame.Bitmap.Size));
        var size = 9 * _options.Zoom;
        var location = NearCursor(frame.Area, size + 10, size + 42);
        graphics.FillRectangle(Brushes.Black, location.X, location.Y, size + 10, size + 42);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        var left = location.X + 5 + (source.Left - x + 4) * _options.Zoom;
        var top = location.Y + 5 + (source.Top - y + 4) * _options.Zoom;
        graphics.DrawImage(frame.Bitmap, new Rectangle(left, top, source.Width * _options.Zoom, source.Height * _options.Zoom), source, GraphicsUnit.Pixel);
        var center = new Rectangle(location.X + 5 + 4 * _options.Zoom, location.Y + 5 + 4 * _options.Zoom, _options.Zoom, _options.Zoom);
        graphics.DrawRectangle(Pens.White, center);
        graphics.DrawRectangle(Pens.Black, Rectangle.Inflate(center, 1, 1));
        var pixel = frame.Bitmap.GetPixel(x, y);
        TextRenderer.DrawText(graphics, $"#{pixel.R:X2}{pixel.G:X2}{pixel.B:X2}", Font, new Point(location.X + 8, location.Y + size + 10), Color.White);
    }

    private Point NearCursor(Rectangle monitor, int width, int height)
    {
        var area = new Rectangle(Relative(monitor.Location), monitor.Size);
        return new Point(Math.Clamp(_cursor.X + 18, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(_cursor.Y + 22, area.Top, Math.Max(area.Top, area.Bottom - height)));
    }

    private void DrawLabel(Graphics graphics, string label, Point anchor)
    {
        var size = TextRenderer.MeasureText(label, Font);
        var area = Screen.FromPoint(Global(anchor)).Bounds;
        var point = NearCursor(area, size.Width + 20, size.Height + 12);
        graphics.FillRectangle(Brushes.Black, point.X, point.Y, size.Width + 20, size.Height + 12);
        TextRenderer.DrawText(graphics, label, Font, new Point(point.X + 10, point.Y + 6), Color.White);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _frames is not null)
            foreach (var (_, frame) in _frames)
                frame.Dispose();
        base.Dispose(disposing);
    }
}
