namespace OmniControl.App.Shared.Services;

/// The retry schedule of one hardware watch after an announcement or at start (<see cref="HardwareWatches"/>): the hardware is often not
/// ready at the first look (after a return from Remote Desktop, an unlock or a wake the monitors may not answer for several seconds), so it is
/// refreshed at each time of the schedule, counted from the last announcement of a burst, until the watch's own check
/// (<see cref="HardwareWatch.LooksUnsettled"/>, else <see cref="HardwareWatch.LooksStale"/>) says it looks right. Before a later try, and
/// before the first one when the schedule was started with "check first", the check is asked first, so a Refresh made meanwhile ends the
/// schedule without another refresh. Every try is one line in the log: what started it, its result and what the watch found.
internal sealed class HardwareRetry : IDisposable
{
    private readonly HardwareWatch _watch;
    private readonly IReadOnlyList<TimeSpan> _schedule;
    private readonly Func<bool> _looksStale;
    private readonly Action<string> _log;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private int _step;
    private int _generation; // a new announcement during a try starts the schedule again; the old try then schedules nothing
    private string _trigger = string.Empty;
    private bool _checkFirst;
    private bool _running, _pending, _disposed;

    /// <param name="watch">The watch; it must have <see cref="HardwareWatch.LooksStale"/>.</param>
    /// <param name="schedule">When to try, each counted from the announcement (at least one entry, rising).</param>
    public HardwareRetry(HardwareWatch watch, IReadOnlyList<TimeSpan> schedule, Action<string> log)
    {
        if (schedule.Count == 0) throw new ArgumentException("A retry schedule needs at least one time.", nameof(schedule));
        _watch = watch;
        _looksStale = watch.LooksUnsettled ?? watch.LooksStale ?? throw new ArgumentException("A retried watch needs a stale check.", nameof(watch));
        _schedule = schedule;
        _log = log;
        _timer = new Timer(_ => Tick());
    }

    /// An announcement arrived, or the program started (any thread): the schedule starts again from its first time.
    /// <param name="trigger">What started it, for the log ("after Session", "start").</param>
    /// <param name="checkFirst">True: the first try too refreshes only when the check says the hardware looks wrong (start-up, screens back on).</param>
    public void Start(string trigger, bool checkFirst)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _step = 0;
            _generation++;
            _trigger = trigger;
            _checkFirst = checkFirst;
            _timer.Change(_schedule[0], Timeout.InfiniteTimeSpan);
        }
    }

    /// Timer thread: one try.
    private void Tick()
    {
        int step, generation;
        string trigger;
        bool checkFirst;
        lock (_gate)
        {
            if (_disposed) return;
            if (_running) { _pending = true; return; } // the last refresh is still going: this try runs right after it
            _running = true;
            (step, generation, trigger, checkFirst) = (_step, _generation, _trigger, _checkFirst);
        }
        var name = $"{_watch.Name} ({trigger})";
        var tries = $"try {step + 1} of {_schedule.Count}";
        bool? done = null;
        try
        {
            if ((step > 0 || checkFirst) && !Stale())
            {
                _log($"{name}: looks right before {tries}; no refresh needed.{_watch.Found()}");
                done = true;
                return;
            }
            var failure = Guard.Run($"Hardware refresh {_watch.Name}", _watch.Refresh);
            done = !Stale();
            _log((failure is not null ? $"{name}: {tries} failed ({failure.GetType().Name}: {failure.Message})."
                : done.Value ? $"{name}: {tries} found it; stopping."
                : step + 1 < _schedule.Count ? $"{name}: {tries} still looks stale; next try in {(_schedule[step + 1] - _schedule[step]).TotalSeconds:0.#} s."
                : $"{name}: {tries} still looks stale; giving up until the next announcement, the periodic check or Refresh.") + _watch.Found());
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

    /// The watch's own check; one that throws counts as stale (logged), so the schedule goes on rather than stopping on a bug.
    private bool Stale()
    {
        var stale = true;
        Guard.Run($"Hardware check {_watch.Name}", () => stale = _looksStale());
        return stale;
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        _timer.Dispose();
    }
}
