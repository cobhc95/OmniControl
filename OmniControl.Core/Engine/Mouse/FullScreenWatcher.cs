namespace OmniControl.Core.Engine.Mouse;

/// Keeps the answer to "is a full-screen app (a game, a video) in front?" ready, so the mouse hook only reads a flag. While enabled a worker
/// asks Windows again only when Windows says the window in front changed or a top-level window moved or resized (WinEvent hooks, nothing
/// polled; a video going full screen in the same window is a resize); where Windows refuses the hooks it asks every
/// <see cref="CoreTiming.FullScreenWatchMs"/>. Stopped, or when Windows can't say, the answer is no (fail open).
public sealed class FullScreenWatcher
{
    // SHQueryUserNotificationState: 2 = a full-screen app that is not a Direct3D one, 3 = a full-screen Direct3D app (most games).
    private const int Busy = 2, RunningD3DFullScreen = 3;
    private const uint EventSystemForeground = 0x0003, EventObjectLocationChange = 0x800B, OutOfContext = 0x0000, WmQuit = 0x0012;
    private const int ObjIdWindow = 0;

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);

    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern int GetMessage(out NativeMessage m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage { public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; }

    private readonly object _gate = new();
    private readonly WinEventProc _proc; // kept in a field so the garbage collector cannot free the callback while Windows holds it
    private volatile bool _active;
    private bool _enabled;
    private Thread? _worker;
    private uint _workerThread;

    public FullScreenWatcher() => _proc = OnEvent;

    /// True while a full-screen app is in front and the watcher is enabled. Cheap: reads a flag.
    public bool IsActive => _active;

    /// Asks Windows once whether a full-screen app is in front (false when it can't say).
    public static bool AppInFront() => SHQueryUserNotificationState(out var state) == 0 && state is Busy or RunningD3DFullScreen;

    /// Starts or stops the worker; stopped, the answer is no.
    public void SetEnabled(bool on)
    {
        lock (_gate)
        {
            if (_enabled == on) return;
            _enabled = on;
            if (!on)
            {
                _active = false;
                if (_workerThread != 0) PostThreadMessage(_workerThread, WmQuit, UIntPtr.Zero, IntPtr.Zero); // ends its message loop
                return;
            }
            if (_worker is { IsAlive: true }) return; // the old worker has not noticed it was stopped yet and carries on
            _worker = new Thread(Watch) { IsBackground = true, Name = "FullScreenWatcher", Priority = ThreadPriority.BelowNormal };
            _worker.Start();
        }
    }

    private bool Enabled { get { lock (_gate) return _enabled; } }

    private void Watch()
    {
        lock (_gate) _workerThread = GetCurrentThreadId();
        var foreground = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _proc, 0, 0, OutOfContext);
        var moved = SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero, _proc, 0, 0, OutOfContext);
        try
        {
            _active = Enabled && AppInFront();
            if (foreground != IntPtr.Zero && moved != IntPtr.Zero)
            {
                while (Enabled && GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
                return;
            }
            while (Enabled)
            {
                Thread.Sleep(CoreTiming.FullScreenWatchMs);
                _active = Enabled && AppInFront();
            }
        }
        finally
        {
            if (foreground != IntPtr.Zero) UnhookWinEvent(foreground);
            if (moved != IntPtr.Zero) UnhookWinEvent(moved);
            lock (_gate) _workerThread = 0;
            _active = false;
        }
    }

    // On the worker (its message loop): only a move or resize of the window in front counts (not the caret's, the cursor's or another window's).
    private void OnEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (eventType == EventObjectLocationChange && (objectId != ObjIdWindow || childId != 0 || window != GetForegroundWindow())) return;
        _active = Enabled && AppInFront();
    }
}
