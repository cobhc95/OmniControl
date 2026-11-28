namespace OmniControl.App.Shared.Services;

/// Watches that the UI thread keeps answering while the main window is shown: a timer posts a heartbeat every
/// <see cref="Timing.UiHeartbeatEvery"/>, and when the UI thread has left one unrun for <see cref="Timing.UiStuckAfter"/> or more
/// (seen at the next look) the stall and its length are written once to <see cref="LogFile"/> (no thread stacks: reading them needed a
/// debugging library that doubled the download). Paused while the app sits hidden in the tray so the UI thread stays idle. One log per
/// stall, at most <see cref="Timing.UiStuckLogsPerRun"/> per run; it never acts on the stall, it only records it.
public sealed class UiWatchdog : IDisposable
{
    public const string LogFile = LogFiles.Hang;

    private readonly object _gate = new();
    private readonly Timer _timer;
    private long _lastBeat = Environment.TickCount64;
    private long _posted;
    private int _logs;
    private bool _stuckLogged, _active, _disposed;

    /// Creates the watchdog; call <see cref="SetActive"/> when the main window shows or hides.
    public UiWatchdog() => _timer = new Timer(_ => Tick());

    /// Runs the heartbeat only while a window is shown; paused in the tray so the UI thread stays completely idle.
    public void SetActive(bool active)
    {
        lock (_gate)
        {
            if (_disposed || _active == active) return;
            _active = active;
            _posted = 0;
            _stuckLogged = false;
            Interlocked.Exchange(ref _lastBeat, Environment.TickCount64);
            _timer.Change(active ? Timing.UiHeartbeatEvery : Timeout.InfiniteTimeSpan, active ? Timing.UiHeartbeatEvery : Timeout.InfiniteTimeSpan);
        }
    }

    // Each look first checks whether the UI thread ran the heartbeat posted at the look before, then posts the next one.
    private void Tick()
    {
        if (!Monitor.TryEnter(_gate)) return;
        try
        {
            if (_disposed || !_active) return;
            Guard.Run("UI watchdog", () =>
            {
                var now = Environment.TickCount64;
                var beat = Interlocked.Read(ref _lastBeat);
                // Still unrun: the stall so far; run late: how late (a stall that ended between two looks).
                var silent = _posted == 0 ? 0 : beat < _posted ? now - _posted : beat - _posted;
                if (silent < Timing.UiStuckAfter.TotalMilliseconds) _stuckLogged = false;
                else if (!_stuckLogged && _logs < Timing.UiStuckLogsPerRun)
                {
                    _stuckLogged = true;
                    _logs++;
                    AppDataStore.AppendLog(LogFile, $"The UI thread has not answered for {silent} ms.");
                }
                if (beat < _posted) return; // the post before is still waiting: no second one behind it
                _posted = now;
                Dispatcher.UIThread.Post(() => Interlocked.Exchange(ref _lastBeat, Environment.TickCount64), DispatcherPriority.Send);
            });
        }
        finally { Monitor.Exit(_gate); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _active = false;
        }
        _timer.Dispose();
    }
}
