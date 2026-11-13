using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Mouse;

/// <summary>
/// The mouse actions that change how the mouse behaves for a while: slow pointer, axis locks, sticky (click-drag) buttons,
/// movement that scrolls, dragging a window, peeking at the desktop, holding keys down. Some last while the button is held,
/// others until it is pressed again. Movement is only watched while one of these is active, so an idle mouse costs nothing.
/// <see cref="Start"/> and <see cref="OnMove"/> run inside the mouse hook, so they only decide and record; everything that calls into
/// Windows at length (sending input, moving windows, pointer speed and look) is posted to an <see cref="InputQueue"/>.
/// </summary>
public sealed class MouseModes : IDisposable
{
    private static readonly ushort[] RevealKeys = ParseKeys("Win+Comma");

    private readonly MouseHook _hook;
    private readonly InputQueue _queue;
    private readonly MoveScroll _scroll;
    private readonly object _gate = new();
    private readonly Dictionary<MouseButton, int> _stuck = new();   // sticky buttons now held down, with a counter that tells one stick from the next
    private int? _lockX, _lockY;            // the pointer coordinate held fixed on that axis
    private int _stickCount;

    // Queue thread only.
    private readonly WindowDrag _drag = new();
    private int? _speedBeforeSlow;          // the pointer speed to go back to, while slowed

    // Window drag: the hook writes the newest pointer position, the queue thread moves the window to it (older positions are skipped).
    private volatile bool _dragging;
    private long _dragTo;
    private int _dragQueued;


    public MouseModes(MouseHook hook)
    {
        _hook = hook;
        _queue = new InputQueue(hook.Diagnose);
        _scroll = new MoveScroll(_queue, hook.Diagnose);
    }

    private readonly PointerLook _look = new();

    /// <summary>
    /// Starts (or, for toggles, flips) the mode, with the pointer look the action asks for while it lasts. Returns what to undo when
    /// the button comes up, or null when nothing is held (a toggle, or a mode that stays on until pressed again).
    /// </summary>
    public Action? Start(MouseAction action, MouseEvent at)
    {
        var undo = StartMode(action, at);

        var scrolls = action.Id == MouseActionCatalog.MoveAsScroll;
        var look = action.Options.Text(scrolls ? MouseOptions.ScrollPointer : MouseOptions.HeldPointer);
        var lasts = scrolls ? _scroll.IsRunning : undo is not null;
        if (!lasts || look == MouseOptions.PointerSame || !MouseActionCatalog.IsWhileHeld(action.Id)) return undo;

        _queue.Post(() => _look.Apply(look));
        if (undo is null) return null; // lasts until pressed again: StartMoveAsScroll clears the look when it stops
        return () =>
        {
            undo();
            _queue.Post(_look.Clear);
        };
    }


    private Action? StartMode(MouseAction action, MouseEvent at)
    {
        switch (action.Id)
        {
            case MouseActionCatalog.SlowHold:
            {
                var speed = action.Options.Int(MouseOptions.SlowSpeed);
                var started = false; // read and written on the queue thread only
                _queue.Post(() => started = EnterSlow(speed));
                return () => _queue.Post(() => { if (started) LeaveSlow(); });
            }
            case MouseActionCatalog.SlowToggle:
            {
                var speed = action.Options.Int(MouseOptions.SlowSpeed);
                _queue.Post(() => { if (!EnterSlow(speed)) LeaveSlow(); });
                return null;
            }

            case MouseActionCatalog.MoveAsScroll: return StartMoveAsScroll(action.Options, at);

            case MouseActionCatalog.AxisXHold: SetLock(x: at.X); return () => SetLock(clearX: true);
            case MouseActionCatalog.AxisYHold: SetLock(y: at.Y); return () => SetLock(clearY: true);
            case MouseActionCatalog.AxisXToggle: ToggleLock(xAxis: true, at); return null;
            case MouseActionCatalog.AxisYToggle: ToggleLock(xAxis: false, at); return null;

            case MouseActionCatalog.DoubleDrag:
                _queue.Post(() =>
                {
                    InputSender.Click(MouseButton.Left);
                    InputSender.ButtonDown(MouseButton.Left);
                });
                return () => _queue.Post(() => InputSender.ButtonUp(MouseButton.Left));

            case MouseActionCatalog.WindowMove or MouseActionCatalog.WindowSize:
            {
                var resize = action.Id == MouseActionCatalog.WindowSize;
                _queue.Post(() =>
                {
                    if (!_drag.Begin(at.X, at.Y, resize)) return;
                    _dragging = true;
                    Refresh();
                });
                return () => _queue.Post(() =>
                {
                    _dragging = false;
                    _drag.End();
                    Refresh();
                });
            }

            case MouseActionCatalog.Reveal:
                _queue.Post(() => InputSender.Press(RevealKeys));
                return () => _queue.Post(() => InputSender.Release(RevealKeys));
        }

        if (action.Id.StartsWith(MouseActionCatalog.StickyPrefix, StringComparison.Ordinal)) ToggleSticky(action, at);
        return null;
    }

    /// <summary>Holds a shortcut's keys down; returns what lets them go (the "keys held down while the button is held" trigger).</summary>
    public Action? HoldKeys(string shortcut)
    {
        if (!KeyChord.TryParse(shortcut, out var keys)) return null;
        _queue.Post(() => InputSender.Press(keys));
        return () => _queue.Post(() => InputSender.Release(keys));
    }

    private Action? StartMoveAsScroll(OptionValues options, MouseEvent at)
    {
        var sticky = options.Bool(MouseOptions.ScrollSticky);
        // With "keep scrolling until pressed again", the next press stops it.
        if (sticky && _scroll.Stop())
        {
            Refresh();
            _queue.Post(_look.Clear);
            return null;
        }

        _scroll.Start(at.X, at.Y, options);
        Refresh();
        if (sticky) return null;
        return () =>
        {
            _scroll.Stop();
            Refresh();
        };
    }

    /// <summary>Queue thread: the pointer speed drops to <paramref name="speed"/>; false when it is already slowed, or Windows won't say.</summary>
    private bool EnterSlow(int speed)
    {
        if (_speedBeforeSlow is not null || SystemCommands.GetCursorSpeed() is not { } before) return false;
        _speedBeforeSlow = before;
        SystemCommands.SetCursorSpeed(speed);
        return true;
    }

    /// <summary>Queue thread: back to the speed from before slowing.</summary>
    private void LeaveSlow()
    {
        if (_speedBeforeSlow is not { } before) return;
        SystemCommands.SetCursorSpeed(before);
        _speedBeforeSlow = null;
    }

    private void SetLock(int? x = null, int? y = null, bool clearX = false, bool clearY = false)
    {
        lock (_gate)
        {
            if (clearX) _lockX = null; else if (x is not null) _lockX = x;
            if (clearY) _lockY = null; else if (y is not null) _lockY = y;
        }
        Refresh();
    }

    private void ToggleLock(bool xAxis, MouseEvent at)
    {
        bool locked;
        lock (_gate) locked = xAxis ? _lockX is not null : _lockY is not null;
        if (xAxis) SetLock(x: locked ? null : at.X, clearX: locked);
        else SetLock(y: locked ? null : at.Y, clearY: locked);
    }

    /// <summary>"sticky.left", "sticky.left.x", "sticky.back": the first press holds the button down, the next lets go (or it lets go by itself after the set time).</summary>
    private void ToggleSticky(MouseAction action, MouseEvent at)
    {
        var parts = action.Id[MouseActionCatalog.StickyPrefix.Length..].Split('.');
        var button = parts[0] switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            "back" => MouseButton.Back,
            "forward" => MouseButton.Forward,
            _ => MouseButton.Left,
        };
        var axis = parts.Length > 1 ? parts[1] : null;

        int stick;
        lock (_gate)
        {
            if (_stuck.Remove(button)) stick = 0;
            else _stuck[button] = stick = ++_stickCount;
        }

        if (stick == 0)
        {
            LetGo(button, axis);
            return;
        }

        _queue.Post(() => InputSender.ButtonDown(button));
        if (axis == "x") SetLock(x: at.X);
        else if (axis == "y") SetLock(y: at.Y);

        var seconds = action.Options.Int(MouseOptions.ReleaseAfter);
        if (seconds > 0) _ = ReleaseLater(button, axis, stick, TimeSpan.FromSeconds(seconds));
    }

    /// <summary>Lets go of a sticky button after a while, unless it was already let go (or pressed again) in the meantime.</summary>
    private async Task ReleaseLater(MouseButton button, string? axis, int stick, TimeSpan after)
    {
        await Task.Delay(after);
        lock (_gate)
        {
            if (!_stuck.TryGetValue(button, out var current) || current != stick) return;
            _stuck.Remove(button);
        }
        LetGo(button, axis);
    }

    private void LetGo(MouseButton button, string? axis)
    {
        _queue.Post(() => InputSender.ButtonUp(button));
        if (axis == "x") SetLock(clearX: true);
        else if (axis == "y") SetLock(clearY: true);
    }

    /// <summary>Watches pointer movement only while a mode needs it.</summary>
    private void Refresh()
    {
        bool needed;
        lock (_gate) needed = _lockX is not null || _lockY is not null || _scroll.IsRunning || _dragging;
        _hook.MoveHandler = needed ? OnMove : null;
    }

    /// <summary>Runs on the hook thread for every pointer movement while a mode is active. Returns true to swallow the movement.</summary>
    private bool OnMove(int x, int y)
    {
        if (_scroll.OnMove(x, y)) return true;

        int toX, toY;
        lock (_gate) (toX, toY) = (_lockX ?? x, _lockY ?? y);
        if (toX != x || toY != y)
        {
            // Placed here rather than on the queue: the next movement is measured from wherever the pointer is, so it must already be
            // on the locked line. SetCursorPos only stores a position and returns at once.
            SystemCommands.SetCursorPos(toX, toY);
            return true;
        }

        if (_dragging)
        {
            Interlocked.Exchange(ref _dragTo, ((long)x << 32) | (uint)y);
            if (Interlocked.Exchange(ref _dragQueued, 1) == 0) _queue.Post(FollowDrag);
        }
        return false;
    }

    /// <summary>Queue thread: moves the dragged window to the newest pointer position.</summary>
    private void FollowDrag()
    {
        Volatile.Write(ref _dragQueued, 0);
        var to = Interlocked.Read(ref _dragTo);
        if (_dragging) _drag.Move((int)(to >> 32), (int)to);
    }

    private static ushort[] ParseKeys(string text) => KeyChord.TryParse(text, out var keys) ? keys : Array.Empty<ushort>();

    /// <summary>Lets go of everything still held (sticky buttons, locks, slowed pointer, pointer look) so quitting never leaves the mouse in a strange state.</summary>
    public void Dispose()
    {
        _hook.MoveHandler = null;
        _look.Dispose();
        List<MouseButton> stuck;
        lock (_gate)
        {
            stuck = _stuck.Keys.ToList();
            _stuck.Clear();
            _lockX = _lockY = null;
        }
        _scroll.Stop();
        _queue.Post(() =>
        {
            foreach (var button in stuck) InputSender.ButtonUp(button);
            _dragging = false;
            _drag.End();
            LeaveSlow();
        });
        _queue.Dispose(); // runs everything posted above before the thread ends
    }
}
