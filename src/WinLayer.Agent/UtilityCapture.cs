using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using WinLayer.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WinLayer.Agent;

internal sealed class CaptureRegion : HubOverlay
{
    private readonly Bitmap _frame;
    private readonly Action<Bitmap> _selected;
    private Point? _anchor;
    private Rectangle _selection;

    public CaptureRegion(Action<Bitmap> selected)
    {
        _selected = selected;
        Bounds = SystemInformation.VirtualScreen;
        AccessibleName = "Sélection de capture WinLayer";
        Cursor = Cursors.Cross;
        if ((long)Width * Height > 64_000_000)
            throw new InvalidOperationException("Le bureau est trop grand pour cette capture.");
        _frame = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
        try
        {
            using var canvas = Graphics.FromImage(_frame);
            foreach (var screen in Screen.AllScreens)
                canvas.CopyFromScreen(screen.Bounds.Location,
                    new Point(screen.Bounds.X - Left, screen.Bounds.Y - Top), screen.Bounds.Size);
        }
        catch
        {
            _frame.Dispose();
            throw;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(_frame, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(140, 11, 10, 24));
        using var outside = new Region(ClientRectangle);
        outside.Exclude(_selection);
        e.Graphics.FillRegion(shade, outside);
        if (!_selection.IsEmpty)
        {
            using var line = new Pen(UtilityWindows.Accent, 2);
            e.Graphics.DrawRectangle(line, _selection);
        }
        TextRenderer.DrawText(e.Graphics, "Sélectionnez une zone   ·   Échap pour annuler", Font, new Point(24, 24), Color.White, Color.FromArgb(25, 24, 42));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) Close();
        else if (e.Button == MouseButtons.Left) { _anchor = e.Location; Capture = true; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_anchor is not Point start) return;
        _selection = Rectangle.Intersect(ClientRectangle, Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y),
            Math.Max(start.X, e.X), Math.Max(start.Y, e.Y)));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_anchor is null || e.Button != MouseButtons.Left) return;
        OnMouseMove(e);
        _anchor = null;
        Capture = false;
        if (_selection.Width < 4 || _selection.Height < 4) return;
        var crop = _frame.Clone(_selection, _frame.PixelFormat);
        Close();
        _selected(crop);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _frame?.Dispose();
        base.Dispose(disposing);
    }
}

internal static class UtilityCapture
{
    public static Form Pin(Bitmap image, Point cursor, Action closeAll)
    {
        var form = UtilityWindows.Create("Capture épinglée", new Size(Math.Clamp(image.Width, 380, 960), Math.Clamp(image.Height + 42, 180, 720)));
        var area = Screen.FromPoint(cursor).WorkingArea;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new(Math.Clamp(cursor.X, area.Left, Math.Max(area.Left, area.Right - form.Width)),
                            Math.Clamp(cursor.Y, area.Top, Math.Max(area.Top, area.Bottom - form.Height)));
        var canvas = new Panel { AutoScroll = true, Dock = DockStyle.Fill };
        var picture = new PictureBox { Image = image, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
        canvas.Controls.Add(picture);
        var toolbar = UtilityWindows.Toolbar();
        toolbar.Controls.Add(UtilityWindows.Button("Copier", () => Clipboard.SetImage(image)));
        toolbar.Controls.Add(UtilityWindows.Button("Enregistrer", () => UtilityWindows.SavePng(form, image)));
        toolbar.Controls.Add(UtilityWindows.Button("Fermer les captures", closeAll));
        var top = new CheckBox { Text = "Premier plan", Checked = true, AutoSize = true, Margin = new Padding(6, 10, 6, 0) };
        top.CheckedChanged += (_, _) => form.TopMost = top.Checked;
        toolbar.Controls.Add(top);
        var zoom = new TrackBar { AccessibleName = "Zoom", Minimum = 0, Maximum = 400, TickStyle = TickStyle.None, Width = 90 };
        zoom.ValueChanged += (_, _) =>
        {
            picture.Dock = zoom.Value == 0 ? DockStyle.Fill : DockStyle.None;
            if (zoom.Value != 0) picture.Size = new(Math.Max(1, image.Width * zoom.Value / 100), Math.Max(1, image.Height * zoom.Value / 100));
        };
        toolbar.Controls.Add(zoom);
        var opacity = new TrackBar { AccessibleName = "Opacité", Minimum = 25, Maximum = 100, Value = 100, TickStyle = TickStyle.None, Width = 90 };
        opacity.ValueChanged += (_, _) => form.Opacity = opacity.Value / 100d;
        toolbar.Controls.Add(opacity);
        form.Controls.Add(canvas);
        form.Controls.Add(toolbar);
        form.Disposed += (_, _) => image.Dispose();
        return form;
    }

    public static Form Ocr(Bitmap image, string language, bool autoCopy, Action<string>? history)
    {
        var form = UtilityWindows.Create("Screen OCR", new Size(700, 470));
        var text = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true,
            AccessibleName = "Texte reconnu et modifiable", BackColor = UtilityWindows.Surface, ForeColor = UtilityWindows.Ink, BorderStyle = BorderStyle.FixedSingle };
        var toolbar = UtilityWindows.Toolbar();
        var feedback = new Label { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(12, 8, 0, 0), ForeColor = UtilityWindows.Ink };
        var languages = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Langue", Width = 110 };
        languages.Items.AddRange(["Français", "English"]);
        languages.SelectedIndex = language == "fr" ? 0 : 1;
        var retry = UtilityWindows.Button("Reconnaître", () => { });
        retry.Click += async (_, _) => await RecognizeAsync();
        toolbar.Controls.AddRange([languages, retry, UtilityWindows.Button("Copier le texte", () => { if (text.TextLength > 0) Clipboard.SetText(text.Text); })]);
        form.Controls.Add(text);
        form.Controls.Add(feedback);
        form.Controls.Add(toolbar);
        form.Disposed += (_, _) => image.Dispose();
        form.Shown += async (_, _) => await RecognizeAsync();
        return form;

        async Task RecognizeAsync()
        {
            retry.Enabled = false;
            feedback.Text = "Reconnaissance locale en cours…";
            try
            {
                var locale = languages.SelectedIndex == 0 ? "fr-FR" : "en-US";
                var engine = OcrEngine.TryCreateFromLanguage(new Language(locale));
                if (engine is null)
                {
                    feedback.Text = $"Installez la fonctionnalité de reconnaissance optique pour {locale} dans les paramètres Windows.";
                    return;
                }

                var scale = Math.Min(1d, OcrEngine.MaxImageDimension / (double)Math.Max(image.Width, image.Height));
                using var resized = new Bitmap(image, new Size(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))));
                using var png = new MemoryStream();
                resized.Save(png, ImageFormat.Png);
                using var source = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(source))
                {
                    writer.WriteBytes(png.ToArray());
                    await writer.StoreAsync();
                    writer.DetachStream();
                }
                source.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(source);
                using var pixels = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
                var result = await engine.RecognizeAsync(pixels);
                if (form.IsDisposed) return;
                text.Text = string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
                feedback.Text = text.TextLength == 0 ? "Aucun texte détecté." : "Texte reconnu. Vous pouvez le modifier.";
                if (text.TextLength > 0)
                {
                    history?.Invoke(text.Text);
                    if (autoCopy) Clipboard.SetText(text.Text);
                }
            }
            catch (Exception error)
            {
                WindowsSettings.Log("Reconnaissance de texte.", error);
                if (!form.IsDisposed) feedback.Text = "La reconnaissance ou la copie a échoué.";
            }
            finally { if (!form.IsDisposed) retry.Enabled = true; }
        }
    }
}

internal static class UtilityWindows
{
    internal static AppTheme? Theme { get; set; }
    internal static Color Canvas => Theme is { } theme ? ColorTranslator.FromHtml(theme.Canvas) : Color.FromArgb(20, 23, 43);
    internal static Color Surface => Theme is { } theme ? ColorTranslator.FromHtml(theme.Surface) : Color.FromArgb(37, 38, 66);
    internal static Color Ink => Theme is { } theme ? ColorTranslator.FromHtml(theme.Ink) : Color.FromArgb(242, 237, 255);
    internal static Color Accent => Theme is { } theme ? ColorTranslator.FromHtml(theme.Accent) : Color.FromArgb(187, 170, 255);
    internal static Color Border => Theme is { } theme ? ColorTranslator.FromHtml(theme.Border) : Color.FromArgb(78, 72, 115);

    public static Form Create(string title, Size size)
    {
        var form = new Form { Text = title + " — WinLayer", Size = size, MinimumSize = new Size(300, 155),
            TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterScreen,
            Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, BackColor = Canvas, ForeColor = Ink, KeyPreview = true };
        form.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) form.Close(); };
        return form;
    }

    public static FlowLayoutPanel Toolbar() => new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), BackColor = Canvas };

    public static Button Button(string title, Action execute)
    {
        var button = new Button { Text = title, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Ink, BackColor = Surface, MinimumSize = new Size(0, 30) };
        button.FlatAppearance.BorderColor = Border;
        button.Click += (_, _) =>
        {
            try { execute(); }
            catch (Exception error)
            {
                WindowsSettings.Log("Action de l’utilitaire.", error);
                MessageBox.Show(button.FindForm(), "L’action n’a pas pu être terminée. Consultez le journal WinLayer.", "WinLayer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        return button;
    }

    public static Bitmap Glyph(string icon)
    {
        var symbol = icon switch
        {
            "home" => '\uE80F', "battery" => '\uEBAA', "mouse" => '\uE962', "keyboard" => '\uE765', "macro" => '\uE7C8',
            "pin" => '\uE718', "windows" => '\uE737', "layers" => '\uE81E', "screen" => '\uE7F4', "color" => '\uE790',
            "text" => '\uED0E', "folder" => '\uE8B7', "circle" => '\uEA3A', "search" => '\uE721', "image" => '\uE91B',
            "network" => '\uE701', "cpu" => '\uE950', "download" => '\uE896', _ => '\uE713',
        };
        var bitmap = new Bitmap(18, 18, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Segoe Fluent Icons", 15, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Accent);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphics.DrawString(symbol.ToString(), font, brush, PointF.Empty);
        return bitmap;
    }

    public static void FadeIn(Form form, double opacity)
    {
        if (Theme?.Motion == false || !SystemInformation.IsMenuAnimationEnabled)
        {
            form.Opacity = opacity;
            return;
        }

        form.Opacity = Math.Min(0.2, opacity);
        var timer = new System.Windows.Forms.Timer { Interval = 16 };
        var start = System.Diagnostics.Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            var fraction = Math.Min(1, start.Elapsed.TotalMilliseconds / 135);
            form.Opacity = Math.Max(0.01, opacity * (1 - (1 - fraction) * (1 - fraction)));
            if (fraction >= 1) timer.Dispose();
        };
        form.Disposed += (_, _) => timer.Dispose();
        timer.Start();
    }

    public static void SavePng(IWin32Window owner, Image image)
    {
        using var dialog = new SaveFileDialog { Filter = "Image PNG (*.png)|*.png", DefaultExt = "png", AddExtension = true };
        if (dialog.ShowDialog(owner) == DialogResult.OK)
            image.Save(dialog.FileName, ImageFormat.Png);
    }
}
