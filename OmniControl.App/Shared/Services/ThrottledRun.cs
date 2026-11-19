namespace OmniControl.App.Shared.Services;

/// Runs a piece of work on a worker thread, however often it is asked for, at most once per <c>gap</c>. A request that arrives inside the
/// gap is not lost: the work runs when the gap is over. For checks that are cheap but must not run in a storm (a display change
/// announced several times in a second).
public sealed class ThrottledRun : IDisposable
{
    private readonly TimeSpan _gap;
    private readonly Action _work;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private DateTime _lastStart = DateTime.MinValue;
    private DateTime? _due;
    private bool _running;
    private bool _disposed;

    public ThrottledRun(TimeSpan gap, Action work)
    {
        _gap = gap;
        _work = work;
        _timer = new Timer(_ => Run());
    }

    /// Asks for a run, no sooner than <paramref name="delay"/> from now and no sooner than the gap after the last run began.
    public void Request(TimeSpan? delay = null)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var due = DateTime.UtcNow + (delay ?? TimeSpan.Zero);
            var earliest = _lastStart + _gap;
            if (due < earliest) due = earliest;
            if (_due is { } waiting && waiting <= due) return; // an earlier run is already waiting
            _due = due;
            var wait = due - DateTime.UtcNow;
            _timer.Change(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }

    private void Run()
    {
        lock (_gate)
        {
            _due = null;
            if (_disposed) return;
            if (_running) // the work outlasted the gap: try again after it
            {
                _due = DateTime.UtcNow + _gap;
                _timer.Change(_gap, Timeout.InfiniteTimeSpan);
                return;
            }
            _running = true;
            _lastStart = DateTime.UtcNow;
        }
        // A timer-thread throw would end the process: log it and stay ready for the next request.
        try { Guard.Run("Throttled work", _work); }
        finally { lock (_gate) _running = false; }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        _timer.Dispose();
    }
}
