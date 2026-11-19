namespace OmniControl.Core.Engine.Mouse;

/// Keeps the answer to "is a full-screen app (a game, a video) in front?" ready, so the mouse hook only reads a flag. While enabled a worker
/// asks Windows every <see cref="CoreTiming.FullScreenWatchMs"/>; stopped, or when Windows can't say, the answer is no (fail open).
public sealed class FullScreenWatcher
{
    // SHQueryUserNotificationState: 2 = a full-screen app that is not a Direct3D one, 3 = a full-screen Direct3D app (most games).
    private const int Busy = 2, RunningD3DFullScreen = 3;

    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

    private readonly object _gate = new();
    private volatile bool _active;
    private bool _enabled;
    private Thread? _worker;

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
            if (!on) { _active = false; return; }
            if (_worker is { IsAlive: true }) return; // the old worker has not noticed it was stopped yet and carries on
            _worker = new Thread(Watch) { IsBackground = true, Name = "FullScreenWatcher", Priority = ThreadPriority.BelowNormal };
            _worker.Start();
        }
    }

    private bool Enabled { get { lock (_gate) return _enabled; } }

    private void Watch()
    {
        while (Enabled)
        {
            _active = AppInFront();
            Thread.Sleep(CoreTiming.FullScreenWatchMs);
        }
    }
}
