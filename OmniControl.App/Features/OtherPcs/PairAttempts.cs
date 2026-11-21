namespace OmniControl.App.Features.OtherPcs;

/// The one pairing that runs now, either way round: each gets its own number and its own stop (linked to <paramref name="outer"/>), so closing
/// the panel ends its exchange at once and a result that comes after it was given up is dropped by its number. Any thread.
internal sealed class PairAttempts(CancellationToken outer)
{
    private readonly object _lock = new();
    private CancellationTokenSource? _running;
    private long _last;
    private bool _givenUp;

    /// Starts an attempt; null while one runs.
    public (long Id, CancellationToken Stop)? TryBegin()
    {
        lock (_lock)
        {
            if (_running is not null) return null;
            _running = CancellationTokenSource.CreateLinkedTokenSource(outer);
            _givenUp = false;
            return (++_last, _running.Token);
        }
    }

    /// Gives the running attempt up: its exchange stops and its result will be dropped.
    public void GiveUp()
    {
        lock (_lock)
        {
            _givenUp = true;
            _running?.Cancel();
        }
    }

    /// True while the attempt runs and was not given up (what it shows may still be shown).
    public bool Counts(long id)
    {
        lock (_lock) return id == _last && _running is not null && !_givenUp;
    }

    /// The attempt ended; true when its result counts (it is the last one and was not given up).
    public bool End(long id)
    {
        lock (_lock)
        {
            if (id != _last || _running is null) return false;
            _running.Dispose();
            _running = null;
            return !_givenUp;
        }
    }
}
