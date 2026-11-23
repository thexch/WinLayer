namespace WinLayer.Agent;

internal sealed class DropShelf : IDisposable
{
    private sealed record Entry(object Value, string Label, DateTimeOffset Expires, bool Pinned = false)
    {
        public override string ToString() => Pinned ? "◆  " + Label : Label;
    }

    private readonly List<Entry> _items = [];
    private readonly System.Windows.Forms.Timer _expiry = new() { Interval = 60_000 };
    private Form? _form;
    private ListBox? _list;
    private Point _mouseDown;
    private int _hours;

    public DropShelf() => _expiry.Tick += (_, _) =>
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in _items.Where(item => !item.Pinned && item.Expires <= now).ToArray())
            Remove(entry);
        Refresh();
    };

    public void Toggle(int hours, Size size, int position)
    {
        _hours = hours;
        if (_form is not null) { _form.Close(); return; }
        var form = UtilityWindows.Create("Drop Shelf", size);
        _form = form;
        form.StartPosition = FormStartPosition.Manual;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        form.Location = new(position % 2 == 0 ? area.Left + 16 : Math.Max(area.Left, area.Right - form.Width - 16),
            position < 2 ? area.Top + 16 : Math.Max(area.Top, area.Bottom - form.Height - 16));
        var list = new ListBox { Dock = DockStyle.Fill, AllowDrop = true, AccessibleName = "Éléments temporaires",
            BackColor = UtilityWindows.Surface, ForeColor = UtilityWindows.Ink, BorderStyle = BorderStyle.FixedSingle,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 36 };
        _list = list;
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= list.Items.Count || list.Items[e.Index] is not Entry item) return;
            e.DrawBackground();
            var offset = 8;
            if (item.Value is Image image)
            {
                var scale = 28d / Math.Max(image.Width, image.Height);
                var width = Math.Max(1, (int)(image.Width * scale));
                var height = Math.Max(1, (int)(image.Height * scale));
                e.Graphics.DrawImage(image, e.Bounds.Left + 8, e.Bounds.Top + (e.Bounds.Height - height) / 2, width, height);
                offset = 44;
            }
            TextRenderer.DrawText(e.Graphics, item.ToString(), list.Font,
                new Rectangle(e.Bounds.X + offset, e.Bounds.Y, Math.Max(0, e.Bounds.Width - offset), e.Bounds.Height),
                e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        };
        list.DragEnter += (_, e) => e.Effect = Supported(e.Data) ? DragDropEffects.Copy & e.AllowedEffect : DragDropEffects.None;
        list.DragDrop += (_, e) =>
        {
            try { Add(e.Data); }
            catch (Exception error) { WindowsSettings.Log("Dépôt Drop Shelf.", error); }
        };
        list.MouseDown += (_, e) => _mouseDown = e.Location;
        list.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || list.IndexFromPoint(_mouseDown) < 0 || list.SelectedItem is not Entry selected ||
                Math.Abs(e.X - _mouseDown.X) + Math.Abs(e.Y - _mouseDown.Y) < 10) return;
            try { if (Payload(selected) is { } data) list.DoDragDrop(data, DragDropEffects.Copy); }
            catch (Exception error) { WindowsSettings.Log("Glisser depuis Drop Shelf.", error); }
        };

        var toolbar = UtilityWindows.Toolbar();
        toolbar.Controls.AddRange([
            UtilityWindows.Button("Copier", () => { if (list.SelectedItem is Entry item && Payload(item) is { } data) Clipboard.SetDataObject(data, true); }),
            UtilityWindows.Button("Épingler", () => { if (list.SelectedItem is Entry item) { var i = _items.IndexOf(item); _items[i] = item with { Pinned = !item.Pinned, Expires = DateTimeOffset.UtcNow.AddHours(_hours) }; Refresh(i); } }),
            UtilityWindows.Button("Monter", () => Move(-1)), UtilityWindows.Button("Descendre", () => Move(1)),
            UtilityWindows.Button("Retirer", () => { if (list.SelectedItem is Entry item) Remove(item); Refresh(); }),
            UtilityWindows.Button("Vider", Clear),
        ]);
        form.Controls.Add(list);
        form.Controls.Add(new Label { Text = "Déposez des fichiers, une image ou du texte", Dock = DockStyle.Top, Height = 34,
            Padding = new(12, 8, 0, 0) });
        form.Controls.Add(toolbar);
        form.FormClosed += (_, _) => { _form = null; _list = null; };
        Refresh();
        form.Show();
    }

    private static bool Supported(IDataObject? data) => data is not null &&
        (data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.UnicodeText));

    private void Add(IDataObject? data)
    {
        if (!Supported(data) || data is null) return;
        var expiry = DateTimeOffset.UtcNow.AddHours(_hours);
        if (data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files.Take(50 - _items.Count))
                _items.Add(new(file, Path.GetFileName(file.TrimEnd(Path.DirectorySeparatorChar)), expiry));
        }
        else if (_items.Count < 50 && data.GetData(DataFormats.Bitmap) is Image bitmap &&
                 (long)bitmap.Width * bitmap.Height <= 4_000_000 && _items.Count(item => item.Value is Image) < 8)
            _items.Add(new(new Bitmap(bitmap), $"Image · {bitmap.Width} × {bitmap.Height}", expiry));
        else if (_items.Count < 50 && data.GetData(DataFormats.UnicodeText) is string { Length: > 0 and <= 100_000 } text)
        {
            var preview = text.ReplaceLineEndings(" ");
            _items.Add(new(new DataObject(DataFormats.UnicodeText, text), preview[..Math.Min(75, preview.Length)], expiry));
        }
        Refresh();
    }

    private static DataObject? Payload(Entry item) => item.Value switch
    {
        string path when File.Exists(path) || Directory.Exists(path) => new DataObject(DataFormats.FileDrop, new[] { path }),
        string => throw new FileNotFoundException("Le fichier d’origine a été déplacé ou supprimé."),
        Image image => new DataObject(DataFormats.Bitmap, image),
        DataObject text => text,
        _ => null
    };

    private void Move(int delta)
    {
        var index = _list?.SelectedIndex ?? -1;
        var next = index + delta;
        if (index < 0 || next < 0 || next >= _items.Count) return;
        (_items[index], _items[next]) = (_items[next], _items[index]);
        Refresh(next);
    }

    private void Remove(Entry item)
    {
        _items.Remove(item);
        (item.Value as Image)?.Dispose();
    }

    private void Refresh(int index = -1)
    {
        _expiry.Enabled = _items.Any(item => !item.Pinned);
        if (_list is not { } list) return;
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            list.Items.AddRange(_items.Cast<object>().ToArray());
            if (index >= 0 && index < list.Items.Count) list.SelectedIndex = index;
        }
        finally { list.EndUpdate(); }
    }

    private void Clear()
    {
        foreach (var item in _items.ToArray()) Remove(item);
        Refresh();
    }

    public void Dispose()
    {
        _form?.Close();
        Clear();
        _expiry.Dispose();
    }
}
