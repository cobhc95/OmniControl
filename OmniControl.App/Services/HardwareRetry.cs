namespace OmniControl.App.Services;

/// <summary>
/// The retry schedule of one hardware watch after an announcement (<see cref="HardwareWatches"/>): the hardware is often not ready at the first
/// look (after a return from Remote Desktop the monitors may not answer for several seconds), so it is refreshed at each time of the schedule,
/// counted from the last announcement of a burst, until the watch's own check says it looks right. Before a later try the check is asked first, so
/// a Refresh made meanwhile ends the schedule without another refresh. Every try and its result is one line in the log.
/// </summary>
internal sealed class HardwareRetry : IDisposable
{
    private readonly string _name;
    private readonly IReadOnlyList<TimeSpan> _schedule;
    private readonly Action _refresh;
    private readonly Func<bool> _looksStale;
    private readonly Action<string> _log;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private int _step;
    private int _generation; // a new announcement during a try starts the schedule again; the old try then schedules nothing
    private bool _running, _pending, _disposed;

    /// <param name="schedule">When to try, each counted from the announcement (at least one entry, rising).</param>
    public HardwareRetry(string name, IReadOnlyList<TimeSpan> schedule, Action refresh, Func<bool> looksStale, Action<string> log)
    {
        if (schedule.Count == 0) throw new ArgumentException("A retry schedule needs at least one time.", nameof(schedule));
        _name = name;
        _schedule = schedule;
        _refresh = refresh;
        _looksStale = looksStale;
        _log = log;
        _timer = new Timer(_ => Tick());
    }

    /// <summary>An announcement arrived (any thread): the schedule starts again from its first time.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _step = 0;
            _generation++;
            _timer.Change(_schedule[0], Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Timer thread: one try.</summary>
    private void Tick()
    {
        int step, generation;
        lock (_gate)
        {
            if (_disposed) return;
            if (_running) { _pending = true; return; } // the last refresh is still going: this try runs right after it
            _running = true;
            step = _step;
            generation = _generation;
        }
        var tries = $"try {step + 1} of {_schedule.Count}";
        bool? done = null;
        try
        {
            if (step > 0 && !Stale())
            {
                _log($"{_name}: looks right before {tries}; no refresh needed.");
                done = true;
                return;
            }
            var failure = Guard.Run($"Hardware refresh {_name}", _refresh);
            done = !Stale();
            _log(failure is not null ? $"{_name}: {tries} failed ({failure.GetType().Name}: {failure.Message})."
                : done.Value ? $"{_name}: {tries} found it; stopping."
                : step + 1 < _schedule.Count ? $"{_name}: {tries} still looks stale; next try in {(_schedule[step + 1] - _schedule[step]).TotalSeconds:0.#} s."
                : $"{_name}: {tries} still looks stale; giving up until the next announcement, the periodic check or Refresh.");
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
                if (!_disposed)
                {
                    if (_pending) { _pending = false; _timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan); }
                    else if (generation == _generation && done == false && step + 1 < _schedule.Count)
                    {
                        _step = step + 1;
                        _timer.Change(_schedule[step + 1] - _schedule[step], Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }
    }

    /// <summary>The watch's own check; one that throws counts as stale (logged), so the schedule goes on rather than stopping on a bug.</summary>
    private bool Stale()
    {
        var stale = true;
        Guard.Run($"Hardware check {_name}", () => stale = _looksStale());
        return stale;
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        _timer.Dispose();
    }
}
