namespace OmniControl.Core.Engine.Screenshot;

/// A recording's own time: it runs from the start, stands still while paused, and goes on from where it stopped on resume, so the video
/// has no gap for a pause. In 100-nanosecond units. Thread-safe. The real time comes from <paramref name="now"/> (the stopwatch by default,
/// a fake one in tests).
public sealed class RecordingClock
{
    private readonly Func<long> _now;
    private readonly object _gate = new();
    private readonly long _startedAt;
    private long _pausedAt;
    private long _pausedFor;
    private bool _paused;

    public RecordingClock(Func<long>? now = null)
    {
        _now = now ?? (() => Stopwatch.GetTimestamp() * VideoMath.TicksPerSecond / Stopwatch.Frequency);
        _startedAt = _now();
    }

    public bool IsPaused
    {
        get { lock (_gate) return _paused; }
    }

    /// The recording's time now (not counting pauses).
    public long Ticks
    {
        get
        {
            lock (_gate) return (_paused ? _pausedAt : _now()) - _startedAt - _pausedFor;
        }
    }

    public TimeSpan Elapsed => TimeSpan.FromTicks(Ticks);

    public void Pause()
    {
        lock (_gate)
        {
            if (_paused) return;
            _paused = true;
            _pausedAt = _now();
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (!_paused) return;
            _paused = false;
            _pausedFor += _now() - _pausedAt;
        }
    }
}
