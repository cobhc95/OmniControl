namespace OmniControl.Core.Engine;

/// Keeps one known Windows pop-up window (<see cref="WindowsOsdWindows"/>) off screen the moment Windows shows or moves it back, so no frame
/// is seen: out-of-context WinEvent hooks filtered to its process, on a worker with its own message loop, park it with one asynchronous
/// SetWindowPos inside the callback. Moving off screen is used because another process's window can't be cloaked (DWMWA_CLOAK is refused)
/// and hiding or making it layered fights Explorer's XAML host; Explorer only moves it back on each show, which is the event caught here.
/// Runs only between <see cref="Watch"/> and <see cref="Stop"/>.
public sealed class WindowsOsdGuard
{
    private const uint EventObjectShow = 0x8002, EventObjectLocationChange = 0x800B, OutOfContext = 0x0000, SkipOwnProcess = 0x0002, WmQuit = 0x0012;
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
    private uint _workerThread;
    private int _parked;

    public WindowsOsdGuard() => _proc = OnEvent;

    /// How many times the hooks parked the window since the last <see cref="Watch"/>.
    public int Parked => Volatile.Read(ref _parked);

    /// True while hooks are installed for a window.
    public bool Hooked { get { lock (_gate) return _workerThread != 0; } }

    /// Starts keeping <paramref name="window"/> of process <paramref name="processId"/> off screen (replaces any earlier window).
    public void Watch(IntPtr window, int processId)
    {
        Stop();
        Volatile.Write(ref _parked, 0);
        Volatile.Write(ref _target, window);
        new Thread(() => Run(window, (uint)processId)) { IsBackground = true, Name = "WindowsOsdGuard" }.Start();
    }

    /// Stops at once (the callback ignores everything from now) and ends the worker's hooks.
    public void Stop()
    {
        Volatile.Write(ref _target, IntPtr.Zero);
        lock (_gate)
            if (_workerThread != 0) PostThreadMessage(_workerThread, WmQuit, UIntPtr.Zero, IntPtr.Zero);
    }

    private void Run(IntPtr window, uint processId)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _target) != window) return; // stopped or replaced before this worker began
            _workerThread = GetCurrentThreadId();
        }
        var shown = SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, _proc, processId, 0, OutOfContext | SkipOwnProcess);
        var moved = SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero, _proc, processId, 0, OutOfContext | SkipOwnProcess);
        try
        {
            if (shown != IntPtr.Zero && moved != IntPtr.Zero)
                while (Volatile.Read(ref _target) == window && GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
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
        if (objectId != ObjIdWindow || childId != 0 || window == IntPtr.Zero || window != Volatile.Read(ref _target)) return;
        if (WindowsOsdWindows.IsParked(window) || !WindowsOsdWindows.HasFlyoutShape(window)) return;
        WindowsOsdWindows.Park(window);
        Interlocked.Increment(ref _parked);
    }
}
