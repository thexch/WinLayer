using System.ComponentModel;
using System.Runtime.InteropServices;
using WinLayer.Core;
using WinLayer.Modules;

namespace WinLayer.Agent;

internal sealed partial class WindowsAutoClicker(SynchronizationContext ui, GlobalHotkeys hotkeys, Func<HubOptions>? keyboard = null) : IDisposable
{
    private AutoClickerOptions _options = new();
    private Run? _run;
    private KeyboardHook? _hook;
    private IntPtr _hookHandle;
    private bool _paused;
    private string? _error;

    private ClickState State => _paused ? ClickState.Paused : _run is not null ? ClickState.Active : ClickState.Idle;

    public AutoClickerStatus Status => new(State, _error);

    // Reserve candidate shortcuts before committing preferences. Old shortcuts remain usable if saving fails.
    public void Reserve(AutoClickerOptions options)
    {
        if (!options.Enabled)
            return;

        hotkeys.Reserve(this, options.Shortcut, OnHotkey);
        hotkeys.Reserve(this, options.Emergency, OnHotkey);
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
        if (_options.Enabled && hotkeys.Owns(this, _options.Shortcut) && hotkeys.Owns(this, _options.Emergency))
            hotkeys.Retain(this, _options.Shortcut, _options.Emergency);
        else
            hotkeys.Retain(this);
    }

    public void Stop() => StopRun();

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

    private void OnHotkey(ClickShortcut shortcut)
    {
        switch (AutoClicker.OnHotkey(_options, State, shortcut))
        {
            case ClickHotkeyAction.Pause:
                Pause();
                break;
            case ClickHotkeyAction.Stop:
                StopRun();
                break;
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

            var run = new Run(_options, keyboard?.Invoke());
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
            using var driver = new WindowsClickDriver(run.Options, run.SendGate, run.Keyboard);
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

    private sealed class Run(AutoClickerOptions options, HubOptions? keyboard)
    {
        public AutoClickerOptions Options { get; } = options;
        public HubOptions? Keyboard { get; } = keyboard;
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

    private delegate IntPtr KeyboardHook(int code, IntPtr message, IntPtr data);

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
