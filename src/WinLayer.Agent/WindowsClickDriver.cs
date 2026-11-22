using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinLayer.Core;
using WinLayer.Modules;

namespace WinLayer.Agent;

internal sealed partial class WindowsClickDriver : IAutoClickerDriver, IDisposable
{
    private readonly AutoClickerOptions _options;
    private readonly object _sendGate;
    private readonly HubOptions? _keyboard;
    private readonly WaitHandle _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private WaitHandle[]? _waitHandles;

    public WindowsClickDriver(AutoClickerOptions options, object sendGate, HubOptions? keyboard = null)
    {
        _options = options;
        _keyboard = keyboard;
        _sendGate = sendGate;
        _timer = new ClickTimer();
    }

    public TimeSpan Elapsed => _clock.Elapsed;
    public bool CanClick => _options.Mode != ClickMode.Hold || IsHeld(_options.Shortcut);

    public bool WaitUntil(TimeSpan elapsed, CancellationToken cancellation)
    {
        var remaining = elapsed - Elapsed;
        if (remaining <= TimeSpan.Zero)
            return !cancellation.IsCancellationRequested;

        // Negative values are relative deadlines in units of 100 ns.
        var deadline = -remaining.Ticks;
        if (!SetWaitableTimer(_timer.SafeWaitHandle, in deadline, 0, IntPtr.Zero, IntPtr.Zero, false))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        _waitHandles ??= [cancellation.WaitHandle, _timer];
        return WaitHandle.WaitAny(_waitHandles) == 1;
    }

    public unsafe void Click(AutoClickerOptions options, CancellationToken cancellation)
    {
        if (_keyboard is { } keyboard)
        {
            lock (_sendGate)
                if (!cancellation.IsCancellationRequested && CanClick)
                    SendKey(keyboard.OutputKey, keyboard.OutputModifiers);
            return;
        }

        lock (_sendGate)
            if (!cancellation.IsCancellationRequested && CanClick)
                SendClick(options, cancellation);
    }

    public static unsafe void SendClick(AutoClickerOptions options, CancellationToken cancellation)
    {
        const uint move = 0x0001;
        const uint absolute = 0x8000;
        const uint virtualDesktop = 0x4000;
        var inputs = stackalloc Input[5];
        var count = 0;
        if (options.Position == ClickPosition.Fixed)
        {
            var bounds = SystemInformation.VirtualScreen;
            var point = new Point(options.X, options.Y);
            // VirtualScreen includes monitor gaps. Check the actual monitor as well.
            if (MonitorFromPoint(point, 0) == IntPtr.Zero || !bounds.Contains(point))
                throw new ArgumentOutOfRangeException(nameof(options));
            inputs[count++] = new Input(move | absolute | virtualDesktop,
                NormalizeAbsolute(point.X, bounds.Left, bounds.Width),
                NormalizeAbsolute(point.Y, bounds.Top, bounds.Height));
        }

        (uint down, uint up) = options.Kind switch
        {
            ClickKind.Right => (0x0008u, 0x0010u),
            ClickKind.Middle => (0x0020u, 0x0040u),
            _ => (0x0002u, 0x0004u)
        };
        inputs[count++] = new Input(down);
        inputs[count++] = new Input(up);
        if (options.Kind == ClickKind.Double)
        {
            inputs[count++] = new Input(down);
            inputs[count++] = new Input(up);
        }

        if (cancellation.IsCancellationRequested)
            return;
        var sent = SendInput((uint)count, inputs, sizeof(Input));
        if (sent == count)
            return;

        var nativeError = Marshal.GetLastPInvokeError();
        var buttonEvents = sent - (options.Position == ClickPosition.Fixed && sent > 0 ? 1u : 0u);
        if (buttonEvents % 2 != 0)
        {
            var release = new Input(up);
            SendInput(1, &release, sizeof(Input));
        }
        throw new Win32Exception(nativeError, "SendInput failed or inserted only part of the click sequence.");
    }

    public static unsafe void SendKey(int key, int modifiers)
    {
        var input = stackalloc Input[8];
        var count = 0;
        if ((modifiers & 2) != 0 && !IsDown(0x11)) input[count++] = Input.Key(0x11, false);
        if ((modifiers & 1) != 0 && !IsDown(0x12)) input[count++] = Input.Key(0x12, false);
        if ((modifiers & 4) != 0 && !IsDown(0x10)) input[count++] = Input.Key(0x10, false);
        var held = count;
        input[count++] = Input.Key(key, false);
        input[count++] = Input.Key(key, true);
        for (var i = held - 1; i >= 0; i--)
            input[count++] = Input.Key(input[i].VirtualKey, true);
        var sent = SendInput((uint)count, input, sizeof(Input));
        if (sent == count)
            return;
        var error = Marshal.GetLastPInvokeError();
        var released = 0;
        for (var i = 0; i < Math.Min(held + 1, (int)sent); i++)
            input[released++] = Input.Key(input[i].VirtualKey, true);
        if (released > 0)
            SendInput((uint)released, input, sizeof(Input));
        throw new Win32Exception(error, "Keyboard SendInput failed.");
    }

    public static unsafe void MovePointer(Point point)
    {
        var bounds = SystemInformation.VirtualScreen;
        if (MonitorFromPoint(point, 0) == IntPtr.Zero || !bounds.Contains(point))
            throw new ArgumentOutOfRangeException(nameof(point));
        var input = new Input(0xC001, NormalizeAbsolute(point.X, bounds.Left, bounds.Width),
            NormalizeAbsolute(point.Y, bounds.Top, bounds.Height));
        if (SendInput(1, &input, sizeof(Input)) != 1)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Pointer movement failed.");
    }

    public void Dispose() => _timer.Dispose();

    internal static int NormalizeAbsolute(int point, int origin, int length) =>
        (int)Math.Min(65_535, ((2L * ((long)point - origin) + 1) * 65_536) / (2L * length));

    public static bool IsHeld(ClickShortcut shortcut)
    {
        var modifiers = (ShortcutModifiers)shortcut.Modifiers;
        return IsDown(shortcut.Key) &&
            (!modifiers.HasFlag(ShortcutModifiers.Control) || IsDown(0x11)) &&
            (!modifiers.HasFlag(ShortcutModifiers.Alt) || IsDown(0x12)) &&
            (!modifiers.HasFlag(ShortcutModifiers.Shift) || IsDown(0x10)) &&
            (!modifiers.HasFlag(ShortcutModifiers.Windows) || IsDown(0x5B) || IsDown(0x5C));
    }

    private static bool IsDown(int key) => GetAsyncKeyState(key) < 0;

    private sealed class ClickTimer : WaitHandle
    {
        public ClickTimer()
        {
            // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, supported by all targeted Windows releases.
            SafeWaitHandle = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, 2, 0x00100002);
            if (SafeWaitHandle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        private uint _type;
        private InputData _data;

        public Input(uint flags, int x = 0, int y = 0)
        {
            _type = 0;
            _data.Mouse = new(x, y, flags);
        }

        public static Input Key(int key, bool up) => new()
        {
            _type = 1,
            _data = new() { Keyboard = new((ushort)key, 0,
                (up ? 2u : 0u) | (key is >= 0x21 and <= 0x28 or 0x2E ? 1u : 0u), 0, 0) }
        };

        public readonly int VirtualKey => _data.Keyboard.VirtualKey;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct KeyboardInput(ushort VirtualKey, ushort Scan, uint Flags, uint Time, nuint Extra);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MouseInput(int x, int y, uint flags)
    {
        private readonly int _x = x;
        private readonly int _y = y;
        private readonly uint _data = 0;
        private readonly uint _flags = flags;
        private readonly uint _time = 0;
        private readonly nuint _extra = 0;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, IntPtr name, uint flags, uint access);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimer(SafeWaitHandle timer, in long dueTime, int period,
        IntPtr completion, IntPtr state, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial uint SendInput(uint count, Input* inputs, int size);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);
}
