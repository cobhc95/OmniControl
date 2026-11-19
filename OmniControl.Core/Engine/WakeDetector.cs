namespace OmniControl.Core.Engine;

/// What the detector decided about one piece of wake evidence, or about a candidate whose time ran out (each is one log line).
public enum WakeVerdict
{
    /// Evidence noted; waiting for a second channel.
    Pending,

    /// A real wake: the "wakes from sleep" trigger fires once.
    Confirmed,

    /// Only one channel told it and it is not a sleep-and-resume pair: no trigger (a screen time-out, a clock blip).
    Rejected,

    /// Another telling of a wake already confirmed: absorbed.
    Duplicate,
}

/// One decision, with the channels that told it and the sentence for the log.
public sealed record WakeDecision(WakeVerdict Verdict, IReadOnlyList<PowerChannel> Channels, string Why);

/// Decides when the PC really woke, from the power events of every listener (<see cref="HardwareSignals"/>). Evidence of a wake is a resume
/// broadcast (hidden window, <c>SystemEvents</c>), the display coming back after being off long enough (Modern Standby), and a clock gap
/// (<see cref="ClockGap"/>). A wake is confirmed as soon as two different channels tell it; when only one does within the confirm window, it
/// is confirmed only if it is a resume broadcast after a suspend was seen (Windows' own pair), else rejected. After a confirmed wake further
/// evidence is a duplicate until something says the PC went down again (a suspend, or the display going off) or the quiet time passes.
/// Pure and thread-safe: the caller passes a monotonic time in milliseconds, so tests replay sequences without sleeping.
public sealed class WakeDetector
{
    private sealed class Candidate(long startMs, bool afterSuspend)
    {
        public long StartMs { get; } = startMs;
        public bool AfterSuspend { get; } = afterSuspend;
        public List<PowerChannel> Channels { get; } = new();
    }

    private readonly long _confirmMs, _quietMs, _displayOffMinMs;
    private readonly object _gate = new();
    private Candidate? _candidate;
    private bool _suspended;
    private long? _displayOffAt;
    private long? _lastWakeMs;

    /// <param name="confirmMs">How long a candidate waits for a second channel.</param>
    /// <param name="quietMs">After a confirmed wake, how long more evidence is a duplicate when nothing said the PC went down again.</param>
    /// <param name="displayOffMinMs">How long the display must have been off for its return to count as evidence (a short blank is not standby).</param>
    public WakeDetector(long confirmMs, long quietMs, long displayOffMinMs)
    {
        _confirmMs = confirmMs;
        _quietMs = quietMs;
        _displayOffMinMs = displayOffMinMs;
    }

    /// Takes one event; a decision when it was wake evidence or closed a waiting candidate (null for events that only go in the journal).
    public WakeDecision? Observe(PowerSignal signal, long nowMs)
    {
        lock (_gate)
        {
            switch (signal.Kind)
            {
                case PowerEventKind.Suspend or PowerEventKind.Hibernate:
                    // A candidate still waiting is decided now, before the next sleep starts a new cycle.
                    var closed = _candidate is { } waiting ? Decide(waiting) : null;
                    _suspended = true;
                    _lastWakeMs = null;
                    return closed;
                case PowerEventKind.DisplayOff:
                    _displayOffAt ??= nowMs;
                    _lastWakeMs = null;
                    return null;
                case PowerEventKind.DisplayOn:
                    if (_displayOffAt is not { } off) return null;
                    _displayOffAt = null;
                    return nowMs - off >= _displayOffMinMs ? Evidence(signal.Channel, nowMs) : null;
                case PowerEventKind.Resume or PowerEventKind.ClockGap:
                    return Evidence(signal.Channel, nowMs);
                default:
                    return null;
            }
        }
    }

    /// Called by a timer: a candidate whose confirm window passed is decided now; null when there is none or it may still get a second channel.
    public WakeDecision? Expire(long nowMs)
    {
        lock (_gate) return _candidate is { } c && nowMs - c.StartMs >= _confirmMs ? Decide(c) : null;
    }

    private WakeDecision Evidence(PowerChannel channel, long nowMs)
    {
        if (_candidate is null && _lastWakeMs is { } last && nowMs - last < _quietMs)
            return new WakeDecision(WakeVerdict.Duplicate, new[] { channel }, $"{channel} told a wake already confirmed {(nowMs - last) / 1000} s ago");
        var candidate = _candidate ??= new Candidate(nowMs, _suspended);
        if (!candidate.Channels.Contains(channel)) candidate.Channels.Add(channel);
        if (candidate.Channels.Count >= 2) return Confirm(candidate, $"confirmed by {candidate.Channels.Count} channels");
        return new WakeDecision(WakeVerdict.Pending, candidate.Channels.ToList(), $"{channel} told a wake; waiting up to {_confirmMs / 1000} s for a second channel");
    }

    private WakeDecision Decide(Candidate candidate)
    {
        var broadcast = candidate.Channels.Any(c => c is PowerChannel.Window or PowerChannel.SystemEvents);
        if (candidate.AfterSuspend && broadcast) return Confirm(candidate, "one channel, but it is Windows' resume after its suspend");
        _candidate = null;
        return new WakeDecision(WakeVerdict.Rejected, candidate.Channels.ToList(),
            $"only {candidate.Channels[0]} told it{(candidate.AfterSuspend ? string.Empty : " and no suspend was seen")}, so it is not taken as a wake");
    }

    private WakeDecision Confirm(Candidate candidate, string why)
    {
        _candidate = null;
        _suspended = false;
        _displayOffAt = null;
        _lastWakeMs = candidate.StartMs;
        return new WakeDecision(WakeVerdict.Confirmed, candidate.Channels.ToList(), why);
    }
}
