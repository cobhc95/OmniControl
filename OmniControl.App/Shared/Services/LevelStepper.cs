namespace OmniControl.App.Shared.Services;

/// Steps levels (display brightness and contrast, master volume, Windows' pointer settings) quickly. A DDC/CI read takes 40 ms or more and a write about as long, so reading
/// before every step made a wheel spin queue up for seconds before the pop-up caught up. Instead the level OmniControl last set or
/// read is used straight away, and only the newest wanted level is written: steps that arrive while a write is under way just
/// change what is written next. A level older than <see cref="Timing.DisplayLevelTrust"/> may have been changed on the display
/// itself, so it is read again on the writer thread before the write and the step is corrected if it was.
internal sealed class LevelStepper
{
    /// A check of a level that was too old to trust: what the step started from, how to read the display, who to tell, the level's range.
    private sealed record Check(int Base, Func<int?> Read, Action<int>? Corrected, int Min, int Max);

    private sealed record Pending(int Level, Action<int> Write, Check? Check);

    private readonly object _gate = new();
    private readonly object _display = new();   // one display call at a time: monitors do not answer two DDC/CI requests at once
    private readonly Dictionary<string, (int Level, DateTime At)> _known = new();
    private readonly Dictionary<string, Pending> _pending = new();
    private bool _writing;

    /// Remembers a level set elsewhere (the page's sliders), so the next step starts from it.
    public void Remember(string key, int level)
    {
        lock (_gate) _known[key] = (level, DateTime.UtcNow);
    }

    /// Sets a level the user chose (a slider): only the newest wanted level is written, later, on a worker thread, so dragging a slider
    /// never waits for the display. Remembers it as the level the next step starts from.
    public void Set(string key, int level, Action<int> write)
    {
        lock (_gate)
        {
            _known[key] = (level, DateTime.UtcNow);
            _pending[key] = new Pending(level, write, null);
            if (_writing) return;
            _writing = true;
        }
        Task.Run(WriteAll);
    }

    /// The level after a step of <paramref name="delta"/>, or null when the display does not report one. <paramref name="read"/> is
    /// only called here when no level is known at all; <paramref name="write"/> runs later on a worker thread, and
    /// <paramref name="corrected"/> (when given) is told the level if a re-read shows the display had been changed elsewhere. The level
    /// stays within <paramref name="min"/>-<paramref name="max"/> (0-100 unless given). Call off the UI thread when a read is slow.
    public int? Step(string key, int delta, Func<int?> read, Action<int> write, Action<int>? corrected, int min = Percent.Min, int max = Percent.Max)
    {
        int level;
        lock (_gate)
        {
            Check? check = null;
            if (!_known.TryGetValue(key, out var known))
            {
                int? current;
                lock (_display) current = read();
                if (current is null) return null;
                known = (current.Value, DateTime.UtcNow);
            }
            else if (!Grace.Within(known.At, Timing.DisplayLevelTrust)) check = new Check(known.Level, read, corrected, min, max);

            // A check still waiting from an earlier step of this spin covers this step too.
            if (_pending.TryGetValue(key, out var waiting) && waiting.Check is not null) check = waiting.Check;

            level = Math.Clamp(known.Level + delta, min, max);
            _known[key] = (level, DateTime.UtcNow);
            _pending[key] = new Pending(level, write, check);
            if (_writing) return level;
            _writing = true;
        }
        Task.Run(WriteAll);
        return level;
    }

    /// Writes the newest wanted level of every display until nothing new is waiting.
    private void WriteAll()
    {
        var finished = false;
        try
        {
            while (true)
            {
                KeyValuePair<string, Pending>[] batch;
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        // Inside the lock, so a step arriving after this starts a new writer.
                        _writing = false;
                        finished = true;
                        return;
                    }
                    batch = _pending.ToArray();
                    _pending.Clear();
                }
                foreach (var (key, pending) in batch)
                {
                    var level = pending.Check is { } check ? Verify(key, pending.Level, check) : pending.Level;
                    lock (_display) pending.Write(level);
                }
            }
        }
        finally
        {
            // A write that threw: let the next step start a new writer instead of waiting for this one forever.
            if (!finished) lock (_gate) _writing = false;
        }
    }

    /// Reads the display whose remembered level was too old. When it was changed elsewhere, moves the wanted level (and any step
    /// queued meanwhile) by the same amount and reports it; otherwise <paramref name="level"/> stands.
    private int Verify(string key, int level, Check check)
    {
        int? actual;
        lock (_display) actual = check.Read();
        if (actual is not { } read || read == check.Base) return level;

        var shift = read - check.Base;
        int corrected;
        lock (_gate)
        {
            corrected = Math.Clamp(level + shift, check.Min, check.Max);
            // Steps that arrived during the read were counted from the old level too.
            var latest = corrected;
            if (_pending.TryGetValue(key, out var queued))
            {
                latest = Math.Clamp(queued.Level + shift, check.Min, check.Max);
                _pending[key] = queued with { Level = latest };
            }
            _known[key] = (latest, DateTime.UtcNow);
        }
        check.Corrected?.Invoke(corrected);
        return corrected;
    }
}
