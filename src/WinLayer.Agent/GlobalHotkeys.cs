using System.Runtime.InteropServices;
using WinLayer.Core;

namespace WinLayer.Agent;

// Reservations stay attached to their owner until that owner commits or rolls back its settings.
internal sealed partial class GlobalHotkeys : IDisposable
{
    private sealed record Binding(object Owner, int Id, Action<ClickShortcut> Callback);
    private readonly Dictionary<ClickShortcut, Binding> _bindings = [];
    private MessageWindow? _window;
    private int _nextId;

    public void Reserve(object owner, ClickShortcut shortcut, Action<ClickShortcut> callback)
    {
        if (_bindings.TryGetValue(shortcut, out var existing))
        {
            if (ReferenceEquals(owner, existing.Owner))
                return;
            throw new InvalidOperationException("Ce raccourci est déjà utilisé par un autre outil WinLayer.");
        }

        _window ??= new MessageWindow(Dispatch);
        do { _nextId = _nextId % 0xBFFF + 1; }
        while (_bindings.Values.Any(binding => binding.Id == _nextId));
        if (!RegisterHotKey(_window.Handle, _nextId, (uint)shortcut.Modifiers | 0x4000, (uint)shortcut.Key))
            throw new InvalidOperationException("Ce raccourci est indisponible dans Windows.");

        _bindings.Add(shortcut, new(owner, _nextId, callback));
    }

    public bool IsRegistered(ClickShortcut shortcut) => _bindings.ContainsKey(shortcut);

    public bool Owns(object owner, ClickShortcut shortcut) =>
        _bindings.TryGetValue(shortcut, out var binding) && ReferenceEquals(owner, binding.Owner);

    public void Retain(object owner, params ClickShortcut[] keep)
    {
        foreach (var (shortcut, binding) in _bindings.ToArray())
        {
            if (!ReferenceEquals(binding.Owner, owner) || keep.Contains(shortcut))
                continue;
            UnregisterHotKey(_window!.Handle, binding.Id);
            _bindings.Remove(shortcut);
        }

        if (_bindings.Count == 0)
        {
            _window?.DestroyHandle();
            _window = null;
        }
    }

    private void Dispatch(int id)
    {
        foreach (var (shortcut, binding) in _bindings)
        {
            if (binding.Id != id)
                continue;
            try { binding.Callback(shortcut); }
            catch (Exception error) { WindowsSettings.Log("Raccourci WinLayer.", error); }
            return;
        }
    }

    public void Dispose()
    {
        foreach (var binding in _bindings.Values)
            UnregisterHotKey(_window!.Handle, binding.Id);
        _bindings.Clear();
        _window?.DestroyHandle();
        _window = null;
    }

    private sealed class MessageWindow : NativeWindow
    {
        private readonly Action<int> _dispatch;

        public MessageWindow(Action<int> dispatch)
        {
            _dispatch = dispatch;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312)
                _dispatch(message.WParam.ToInt32());
            else
                base.WndProc(ref message);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr window, int id);
}
