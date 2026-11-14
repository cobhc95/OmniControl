namespace OmniControl.App.Audio.Engine;

/// <summary>
/// The make-or-break question of a recorded program (<see cref="ProgramEngine"/>), answered from what the engine reads every tick, with
/// no Windows calls of its own (so it can be tested with a made-up clock): the original is already silenced and the replay open before
/// the program plays, so when Windows' meter shows the program playing, the recording must deliver sound that passes
/// (<see cref="QuietLevel.KeepsSound"/>) within the probe time, or silencing is taken to have silenced the recording too. Once proved, it
/// tells whether sound passes now ("running") or has not for a while ("ready").
/// </summary>
internal sealed class RecordingCheck
{
    public enum Verdict
    {
        /// <summary>Armed: the original is silenced and the replay waits; nothing is playing.</summary>
        Ready,
        /// <summary>The program plays and the recording has not delivered passing sound yet.</summary>
        Checking,
        /// <summary>Sound passes through the effects (or did within the idle time).</summary>
        Running,
        /// <summary>The program played for the probe time and the recording never delivered passing sound.</summary>
        Failed,
    }

    private readonly SilenceMethod _method;
    private readonly TimeSpan _probe;
    private readonly TimeSpan _idleAfter;
    private bool _proved;
    private bool _sawLoud;
    private DateTime? _checkSince;
    private DateTime _lastSound;

    public RecordingCheck(SilenceMethod method, TimeSpan probe, TimeSpan idleAfter)
    {
        _method = method;
        _probe = probe;
        _idleAfter = idleAfter;
    }

    /// <summary>The recording has delivered passing sound at least once since the run started.</summary>
    public bool Proved => _proved;

    /// <param name="now">The time of this look.</param>
    /// <param name="rawPeak">The recording's loudest raw sample since the last look (before the make-up gain).</param>
    /// <param name="programPeak">Windows' meter of the program (read before its session volume, so it shows the program playing even while silenced).</param>
    public Verdict Look(DateTime now, float rawPeak, float programPeak)
    {
        var madeUp = rawPeak * QuietLevel.MakeupFor(_method);
        if (QuietLevel.KeepsSound(_method, rawPeak) && madeUp >= ProgramEngine.Audible)
        {
            _proved = true;
            _checkSince = null;
            _lastSound = now;
            return Verdict.Running;
        }
        if (_proved) return now - _lastSound < _idleAfter ? Verdict.Running : Verdict.Ready;
        if (rawPeak > QuietLevel.CeilingFor(_method)) _sawLoud = true;
        if (programPeak >= ProgramEngine.Audible || madeUp >= ProgramEngine.Audible) _checkSince ??= now;
        if (_checkSince is not { } since) return Verdict.Ready;
        return now - since >= _probe ? Verdict.Failed : Verdict.Checking;
    }

    /// <summary>Why the check failed, in the row's words.</summary>
    public string FailReason => _sawLoud
        ? "the recording did not follow the program's volume (press Test to try the other ways)"
        : "the recording went silent when the original was silenced (press Test to try the other ways)";
}
