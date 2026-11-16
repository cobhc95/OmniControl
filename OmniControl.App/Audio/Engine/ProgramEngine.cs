using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>What "Test effects on this program" found, as stored in <see cref="MixerSettings.CaptureSilence"/>.</summary>
public static class CaptureTestResult
{
    public const string Untested = "", Mute = "mute", ZeroVolume = "volume", Quiet = "quiet", Failed = "none";

    /// <summary>
    /// What a Test stored before <see cref="SilenceMethod.Quiet"/> existed when muting and volume 0 both failed; it says nothing about
    /// the quiet way, so it counts as untested.
    /// </summary>
    private const string FailedBeforeQuiet = "failed";

    /// <summary>
    /// The way to silence originals; null when the test found that no way keeps the capture alive. Untested PCs use
    /// <see cref="SilenceMethod.Quiet"/>: on the owner's PC the recording follows the session's volume (muting silenced it), and the
    /// engine checks it every time and falls back with a reason.
    /// </summary>
    public static SilenceMethod? MethodFor(string stored) => stored switch
    {
        Failed => null,
        Mute => SilenceMethod.Mute,
        ZeroVolume => SilenceMethod.ZeroVolume,
        _ => SilenceMethod.Quiet, // Quiet, Untested, FailedBeforeQuiet
    };

    /// <summary>Whether a stored result came from a Test (an old result that predates the quiet way does not count).</summary>
    public static bool IsTested(string stored) => stored is not (Untested or FailedBeforeQuiet);

    public static string From(SilenceMethod? method) => method switch
    {
        SilenceMethod.Mute => Mute,
        SilenceMethod.ZeroVolume => ZeroVolume,
        SilenceMethod.Quiet => Quiet,
        _ => Failed,
    };
}


/// <summary>
/// Capture and replay for one program (when the program records instead of using a cable): records the
/// program's process tree (process loopback), runs the chain and plays the result on the program's outputs (one or several devices,
/// usually the master bus's cable); the original is silenced through its sessions (<see cref="SilenceMethod"/>; with
/// <see cref="SilenceMethod.Quiet"/> the recording gets <see cref="QuietLevel.Makeup"/> and blocks above <see cref="QuietLevel.RawCeiling"/>
/// are left out).
/// <para>
/// Armed ahead of the sound: as soon as the program has a session (open, even paused or silent) its original is silenced, the recording
/// runs and the replay is open, so nothing is left to start when it plays and its effects apply from its first sound (measured: no dry
/// sound at all, the processed copy about 75-85 ms after the original would have been heard). Windows keeps the session's volume when
/// the program starts or restarts its stream. The make-or-break question (does silencing the session also silence the recording?) is
/// answered by <see cref="RecordingCheck"/> the first time the program plays; if it fails, the original is put back at once and the row
/// says so. The journal is written before the original is touched.
/// </para>
/// </summary>
internal sealed class ProgramEngine : EngineRun
{
    /// <summary>A raw peak above this (-60 dBFS) counts as the program being heard.</summary>
    internal const float Audible = 0.001f;

    private const string StartsWhenOpen = "Effects start when the program plays";

    private readonly SilenceMethod _method;
    private readonly UserLevel _level;
    private readonly RecordingCheck _check;
    private bool _silenced;
    private IReadOnlyList<int> _roots = Array.Empty<int>();

    public ProgramEngine(RunSpec spec, string name, MixerService mixer, EngineJournal journal, SilenceMethod method)
        : this(spec, name, mixer, journal, method, new UserLevel()) { }

    private ProgramEngine(RunSpec spec, string name, MixerService mixer, EngineJournal journal, SilenceMethod method, UserLevel level)
        : base(spec, name, mixer, journal, new ChainProcessor(level, gateOpen: false))
    {
        _method = method;
        _level = level;
        _check = new RecordingCheck(method, Timing.CaptureSilenceProbe, Timing.EngineIdleAfter);
    }

    public override void Start()
    {
        if (Mixer.Find(Key) is { } entry) _level.Set(entry.Volume, entry.Pan, entry.Muted);
        Attach();
    }

    /// <summary>
    /// Records the program's processes as they are now: the original is silenced first (before it can sound), then the pipe starts and
    /// the replay opens with the make-up already set. With no session there is nothing to record yet; it attaches when one opens.
    /// </summary>
    private void Attach()
    {
        _roots = ProcessTree.Roots(Mixer.OpenStreams(Key).Select(s => s.ProcessId));
        if (_roots.Count == 0 || !SilenceOriginal())
        {
            ShowWaiting(StartsWhenOpen, StatusKind.StartsWhenPlays);
            return;
        }
        if (!_check.Proved) ShowWaiting(EffectsRules.EffectsReady, StatusKind.Ready);
        // A new pipe starts at gain 1 with no ceiling: the replay stays shut until the make-up and ceiling are set.
        Processor.GateOpen = false;
        StartPipe(_roots.Select(PipeSource.Program).ToList(), OpenReplay);
    }

    /// <summary>The pipe is up: every source gets the method's make-up gain and ceiling, then the replay opens.</summary>
    private void OpenReplay()
    {
        if (Pipe is not { } pipe) return;
        for (var i = 0; i < _roots.Count; i++) pipe.SetSourceGain(i, QuietLevel.MakeupFor(_method), QuietLevel.CeilingFor(_method));
        Processor.GateOpen = true;
    }

    /// <summary>Journals the program and silences its original (once per run; later sessions are held silent by the mixer). False when it has no entry.</summary>
    private bool SilenceOriginal()
    {
        if (_silenced) return true;
        if (Mixer.Find(Key) is not { } entry) return false;
        Journal.Note(Key, entry, Mixer.DirectRouteOf(Key), silenced: true, routed: false);
        Mixer.Silence(Key, _method);
        _silenced = true;
        return true;
    }

    protected override string RunningText => Spec.Outputs.Count > 1 ? $"Playing on {Spec.Outputs.Count} devices by recording" : EffectsRules.RunningByRecording;

    public override void Tick()
    {
        if (Pipe is null || IsStopped) return;
        var program = Mixer.Peaks(Key, out var left, out var right) ? Math.Max(left, right) : 0f;
        switch (_check.Look(DateTime.UtcNow, Pipe.TakeCapturePeak(), program))
        {
            case RecordingCheck.Verdict.Failed:
                Fail(_check.FailReason, kind: StatusKind.WentSilent);
                break;
            case RecordingCheck.Verdict.Running:
                ShowRunning();
                break;
            case RecordingCheck.Verdict.Checking:
                ShowWaiting("Effects starting (checking the recording)", StatusKind.Checking);
                break;
            default:
                ShowWaiting(EffectsRules.EffectsReady, StatusKind.Ready);
                break;
        }
    }

    public override void OnProgramChanged()
    {
        if (Mixer.Find(Key) is { } entry) _level.Set(entry.Volume, entry.Pan, entry.Muted);
    }

    /// <summary>
    /// A process of the program opened or closed: record the new set (the original stays silenced meanwhile). A program that closed keeps
    /// its run and its silence, so it is armed again the moment it reopens (Windows also starts it at the volume it closed with).
    /// </summary>
    public override void OnStreamsChanged()
    {
        if (IsStopped) return;
        var roots = ProcessTree.Roots(Mixer.OpenStreams(Key).Select(s => s.ProcessId));
        if (roots.Count > 0 && roots.ToHashSet().SetEquals(_roots)) return;
        StopPipe();
        Attach();
    }

    protected override void Undo()
    {
        if (!_silenced) return;
        // A block recorded at full level from here on is above the ceiling: never made up. A program that closed while silenced stays
        // journalled (Windows keeps its silenced volume) until its next session gets its own values back (MixerService.Scan) or the
        // next start puts it back.
        if (Mixer.Unsilence(Key)) Journal.Forget(Key);
        _silenced = false;
    }
}
