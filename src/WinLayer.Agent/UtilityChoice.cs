using System.ComponentModel;

namespace WinLayer.Agent;

internal sealed class UtilityChoice : Button
{
    private readonly IReadOnlyList<string> _options;
    private readonly ContextMenuStrip _popup;
    private int _selectedIndex;

    public UtilityChoice(string label, IReadOnlyList<string> options)
    {
        _options = options;
        _popup = new ContextMenuStrip { Renderer = UtilityWindows.MenuRenderer(), ShowImageMargin = false,
            BackColor = UtilityWindows.Surface, ForeColor = UtilityWindows.Ink };
        FlatStyle = FlatStyle.Flat;
        BackColor = UtilityWindows.Surface;
        ForeColor = UtilityWindows.Ink;
        FlatAppearance.BorderColor = UtilityWindows.Border;
        AccessibleName = label;
        AccessibleRole = AccessibleRole.ButtonDropDown;
        MinimumSize = new Size(0, 30);
        SelectedIndex = 0;
        for (var index = 0; index < options.Count; index++)
        {
            var value = index;
            _popup.Items.Add(new ToolStripMenuItem(options[index]) { ForeColor = UtilityWindows.Ink });
            _popup.Items[index].Click += (_, _) => SelectedIndex = value;
        }
        _popup.Closed += (_, _) =>
        {
            if (!IsDisposed && FindForm() is { ContainsFocus: true }) Focus();
        };
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value;
            Text = _options[value] + "  ▾";
            AccessibleDescription = _options[value];
        }
    }

    public string SelectedValue => _options[_selectedIndex];

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _popup.Show(this, new Point(0, Height));
        _popup.Items[_selectedIndex].Select();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keys)
    {
        if (keys is Keys.Down or (Keys.Alt | Keys.Down) or Keys.F4)
        {
            OnClick(EventArgs.Empty);
            return true;
        }
        return base.ProcessCmdKey(ref message, keys);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _popup?.Dispose();
        base.Dispose(disposing);
    }
}
