using System.ComponentModel;
using System.Runtime.InteropServices;
using WinLayer.Core;
using WinLayer.Modules;

namespace WinLayer.Agent;

internal sealed partial class WindowsAutoClicker(SynchronizationContext ui) : IDisposable
{
    private readonly Dictionary<ClickShortcut, int> _bindings = new(4);
    private HotkeyWindow? _window;
    private AutoClickerOptions _options = new();
    private Run? _run;
    private KeyboardHook? _hook;
    private IntPtr _hookHandle;
    private int _nextId;
    private bool _paused;
    private string? _error;

    private ClickState State => _paused ? ClickState.Paused : _run is not null ? ClickState.Active : ClickState.Idle;

    public AutoClickerStatus Status => new(State, _error);

    // Reserve candidate shortcuts before committing preferences. Old shortcuts remain usable if saving fails.
    public void Reserve(AutoClickerOptions options)
    {
        if (!options.Enabled)
            return;

        _window ??= new HotkeyWindow(OnHotkey);
        Register(options.Shortcut);
        Register(options.Emergency);
    }

    public void Apply(AutoClickerOptions options)
    {
        if (options == _options)
            return;

        StopRun();
        if (!_options.Enabled || !options.Enabled)
        {
            _paused = false;
            _error = null;
        }

        if (options.Shortcut != _options.Shortcut || options.Emergency != _options.Emergency)
            _error = null;
        _options = options;
    }

    public void RejectStartup(AutoClickerOptions options, string error)
    {
        _options = options;
        Pause(error);
    }

    public void Prune()
    {
        var hasPair = _options.Enabled && _bindings.ContainsKey(_options.Shortcut) &&
            _bindings.ContainsKey(_options.Emergency);
        foreach (var (shortcut, id) in _bindings.ToArray())
        {
            if (hasPair && (shortcut == _options.Shortcut || shortcut == _options.Emergency))
                continue;

            UnregisterHotKey(_window!.Handle, id);
            _bindings.Remove(shortcut);
        }

        if (_bindings.Count == 0)
        {
            _window?.DestroyHandle();
            _window = null;
        }
    }

    public void Rearm()
    {
        if (!_options.Enabled || !_paused)
            return;

        try
        {
            Reserve(_options);
            _paused = false;
            _error = null;
        }
        finally
        {
            Prune();
        }
    }

    public void Pause(string? error = null)
    {
        if (!_options.Enabled)
            return;

        _paused = true;
        _error = error;
        StopRun();
    }

    public void Dispose()
    {
        Apply(new());
        Prune();
    }

    private void Register(ClickShortcut shortcut)
    {
        if (_bindings.ContainsKey(shortcut))
            return;

        // Only two to four IDs are registered, but queued messages must not match a replaced shortcut.
        _nextId = _nextId % 0xBFFF + 1;
        if (!RegisterHotKey(_window!.Handle, _nextId, (uint)shortcut.Modifiers | 0x4000 /* MOD_NOREPEAT */, (uint)shortcut.Key))
            throw new InvalidOperationException($"Le raccourci {ShortcutName(shortcut)} est indisponible. Choisissez une autre combinaison.");

        _bindings.Add(shortcut, _nextId);
    }

    private void OnHotkey(int id)
    {
        if (_bindings.TryGetValue(_options.Emergency, out var stopId) && id == stopId)
        {
            if (AutoClicker.OnHotkey(_options, State, _options.Emergency) == ClickHotkeyAction.Pause)
                Pause();
            return;
        }

        if (!_bindings.TryGetValue(_options.Shortcut, out var startId) || id != startId)
            return;
        switch (AutoClicker.OnHotkey(_options, State, _options.Shortcut))
        {
            case ClickHotkeyAction.Stop:
                StopRun();
                return;
            case ClickHotkeyAction.Start:
                StartRun();
                break;
        }
    }

    private void StartRun()
    {
        try
        {
            if (_options.Mode == ClickMode.Hold)
            {
                if (!WindowsClickDriver.IsHeld(_options.Shortcut))
                    return;

                _hook = OnKeyboard;
                _hookHandle = SetWindowsHookExW(13 /* WH_KEYBOARD_LL */, _hook, GetModuleHandleW(null), 0);
                if (_hookHandle == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            var run = new Run(_options);
            _run = run;
            try
            {
                _ = Task.Factory.StartNew(() => Execute(run), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch
            {
                run.Complete();
                throw;
            }
        }
        catch (Exception error)
        {
            OnFailure(error);
        }
    }

    private void Execute(Run run)
    {
        Exception? failure = null;
        try
        {
            using var driver = new WindowsClickDriver(run.Options, run.SendGate);
            AutoClicker.Run(run.Options, driver, run.Cancellation.Token);
        }
        catch (Exception error)
        {
            failure = error;
        }

        run.Complete();
        ui.Post(_ =>
        {
            if (_run != run)
                return;

            StopRun();
            if (failure is not null)
                OnFailure(failure);
        }, null);
    }

    private void StopRun()
    {
        if (_run is { } run)
        {
            _run = null;
            run.Cancel();
        }
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _hook = null;
    }

    private void OnFailure(Exception error)
    {
        Pause(error is ArgumentOutOfRangeException
            ? "La position fixe est en dehors des écrans disponibles."
            : "Windows n’a pas pu simuler les clics. Vérifiez la fenêtre cible et réarmez le module.");
        WindowsSettings.Log("Auto Clicker.", error);
    }

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt64() is 0x101 or 0x105 &&
            (Marshal.ReadInt32(data, 8) & 0x10) == 0 &&
            AutoClicker.ReleasedShortcut(Marshal.ReadInt32(data), _options.Shortcut))
            StopRun();

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private static string ShortcutName(ClickShortcut shortcut)
    {
        var modifiers = (ShortcutModifiers)shortcut.Modifiers;
        return (modifiers.HasFlag(ShortcutModifiers.Control) ? "Ctrl+" : "") +
            (modifiers.HasFlag(ShortcutModifiers.Alt) ? "Alt+" : "") +
            (modifiers.HasFlag(ShortcutModifiers.Shift) ? "Maj+" : "") +
            (modifiers.HasFlag(ShortcutModifiers.Windows) ? "Win+" : "") +
            (shortcut.Key >= 0x70 ? $"F{shortcut.Key - 0x6F}" : ((char)shortcut.Key).ToString());
    }

    private sealed class Run(AutoClickerOptions options)
    {
        public AutoClickerOptions Options { get; } = options;
        public CancellationTokenSource Cancellation { get; } = new();
        public object SendGate { get; } = new();
        private bool _finished;

        public void Cancel()
        {
            lock (SendGate)
            {
                if (!_finished)
                    Cancellation.Cancel();
            }
        }

        public void Complete()
        {
            lock (SendGate)
            {
                _finished = true;
                Cancellation.Dispose();
            }
        }
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly Action<int> _receive;

        public HotkeyWindow(Action<int> receive)
        {
            _receive = receive;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) /* HWND_MESSAGE */ });
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 /* WM_HOTKEY */)
                _receive(message.WParam.ToInt32());
            else
                base.WndProc(ref message);
        }
    }

    private delegate IntPtr KeyboardHook(int code, IntPtr message, IntPtr data);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int type, KeyboardHook hook, IntPtr module, uint thread);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hook);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string? name);
}
