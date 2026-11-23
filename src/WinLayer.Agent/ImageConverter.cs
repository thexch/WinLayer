using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WinLayer.Agent;

internal static class ImageConverter
{
    public static Form Create()
    {
        var form = UtilityWindows.Create("Image Converter", new Size(760, 520));
        var files = new ListBox { Dock = DockStyle.Fill, AllowDrop = true, BackColor = UtilityWindows.Surface, ForeColor = UtilityWindows.Ink,
            AccessibleName = "Images à convertir", HorizontalScrollbar = true };
        var toolbar = UtilityWindows.Toolbar();
        var preview = new PictureBox { Dock = DockStyle.Right, Width = 225, SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = UtilityWindows.Canvas, AccessibleName = "Aperçu de la source sélectionnée" };
        var previewVersion = 0;
        files.SelectedIndexChanged += async (_, _) =>
        {
            var revision = ++previewVersion;
            preview.Image?.Dispose();
            preview.Image = null;
            if (files.SelectedItem is not string path) return;
            try
            {
                using var image = await Task.Run(() => LoadAsync(path, maxEdge: 240));
                if (!form.IsDisposed && revision == previewVersion)
                    preview.Image = new Bitmap(image);
            }
            catch (Exception error) { WindowsSettings.Log("Aperçu d’image.", error); }
        };
        var formats = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Format de sortie", Width = 80 };
        formats.Items.AddRange(["PNG", "JPEG", "BMP"]);
        var webp = BitmapEncoder.GetEncoderInformationEnumerator().FirstOrDefault(info => info.FileExtensions.Contains(".webp", StringComparer.OrdinalIgnoreCase));
        if (webp is not null) formats.Items.Add("WebP");
        formats.SelectedIndex = 0;
        var width = new NumericUpDown { AccessibleName = "Largeur en pixels, 0 conserve le rapport", Maximum = 8192, Width = 65 };
        var height = new NumericUpDown { AccessibleName = "Hauteur en pixels, 0 conserve le rapport", Maximum = 8192, Width = 65 };
        var keepRatio = new CheckBox { Text = "Proportions", AutoSize = true, Checked = true };
        var quality = new NumericUpDown { AccessibleName = "Qualité JPEG", Minimum = 1, Maximum = 100, Value = 90, Width = 60 };
        var feedback = new Label { Dock = DockStyle.Bottom, Height = 44, Padding = new(10, 7, 0, 0), Text = webp is null
            ? "Déposez vos images. Encodage WebP indisponible sur ce PC." : "Déposez vos images. Les fichiers d’origine sont conservés." };
        var progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 5 };
        CancellationTokenSource? cancellation = null;
        var convert = UtilityWindows.Button("Convertir…", () => { });
        var stop = UtilityWindows.Button("Arrêter", () => cancellation?.Cancel());
        stop.Enabled = false;
        convert.Click += async (_, _) => await ConvertAsync();
        toolbar.Controls.AddRange([
            UtilityWindows.Button("Ajouter…", () => { using var dialog = new OpenFileDialog { Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif;*.tiff|Tous les fichiers|*.*" }; if (dialog.ShowDialog(form) == DialogResult.OK) Add(dialog.FileNames); }),
            UtilityWindows.Button("Vider", () => files.Items.Clear()), formats, Tag("L"), width, Tag("H"), height, keepRatio, Tag("JPEG %"), quality,
            convert, stop
        ]);
        files.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        files.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) Add(paths); };
        form.FormClosed += (_, _) => { cancellation?.Cancel(); preview.Image?.Dispose(); };
        form.Controls.Add(files);
        form.Controls.Add(preview);
        form.Controls.Add(feedback);
        form.Controls.Add(progress);
        form.Controls.Add(toolbar);
        return form;

        static Label Tag(string label) => new() { Text = label, AutoSize = true, Margin = new(5, 8, 0, 0) };

        void Add(string[] paths)
        {
            foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (files.Items.Count >= 100) break;
                if (!files.Items.Contains(path)) files.Items.Add(path);
            }
        }

        async Task ConvertAsync()
        {
            if (files.Items.Count == 0) { feedback.Text = "Ajoutez au moins une image."; return; }
            using var folder = new FolderBrowserDialog { Description = "Choisir un dossier de destination", UseDescriptionForTitle = true };
            if (folder.ShowDialog(form) != DialogResult.OK) return;
            using var job = new CancellationTokenSource();
            cancellation = job;
            var paths = files.Items.Cast<string>().ToArray();
            var output = folder.SelectedPath;
            var format = (string)formats.SelectedItem!;
            var w = (int)width.Value;
            var h = (int)height.Value;
            var ratio = keepRatio.Checked;
            var jpeg = (long)quality.Value;
            progress.Value = 0;
            progress.Maximum = paths.Length;
            convert.Enabled = false;
            stop.Enabled = true;
            var success = 0;
            var failed = 0;
            var totalInput = 0L;
            var totalOutput = 0L;
            try
            {
                foreach (var path in paths)
                {
                    if (job.IsCancellationRequested || form.IsDisposed) break;
                    feedback.Text = $"Traitement de {Path.GetFileName(path)}…";
                    try
                    {
                        var result = await Task.Run(() => SaveAsync(path, output, format, webp?.CodecId, w, h, ratio, jpeg, job.Token), job.Token);
                        totalInput += result.Input;
                        totalOutput += result.Output;
                        success++;
                    }
                    catch (OperationCanceledException) when (job.IsCancellationRequested) { break; }
                    catch (Exception error) { failed++; WindowsSettings.Log("Conversion d’une image.", error); }
                    if (!form.IsDisposed) progress.Value++;
                }
                if (!form.IsDisposed)
                    feedback.Text = $"{success} image(s) · {failed} erreur(s){(job.IsCancellationRequested ? " · Annulé" : "")}   |   {totalInput / 1024:N0} Ko → {totalOutput / 1024:N0} Ko";
            }
            finally
            {
                cancellation = null;
                if (!form.IsDisposed) { convert.Enabled = true; stop.Enabled = false; }
        convert.Click += async (_, _) => await ConvertAsync();
            }
        }
    }

    private static async Task<(long Input, long Output)> SaveAsync(string path, string directory, string format, Guid? webp, int w, int h, bool ratio, long quality, CancellationToken token)
    {
        var original = new FileInfo(path);
        if (original.Length > 100 * 1024 * 1024) throw new InvalidDataException("Image trop volumineuse.");
        using var source = await LoadAsync(path);
        if (ratio || w == 0 || h == 0)
        {
            var scale = Math.Min(w > 0 ? (double)w / source.Width : double.PositiveInfinity,
                h > 0 ? (double)h / source.Height : double.PositiveInfinity);
            if (double.IsInfinity(scale)) scale = 1;
            w = Math.Max(1, (int)Math.Round(source.Width * scale));
            h = Math.Max(1, (int)Math.Round(source.Height * scale));
        }
        if ((long)w * h > 32_000_000) throw new InvalidDataException("Dimensions maximales dépassées.");
        using var resized = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var canvas = Graphics.FromImage(resized))
        {
            if (format is "JPEG" or "BMP") canvas.Clear(Color.White);
            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
            canvas.DrawImage(source, new Rectangle(0, 0, w, h));
        }
        using var data = new MemoryStream();
        if (format == "JPEG")
        {
            var encoder = ImageCodecInfo.GetImageEncoders().Single(info => info.FormatID == ImageFormat.Jpeg.Guid);
            using var options = new EncoderParameters(1);
            options.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            resized.Save(data, encoder, options);
        }
        else if (format == "WebP" && webp is Guid codec)
        {
            var bitmap = resized.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buffer = new byte[w * h * 4];
            try
            {
                for (var y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(bitmap.Scan0 + y * bitmap.Stride, buffer, y * w * 4, w * 4);
            }
            finally { resized.UnlockBits(bitmap); }
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(codec, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)w, (uint)h, 96, 96, buffer);
            await encoder.FlushAsync();
            using var input = stream.GetInputStreamAt(0);
            using var reader = new DataReader(input);
            var bytes = new byte[checked((int)stream.Size)];
            if (await reader.LoadAsync((uint)bytes.Length) != bytes.Length) throw new EndOfStreamException();
            reader.ReadBytes(bytes);
            data.Write(bytes);
        }
        else
            resized.Save(data, format == "BMP" ? ImageFormat.Bmp : ImageFormat.Png);

        token.ThrowIfCancellationRequested();
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = format == "JPEG" ? "jpg" : format.ToLowerInvariant();
        var output = Path.Combine(directory, $"{name}.{ext}");
        for (var index = 2; ; index++)
        {
            try
            {
                using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                try { data.WriteTo(file); file.Flush(); }
                catch { file.Dispose(); File.Delete(output); throw; }
                return (original.Length, data.Length);
            }
            catch (IOException) when (File.Exists(output) && index < 1000)
            {
                output = Path.Combine(directory, $"{name} ({index}).{ext}");
            }
        }
    }

    private static async Task<Bitmap> LoadAsync(string path, uint maxEdge = 0)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        if ((long)decoder.OrientedPixelWidth * decoder.OrientedPixelHeight > 32_000_000)
            throw new InvalidDataException("Dimensions maximales dépassées.");
        var transform = new BitmapTransform();
        if (maxEdge > 0 && Math.Max(decoder.PixelHeight, decoder.PixelWidth) > maxEdge)
        {
            var scale = maxEdge / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            transform.ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * scale));
            transform.ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * scale));
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
        using var pixels = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        using var png = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
        encoder.SetSoftwareBitmap(pixels);
        await encoder.FlushAsync();
        png.Seek(0);
        using var input = png.GetInputStreamAt(0);
        using var reader = new DataReader(input);
        var buffer = new byte[checked((int)png.Size)];
        if (await reader.LoadAsync((uint)buffer.Length) != buffer.Length) throw new EndOfStreamException();
        reader.ReadBytes(buffer);
        using var data = new MemoryStream(buffer);
        using var bitmap = new Bitmap(data);
        return new Bitmap(bitmap);
    }
}
