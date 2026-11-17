using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine.Mouse;

/// <summary>
/// System-wide low-level mouse hook on its own message-loop thread. Each handler (<see cref="AddHandler"/>) runs inside the hook, so it must return
/// quickly (Windows drops slow hooks); it returns true to swallow the input. Input made by <see cref="InputSender"/> is never reported back.
/// Inputs from other sources (trackpad gestures) are run through the same handlers on the same thread by <see cref="Raise"/>.
/// </summary>
public sealed class MouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const uint WmQuit = 0x0012, WmMouseMove = 0x200;
    private const uint WmRaise = 0x8000 + 1; // WM_APP + 1: an input from another source (a trackpad gesture), run on this thread
    private const uint WmReinstall = 0x8000 + 2; // WM_APP + 2: the watchdog found the hook dropped; put it back on this thread
    private const uint WmLButtonDown = 0x201, WmLButtonUp = 0x202, WmRButtonDown = 0x204, WmRButtonUp = 0x205;
    private const uint WmMButtonDown = 0x207, WmMButtonUp = 0x208, WmMouseWheel = 0x20A, WmXButtonDown = 0x20B, WmXButtonUp = 0x20C, WmMouseHWheel = 0x20E;

    [StructLayout(LayoutKind.Sequential)]
    private struct HookData { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg m, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint msg, UIntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    // Kept in a field so the garbage collector cannot free the callback while Windows still holds it.
    private readonly HookProc _proc;
    private readonly object _handlerLock = new();
    private volatile Func<MouseEvent, bool>[] _handlers = Array.Empty<Func<MouseEvent, bool>>();
    private volatile Func<int, int, bool>? _moveHandler;
    private uint _threadId;
    private bool _started;
    private long _lastCallback = Environment.TickCount64; // when Windows last called the hook; written by every call, read by the watchdog
    private readonly ManualResetEventSlim _stopWatchdog = new();

    private int _logged, _wheelLogged, _moveLogged, _diagnosed;

    // Log lines are written by their own thread: file writes inside the hook callback would slow every input down.
    private readonly ConcurrentQueue<string> _logLines = new();
    private readonly SemaphoreSlim _logReady = new(0);

    /// <summary>Where the hook writes what it did (installed or refused, first inputs, errors, slow handlers); set before <see cref="Start"/>. Called on a background thread.</summary>
    public Action<string>? Log { get; set; }

    public MouseHook() => _proc = Callback;

    private void Note(string line, ref int counter)
    {
        if (Log is null || Interlocked.Increment(ref counter) > CoreTiming.HookLogLimit) return;
        _logLines.Enqueue(line);
        _logReady.Release();
    }

    /// <summary>Lets the layers above add a line about what they did with an input (its own budget of lines per run). Never blocks.</summary>
    public void Diagnose(string line) => Note(line, ref _diagnosed);

    // Buttons have their own budget so a burst of wheel notches (or gestures) cannot use up the log before a button is pressed.
    private void Note(MouseButton button, string line) => Note(line, ref button.HasRelease() ? ref _logged : ref _wheelLogged);

    // Asked before a routine line is built, so the hook formats no text once the log is off or its budget is spent.
    private bool Wants(MouseButton button) =>
        Log is not null && Volatile.Read(ref button.HasRelease() ? ref _logged : ref _wheelLogged) < CoreTiming.HookLogLimit;

    // Reads the hook's data in place: Marshal.PtrToStructure boxes a copy on every input.
    private static unsafe HookData Read(IntPtr lParam) => *(HookData*)lParam;

    /// <summary>
    /// Asked about every pointer movement (x, y), but only while it is set, so an idle hook costs nothing on movement.
    /// Return true to swallow the movement: the pointer then stays where it is. Runs inside the hook, so it must only record what it
    /// needs and hand any real work to another thread. Set it to null when no longer needed.
    /// </summary>
    public Func<int, int, bool>? MoveHandler
    {
        get => _moveHandler;
        set => _moveHandler = value;
    }

    /// <summary>
    /// Asked before the handlers for every press, wheel step and raised input; true passes the input untouched (no handler sees it). A
    /// release still reaches the handlers, so what a handler swallowed on press is never left held. Runs on the hook thread: it must only read
    /// what is already known.
    /// </summary>
    public Func<bool>? PassAll { get; set; }

    /// <summary>
    /// Adds a handler that decides what happens to an input: return true to swallow it, false to let it go on to the next handler and
    /// then to Windows. Handlers are asked in the order they were added (one added with <paramref name="first"/> ahead of all the
    /// others); the first to return true wins.
    /// </summary>
    public void AddHandler(Func<MouseEvent, bool> handler, bool first = false)
    {
        lock (_handlerLock) _handlers = first ? _handlers.Prepend(handler).ToArray() : _handlers.Append(handler).ToArray();
    }

    /// <summary>Installs the hook. Returns false when Windows refuses it.</summary>
    public bool Start()
    {
        if (_started) return true;
        new Thread(WriteLog) { IsBackground = true, Name = "MouseHookLog" }.Start();
        var ok = false;
        var ready = new ManualResetEventSlim();
        // Highest priority: Windows holds back all mouse input while the callback runs, and gestures must be seen at once.
        new Thread(() => Loop(ready, v => ok = v)) { IsBackground = true, Name = "MouseHook", Priority = ThreadPriority.Highest }.Start();
        ready.Wait();
        _started = ok;
        if (ok) new Thread(Watchdog) { IsBackground = true, Name = "MouseHookWatchdog", Priority = ThreadPriority.BelowNormal }.Start();
        return ok;
    }

    public void Dispose()
    {
        if (!_started) return;
        _started = false;
        _stopWatchdog.Set();
        PostThreadMessage(_threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Writes queued log lines; a background thread, so it simply ends with the program.</summary>
    private void WriteLog()
    {
        while (true)
        {
            _logReady.Wait();
            if (_logLines.TryDequeue(out var line)) Log?.Invoke(line);
        }
    }

    private void Loop(ManualResetEventSlim ready, Action<bool> installed)
    {
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // creates this thread's message queue
        _threadId = GetCurrentThreadId();
        var hook = Install("hook installed");
        installed(hook != IntPtr.Zero);
        ready.Set();
        if (hook == IntPtr.Zero) return;

        // The hook is driven by this loop; the only message to handle is an input raised from another source.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.Message == WmRaise && SystemCommands.GetCursorPos() is { } at) RunHandlers(new MouseEvent((MouseButton)msg.WParam, true, at.X, at.Y));
            else if (msg.Message == WmReinstall)
            {
                if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
                hook = Install("hook reinstalled (Windows had dropped it)");
            }
        }
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
    }

    /// <summary>Installs the hook on the calling (hook) thread and logs the outcome; zero when Windows refuses.</summary>
    private IntPtr Install(string successLine)
    {
        var hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
        Note(hook == IntPtr.Zero ? $"hook refused, error {Marshal.GetLastWin32Error()}" : successLine, ref _logged);
        Volatile.Write(ref _lastCallback, Environment.TickCount64);
        return hook;
    }

    /// <summary>
    /// Windows silently removes a hook whose callback was too slow. Every <see cref="CoreTiming.HookWatchdogMs"/> this checks whether the
    /// pointer moved and input happened since the last look while the hook was never called; if so the hook is reinstalled on its thread.
    /// It only reads the pointer and the idle time; it never sends input.
    /// </summary>
    private void Watchdog()
    {
        var lastLook = Environment.TickCount64;
        var lastAt = SystemCommands.GetCursorPos();
        while (!_stopWatchdog.Wait(CoreTiming.HookWatchdogMs))
        {
            var now = Environment.TickCount64;
            var at = SystemCommands.GetCursorPos();
            var moved = at is not null && lastAt is not null && at != lastAt;
            var inputSince = Engine.PowerState.IdleFor().TotalMilliseconds < now - lastLook;
            var silent = Volatile.Read(ref _lastCallback) < lastLook;
            lastLook = now;
            lastAt = at;
            if (moved && inputSince && silent && _started) PostThreadMessage(_threadId, WmReinstall, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>
    /// Passes an input that does not come through the hook (a recognised trackpad gesture) to the same handlers, in the same order and
    /// on the hook's own thread, with the pointer where it is then. It cannot be swallowed (Windows has already had it); a handler that
    /// takes it only stops the handlers after it. Never blocks: the input is posted to the hook thread.
    /// </summary>
    public void Raise(MouseButton button)
    {
        if (_started) PostThreadMessage(_threadId, WmRaise, (UIntPtr)(uint)button, IntPtr.Zero);
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        Volatile.Write(ref _lastCallback, Environment.TickCount64);
        if (code >= 0 && (uint)wParam == WmMouseMove && MoveHandler is { } onMove)
        {
            var move = Read(lParam);
            try
            {
                if (move.ExtraInfo != InputSender.Marker && onMove(move.X, move.Y))
                {
                    if (_moveLogged < 5) Note($"  -> move to {move.X},{move.Y} swallowed", ref _moveLogged); // a few show the movement is caught
                    return (IntPtr)1;
                }
            }
            catch (Exception ex)
            {
                // As below: an exception must never escape the hook; the movement passes through and the error is logged.
                Note($"move handler failed: {ex}", ref _moveLogged);
            }
        }
        else if (code >= 0 && (uint)wParam != WmMouseMove && _handlers.Length > 0)
        {
            var data = Read(lParam);
            if (data.ExtraInfo != InputSender.Marker && Translate((uint)wParam, data) is { } ev && RunHandlers(ev)) return (IntPtr)1;
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>Asks the handlers in order; true when one took the input. Runs on the hook thread only.</summary>
    private bool RunHandlers(MouseEvent ev)
    {
        if (ev.IsDown && PassAll?.Invoke() == true) return false;
        if (ev.Button is not (MouseButton.Left or MouseButton.Right) && Wants(ev.Button)) Note(ev.Button, $"hook sees {ev.Button} {(ev.IsDown ? "down" : "up")}"); // plain clicks would use up the log
        var started = Environment.TickCount64;
        foreach (var handler in _handlers)
        {
            try
            {
                if (handler(ev))
                {
                    if (Wants(ev.Button)) Note(ev.Button, $"  -> {ev.Button} {(ev.IsDown ? "down" : "up")} taken");
                    return true;
                }
            }
            catch (Exception ex)
            {
                // An exception escaping a hook callback would take the whole app down; the input passes through and the error is logged.
                Note(ev.Button, $"handler failed on {ev.Button}: {ex}");
            }
        }
        var took = Environment.TickCount64 - started;
        if (took >= CoreTiming.HookSlowMs) Note(ev.Button, $"handlers were slow: {took} ms (Windows drops a hook that takes too long)");
        return false;
    }

    private static MouseEvent? Translate(uint message, HookData d)
    {
        var high = (short)(d.MouseData >> 16);
        return message switch
        {
            WmLButtonDown => new MouseEvent(MouseButton.Left, true, d.X, d.Y),
            WmLButtonUp => new MouseEvent(MouseButton.Left, false, d.X, d.Y),
            WmRButtonDown => new MouseEvent(MouseButton.Right, true, d.X, d.Y),
            WmRButtonUp => new MouseEvent(MouseButton.Right, false, d.X, d.Y),
            WmMButtonDown => new MouseEvent(MouseButton.Middle, true, d.X, d.Y),
            WmMButtonUp => new MouseEvent(MouseButton.Middle, false, d.X, d.Y),
            WmXButtonDown or WmXButtonUp => new MouseEvent(high == 1 ? MouseButton.Back : MouseButton.Forward, message == WmXButtonDown, d.X, d.Y),
            WmMouseWheel => new MouseEvent(high > 0 ? MouseButton.WheelUp : MouseButton.WheelDown, true, d.X, d.Y),
            WmMouseHWheel => new MouseEvent(high > 0 ? MouseButton.TiltRight : MouseButton.TiltLeft, true, d.X, d.Y),
            _ => null,
        };
    }
}
