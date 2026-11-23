using System.Diagnostics;
using WinLayer.Core;

namespace WinLayer.Agent;

internal sealed class MacroSession(Action<string> report, SynchronizationContext ui) : IDisposable
{
    private RecordingWindow? _window;
    private Playback? _playback;
    private MacroStep[] _finished = [];
    public string? State => _playback is not null ? "playing" : _window is null ? null : _window.Paused ? "paused" : "recording";
    public MacroStep[] Steps => _window?.Snapshot ?? _finished;

    public void Record()
    {
        Stop();
        _finished = [];
        var window = new RecordingWindow();
        _window = window;
        window.FormClosed += (_, _) =>
        {
            _finished = window.Snapshot;
            if (ReferenceEquals(_window, window))
                _window = null;
            window.Dispose();
        };
        window.Show();
        window.Activate();
    }

    public void Pause() => _window?.TogglePause();

    public void Play(HubWindow target, MacroPreset macro, HubOptions options)
    {
        Stop();
        var session = new Playback();
        _playback = session;
        _ = Task.Run(async () =>
        {
            try
            {
                var token = session.Token;
                for (var repeat = 0; options.Loop || repeat < options.PlayCount; repeat++)
                {
                    await Task.Delay(100, token);
                    foreach (var step in macro.Steps)
                    {
                        token.ThrowIfCancellationRequested();
                        await Task.Delay(Math.Max(1, step.Delay * 100 / options.SpeedPercent), token);
                        session.Send(() => Execute(target, step, token));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                ui.Post(_ =>
                {
                    if (_playback == session)
                        report("Lecture interrompue. La cible a changé ou Windows a refusé des entrées.");
                }, null);
                WindowsSettings.Log("Lecture de macro.", error);
            }
            finally
            {
                session.Dispose();
                ui.Post(_ =>
                {
                    if (_playback == session)
                        _playback = null;
                }, null);
            }
        });
    }

    private static void Execute(HubWindow target, MacroStep step, CancellationToken token)
    {
        if (HubWindows.Active.ToInt64() != target.Handle || !HubWindows.IsSame(target))
            throw new InvalidOperationException("The target window lost focus.");
        if (step.Kind == MacroKind.Wait)
            return;
        if (step.Kind == MacroKind.Key)
        {
            WindowsClickDriver.SendKey(step.Key, step.Modifiers);
            return;
        }

        if (HubWindows.Client(target.Handle) is not { } area)
            throw new InvalidOperationException("The target geometry is unavailable.");
        var point = new Point(area.Left + (int)((long)step.X * (area.Width - 1) / 10_000),
            area.Top + (int)((long)step.Y * (area.Height - 1) / 10_000));
        if (HubWindows.WindowAt(point).ToInt64() != target.Handle)
            throw new InvalidOperationException("Another window is covering the target point.");
        if (step.Kind == MacroKind.Move)
        {
            WindowsClickDriver.MovePointer(point);
            return;
        }

        var options = new AutoClickerOptions
        {
            Position = ClickPosition.Fixed, X = point.X, Y = point.Y,
            Kind = step.Kind == MacroKind.Right ? ClickKind.Right : step.Kind == MacroKind.Middle ? ClickKind.Middle : ClickKind.Left
        };
        WindowsClickDriver.SendClick(options, token);
    }

    public void Stop()
    {
        _window?.Close();
        _playback?.Cancel();
        _playback = null;
    }

    public void Dispose() => Stop();

    private sealed class Playback : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancel = new();
        private bool _disposed;
        public CancellationToken Token => _cancel.Token;
        public void Cancel()
        {
            lock (_gate)
                if (!_disposed)
                    _cancel.Cancel();
        }
        public void Send(Action action)
        {
            lock (_gate)
            {
                Token.ThrowIfCancellationRequested();
                action();
            }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _cancel.Dispose();
            }
        }
    }

    private sealed class RecordingSurface : Panel
    {
        public RecordingSurface() => SetStyle(ControlStyles.Selectable, true);

        protected override bool IsInputKey(Keys keyData) => HubOptions.KeyboardKey((int)(keyData & Keys.KeyCode)) || base.IsInputKey(keyData);
    }

    private sealed class RecordingWindow : HubOverlay
    {
        private readonly List<MacroStep> _steps = [];
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _last;
        private long _lastMove;
        private readonly Label _status;
        private readonly Panel _surface;
        private readonly HashSet<int> _held = [];
        public bool Paused { get; private set; }
        public MacroStep[] Snapshot => _steps.ToArray();

        public RecordingWindow()
        {
            Text = "Macro Recorder — Enregistrement local";
            AccessibleName = Text;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MinimizeBox = false;
            MaximizeBox = false;
            UtilityWindows.Style(this);
            BackColor = UtilityWindows.Canvas;
            ForeColor = UtilityWindows.Ink;
            Padding = new Padding(16);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Bounds = new(area.Left + (area.Width - 560) / 2, area.Top + (area.Height - 400) / 2, 560, 400);
            _status = new Label { Dock = DockStyle.Top, Height = 62, Text = "● Enregistrement · Échap pour terminer\nSeules les entrées dans cette fenêtre sont enregistrées.", ForeColor = UtilityWindows.Accent };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40 };
            var pause = UtilityWindows.Button("Pause / reprendre", TogglePause);
            var stop = UtilityWindows.Button("Terminer", Close);
            buttons.Controls.AddRange([pause, stop]);
            _surface = new RecordingSurface { Dock = DockStyle.Fill, BackColor = UtilityWindows.Surface, TabStop = true, AccessibleName = "Surface d’enregistrement" };
            _surface.MouseDown += (_, e) =>
            {
                _surface.Focus();
                AddMouse(e, e.Button == MouseButtons.Right ? MacroKind.Right : e.Button == MouseButtons.Middle ? MacroKind.Middle : MacroKind.Left);
            };
            _surface.MouseMove += (_, e) =>
            {
                if (_clock.ElapsedMilliseconds - _lastMove < 35)
                    return;
                _lastMove = _clock.ElapsedMilliseconds;
                AddMouse(e, MacroKind.Move);
            };
            Controls.Add(_surface);
            Controls.Add(buttons);
            Controls.Add(_status);
            Shown += (_, _) => _surface.Focus();
            Deactivate += (_, _) => { _held.Clear(); if (!Paused) TogglePause(); };
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape || !_surface.Focused || !HubOptions.KeyboardKey(e.KeyValue))
                    return;
                if (_held.Add(e.KeyValue))
                    Add(new(MacroKind.Key, Delay(), Key: e.KeyValue,
                        Modifiers: (e.Control ? 2 : 0) | (e.Alt ? 1 : 0) | (e.Shift ? 4 : 0)));
                e.SuppressKeyPress = true;
            };
            KeyUp += (_, e) => _held.Remove(e.KeyValue);
        }

        public void TogglePause()
        {
            if (_steps.Count >= 240)
                return;
            Paused = !Paused;
            _held.Clear();
            _last = _clock.ElapsedMilliseconds;
            UpdateLabel();
        }

        private int Delay() => _steps.Count == 0 ? 0 : (int)Math.Min(60_000, _clock.ElapsedMilliseconds - _last);

        private void AddMouse(MouseEventArgs e, MacroKind kind) =>
            Add(new(kind, Delay(), Math.Clamp(e.X * 10_000 / Math.Max(1, _surface.Width - 1), 0, 10_000),
                Math.Clamp(e.Y * 10_000 / Math.Max(1, _surface.Height - 1), 0, 10_000)));

        private void Add(MacroStep step)
        {
            if (Paused || !ContainsFocus || _steps.Count >= 240)
                return;
            _steps.Add(step);
            _last = _clock.ElapsedMilliseconds;
            if (_steps.Count == 240) Paused = true;
            UpdateLabel();
        }

        private void UpdateLabel() => _status.Text = _steps.Count == 240
            ? "Limite de 240 étapes atteinte · Terminez pour modifier l’enregistrement."
            : Paused ? $"En pause · {_steps.Count} étapes\nAppuyez sur Reprendre pour continuer."
            : $"● Enregistrement · {_steps.Count} étapes · Échap pour terminer\nSeules les entrées dans cette fenêtre sont enregistrées.";
    }
}
