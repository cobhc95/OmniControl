namespace OmniControl.Core.Engine;

/// The program the user is working in, kept ready by a worker so the mouse hook never looks a program up itself: Windows tells the worker
/// each time another window comes to the front (a WinEvent hook, nothing polled); where Windows refuses that, the worker looks every
/// <see cref="CoreTiming.ForegroundWatchMs"/>. The worker starts with the first question and is a background thread, so it ends with the program.
public sealed class ForegroundProgram
{
    private const uint EventSystemForeground = 0x0003, OutOfContext = 0x0000;

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern int GetMessage(out NativeMessage m, IntPtr h, uint min, uint max);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage { public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; }

    private readonly ProgramIdentity _identity = new();
    private readonly object _gate = new();
    private readonly WinEventProc _proc; // kept in a field so the garbage collector cannot free the callback while Windows holds it
    private volatile string _name = string.Empty;
    private bool _started;

    public ForegroundProgram() => _proc = OnForeground;

    /// The foreground program's identity (<see cref="ProgramIdentity.Normalize"/>), or empty; as of the last change of the window in front.
    public string Name
    {
        get
        {
            Start();
            return _name;
        }
    }

    private void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            _name = _identity.Foreground(); // the very first answer needs no wait
            new Thread(Watch) { IsBackground = true, Name = "ForegroundProgram", Priority = ThreadPriority.BelowNormal }.Start();
        }
    }

    /// The worker: Windows' foreground notices arrive through this thread's message loop; without them, a look every interval.
    private void Watch()
    {
        if (SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _proc, 0, 0, OutOfContext) != IntPtr.Zero)
        {
            _name = _identity.Foreground(); // a change between the first answer and the hook
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            return;
        }
        while (true)
        {
            Thread.Sleep(CoreTiming.ForegroundWatchMs);
            _name = _identity.Foreground();
        }
    }

    private void OnForeground(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time) =>
        _name = _identity.Foreground();
}
