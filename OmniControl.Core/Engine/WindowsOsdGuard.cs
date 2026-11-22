namespace OmniControl.Core.Engine;

/// Keeps one known Windows pop-up window (<see cref="WindowsOsdWindows"/>) off screen the moment Windows shows or moves it back, so no frame
/// is seen: out-of-context WinEvent hooks filtered to its process, on a worker with its own message loop, park it with one asynchronous
/// SetWindowPos inside the callback. Moving off screen is used because another process's window can't be cloaked (DWMWA_CLOAK is refused)
/// and hiding or making it layered fights Explorer's XAML host; Explorer only moves it back on each show, which is the event caught here.
/// Before the pop-up is known, <see cref="LookOut"/> catches Windows creating or first showing it (Explorer makes it lazily) the same way.
/// Runs only between <see cref="Watch"/> or <see cref="LookOut"/> and <see cref="Stop"/>.
public sealed class WindowsOsdGuard
{
    private const uint EventObjectCreate = 0x8000, EventObjectShow = 0x8002, EventObjectLocationChange = 0x800B;
    private const uint OutOfContext = 0x0000, SkipOwnProcess = 0x0002, WmQuit = 0x0012;
    private const int ObjIdWindow = 0;

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern int GetMessage(out NativeMessage m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage { public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; }

    private readonly object _gate = new();
    private readonly WinEventProc _proc; // kept in a field so the garbage collector cannot free the callback while Windows holds it
    private IntPtr _target;              // read by the callback; zero stops it touching anything at once
    private Action<IntPtr, WindowPlace>? _found; // while looking out: told once about the pop-up parked on its creation or first show
    private int _run;                    // which Watch or LookOut is current; a worker of an older one ends
    private uint _workerThread;
    private int _parked;

    public WindowsOsdGuard() => _proc = OnEvent;

    /// How many times the hooks parked the window since the last <see cref="Watch"/>.
    public int Parked => Volatile.Read(ref _parked);

    /// True while hooks are installed for a window.
    public bool Hooked { get { lock (_gate) return _workerThread != 0 && Volatile.Read(ref _target) != IntPtr.Zero; } }

    /// Starts keeping <paramref name="window"/> of process <paramref name="processId"/> off screen (replaces any earlier window).
    public void Watch(IntPtr window, int processId)
    {
        Stop();
        Volatile.Write(ref _parked, 0);
        Volatile.Write(ref _target, window);
        Start((uint)processId, EventObjectShow, watchMoves: true);
    }

    /// Until <see cref="Stop"/>, the first window of process <paramref name="processId"/> created or shown that looks like the pop-up
    /// (<see cref="WindowsOsdWindows.LooksLikeFlyout"/>) is moved off screen and minimised in the callback, then reported once to
    /// <paramref name="found"/> with the place it had (on the worker thread).
    public void LookOut(int processId, Action<IntPtr, WindowPlace> found)
    {
        Stop();
        Volatile.Write(ref _found, found);
        Start((uint)processId, EventObjectCreate, watchMoves: false);
    }

    /// Stops at once (the callback ignores everything from now) and ends the worker's hooks.
    public void Stop()
    {
        Volatile.Write(ref _target, IntPtr.Zero);
        Volatile.Write(ref _found, null);
        Interlocked.Increment(ref _run);
        lock (_gate)
            if (_workerThread != 0) PostThreadMessage(_workerThread, WmQuit, UIntPtr.Zero, IntPtr.Zero);
    }

    private void Start(uint processId, uint firstEvent, bool watchMoves)
    {
        var run = Volatile.Read(ref _run);
        new Thread(() => Run(run, processId, firstEvent, watchMoves)) { IsBackground = true, Name = "WindowsOsdGuard" }.Start();
    }

    private void Run(int run, uint processId, uint firstEvent, bool watchMoves)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _run) != run) return; // stopped or replaced before this worker began
            _workerThread = GetCurrentThreadId();
        }
        var shown = SetWinEventHook(firstEvent, EventObjectShow, IntPtr.Zero, _proc, processId, 0, OutOfContext | SkipOwnProcess);
        var moved = watchMoves ? SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero, _proc, processId, 0, OutOfContext | SkipOwnProcess) : IntPtr.Zero;
        try
        {
            if (shown != IntPtr.Zero && (moved != IntPtr.Zero || !watchMoves))
                while (Volatile.Read(ref _run) == run && GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        }
        finally
        {
            if (shown != IntPtr.Zero) UnhookWinEvent(shown);
            if (moved != IntPtr.Zero) UnhookWinEvent(moved);
            lock (_gate) if (_workerThread == GetCurrentThreadId()) _workerThread = 0;
        }
    }

    // On the worker: only the window itself counts; the shape check keeps a reused handle showing something else untouched.
    private void OnEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (objectId != ObjIdWindow || childId != 0 || window == IntPtr.Zero) return;
        if (Volatile.Read(ref _found) is { } found)
        {
            if (WindowsOsdWindows.IsParked(window) || !WindowsOsdWindows.LooksLikeFlyout(window) || WindowsOsdWindows.PlaceOf(window) is not { } place) return;
            if (Interlocked.CompareExchange(ref _found, null, found) != found) return; // reported once
            WindowsOsdWindows.Park(window);
            WindowsOsdWindows.Minimise(window);
            found(window, place);
            return;
        }
        if (window != Volatile.Read(ref _target)) return;
        if (WindowsOsdWindows.IsParked(window) || !WindowsOsdWindows.HasFlyoutShape(window)) return;
        WindowsOsdWindows.Park(window);
        Interlocked.Increment(ref _parked);
    }
}
