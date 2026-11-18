namespace OmniControl.Core.Engine;

/// <summary>
/// The fallback wake check: a timer samples two monotonic clocks every few seconds. <c>Environment.TickCount64</c> keeps counting while the PC
/// sleeps and ignores clock changes (unlike the wall clock, which a time sync can move); the unbiased interrupt time stops while the PC
/// sleeps. A sample that comes much later than asked means the program was stopped: asleep (the two clocks disagree) or frozen by Modern
/// Standby (they agree). Pure: the caller passes the readings, so tests need no sleep. Not thread-safe (one timer calls it).
/// </summary>
public sealed class ClockGap
{
    private readonly long _intervalMs, _thresholdMs;
    private long? _lastTickMs, _lastUnbiasedMs;

    /// <param name="intervalMs">How often the timer samples.</param>
    /// <param name="thresholdMs">How much later than <paramref name="intervalMs"/> a sample must come to count as a gap.</param>
    public ClockGap(long intervalMs, long thresholdMs)
    {
        _intervalMs = intervalMs;
        _thresholdMs = thresholdMs;
    }

    /// <summary>One sample; a <see cref="PowerSignal"/> of kind <see cref="PowerEventKind.ClockGap"/> when the program was stopped since the last one, else null.</summary>
    /// <param name="tickMs">A clock that counts sleep (<c>Environment.TickCount64</c>).</param>
    /// <param name="unbiasedMs">A clock that stops in sleep (the unbiased interrupt time); null when Windows could not give it.</param>
    public PowerSignal? Sample(long tickMs, long? unbiasedMs)
    {
        var lastTick = _lastTickMs;
        var lastUnbiased = _lastUnbiasedMs;
        _lastTickMs = tickMs;
        _lastUnbiasedMs = unbiasedMs;
        if (lastTick is not { } before) return null;
        var late = tickMs - before - _intervalMs;
        if (late < _thresholdMs) return null;
        var slept = unbiasedMs is { } u && lastUnbiased is { } ub ? tickMs - before - (u - ub) : 0;
        var detail = slept >= _thresholdMs
            ? $"the PC slept {Words(slept)} (sleep clock)"
            : $"the program was stopped {Words(late)} while the PC kept its clock (Modern Standby or a frozen process)";
        return new PowerSignal(PowerEventKind.ClockGap, PowerChannel.Clock, detail);
    }

    private static string Words(long ms) => ms >= 60_000 ? $"{ms / 60_000.0:0.#} min" : $"{ms / 1000.0:0} s";
}
