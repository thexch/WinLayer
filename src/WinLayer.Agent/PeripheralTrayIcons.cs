using System.Runtime.InteropServices;
using WinLayer.Core;

namespace WinLayer.Agent;

internal sealed partial class PeripheralTrayIcons : NativeWindow, IDisposable
{
    private const int Callback = 0x8001;
    private const uint Add = 0, Modify = 1, Delete = 2, SetVersion = 4;
    private const uint GuidFlag = 0x20;
    private const uint DisplayFlags = GuidFlag | 0x80 | 0x07;
    private readonly Action _openBattery;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Dictionary<Guid, Entry> _entries = [];
    private ushort _nextId;

    internal PeripheralTrayIcons(Action openBattery)
    {
        _openBattery = openBattery;
        CreateHandle(new CreateParams { Caption = "WinLayer battery icons" });
    }

    public void Update(IReadOnlyList<BatteryDevice> devices)
    {
        var remaining = new HashSet<Guid>(_entries.Keys);
        foreach (var device in devices)
        {
            if (BatteryTrayDisplay.FromDevice(device) is not { } display)
                continue;
            remaining.Remove(display.IconId);

            var exists = _entries.TryGetValue(display.IconId, out var entry);
            if (!exists)
            {
                do { _nextId++; }
                while (_nextId == 0 || _entries.Values.Any(other => other.Id == _nextId));
                entry = new Entry(_nextId);
                _entries.Add(display.IconId, entry);
            }

            if (entry!.Display == display && entry.Registered)
                continue;

            var icon = TrayBatteryIcon.Create(display, SystemInformation.SmallIconSize.Width);
            entry.Icon?.Dispose();
            entry.Icon = icon;
            entry.Display = display;
            var data = CreateData(display.IconId, entry);
            entry.Registered = exists && entry.Registered && Notify(Modify, ref data) ||
                Register(ref data);
        }

        foreach (var id in remaining)
        {
            Remove(id, _entries[id]);
            _entries.Remove(id);
        }
    }

    private static bool Register(ref IconData data)
    {
        if (!Notify(Add, ref data))
            return false;
        data.Version = 4;
        Notify(SetVersion, ref data);
        return true;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == _taskbarCreated)
        {
            foreach (var (id, entry) in _entries)
            {
                var data = CreateData(id, entry);
                entry.Registered = Register(ref data);
            }
        }
        else if (message.Msg == Callback && ((long)message.LParam & 0xFFFF) is 0x0202 or 0x0400 or 0x0401)
        {
            _openBattery();
        }
        base.WndProc(ref message);
    }

    private IconData CreateData(Guid id, Entry entry)
    {
        // Leave room for Unicode terminator, and keep the percentage even with long device names.
        var tooltip = entry.Display.Tooltip;
        if (tooltip.Length > 127)
            tooltip = string.Concat(tooltip.AsSpan(0, 110), "… · ", entry.Display.Percentage.ToString(), " %");
        return new()
        {
            Size = (uint)Marshal.SizeOf<IconData>(),
            Window = Handle,
            Id = entry.Id,
            Flags = DisplayFlags,
            Callback = Callback,
            Icon = entry.Icon?.Handle ?? 0,
            Tooltip = tooltip,
            Info = "",
            InfoTitle = "",
            Guid = id
        };
    }

    private void Remove(Guid id, Entry entry)
    {
        var data = CreateData(id, entry);
        if (entry.Registered)
            Notify(Delete, ref data);
        entry.Icon?.Dispose();
    }

    public void Dispose()
    {
        foreach (var (id, entry) in _entries)
            Remove(id, entry);
        _entries.Clear();
        DestroyHandle();
    }

    private sealed class Entry(ushort id)
    {
        public ushort Id { get; } = id;
        public BatteryTrayDisplay Display { get; set; }
        public Icon? Icon { get; set; }
        public bool Registered { get; set; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tooltip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Notify(uint message, ref IconData data);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessage(string message);
}
