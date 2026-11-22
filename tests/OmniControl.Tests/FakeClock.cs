namespace OmniControl.Tests;

/// A clock whose time moves only when told; its timers fire as it passes their time.
internal sealed class FakeClock : TimeProvider
{
    private readonly List<FakeTimer> _timers = new();
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, () => callback(state));
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;
        foreach (var timer in _timers.ToList()) timer.FireIfDue(_now);
    }

    private sealed class FakeTimer(FakeClock clock, Action fire) : ITimer
    {
        private DateTimeOffset? _due;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
            return true;
        }

        public void FireIfDue(DateTimeOffset now)
        {
            if (_due is not { } due || due > now) return;
            _due = null;
            fire();
        }

        public void Dispose() => _due = null;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
