using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>What "Test effects on this program" found, as stored in <see cref="MixerSettings.CaptureSilence"/>.</summary>
public static class CaptureTestResult
{
    public const string Untested = "", Mute = "mute", ZeroVolume = "volume", Failed = "failed";

    /// <summary>The way to silence originals; null when the test found that neither keeps the capture alive.</summary>
    public static SilenceMethod? MethodFor(string stored) => stored switch
    {
        Failed => null,
        ZeroVolume => SilenceMethod.ZeroVolume,
        _ => SilenceMethod.Mute,
    };

    public static string From(SilenceMethod? method) => method switch
    {
        SilenceMethod.Mute => Mute,
        SilenceMethod.ZeroVolume => ZeroVolume,
        _ => Failed,
    };
}

/// <summary>
/// Capture and replay for one program (Fast mode): records the program's process tree (process loopback), runs the chain and plays
/// the result on the program's outputs (one or several devices); the original is silenced through its sessions. The make-or-break question (does silencing the
/// session also silence the capture?) is answered here every time: playback stays shut until the program is heard, then the
/// original is silenced and the capture must keep delivering sound within <see cref="Timing.CaptureSilenceProbe"/>; if it does
/// not, the original is put back at once and the row says so. The journal is written before the original is touched.
/// </summary>
internal sealed class ProgramEngine : EngineRun
{
    /// <summary>A raw peak above this (-60 dBFS) counts as the program being heard.</summary>
    internal const float Audible = 0.001f;

    private enum Phase { Waiting, Listening, Checking, Running }

    private readonly SilenceMethod _method;
    private readonly UserLevel _level;
    private Phase _phase = Phase.Waiting;
    private DateTime _checkStart;
    private bool _heardAfterSilence;
    private bool _silenced;
    private IReadOnlyList<int> _roots = Array.Empty<int>();

    public ProgramEngine(RunSpec spec, string name, MixerService mixer, EngineJournal journal, SilenceMethod method)
        : this(spec, name, mixer, journal, method, new UserLevel()) { }

    private ProgramEngine(RunSpec spec, string name, MixerService mixer, EngineJournal journal, SilenceMethod method, UserLevel level)
        : base(spec, name, mixer, journal, new ChainProcessor(level, gateOpen: false))
    {
        _method = method;
        _level = level;
    }

    public override void Start()
    {
        if (Mixer.Find(Key) is { } entry) _level.Set(entry.Volume, entry.Pan, entry.Muted);
        StartCapture();
    }

    private void StartCapture()
    {
        var streams = Mixer.OpenStreams(Key);
        if (streams.Count == 0)
        {
            ShowWaiting("Effects start when the program plays");
            return;
        }
        _roots = ProcessTree.Roots(streams.Select(s => s.ProcessId));
        ShowWaiting("Effects waiting for sound from the program");
        _phase = Phase.Listening;
        StartPipe(_roots.Select(PipeSource.Program).ToList(), () => { });
    }

    protected override string RunningText => Spec.Outputs.Count > 1 ? $"Playing on {Spec.Outputs.Count} devices" : "Effects running";

    public override void Tick()
    {
        if (Pipe is null || IsStopped) return;
        var peak = Pipe.TakeCapturePeak();
        switch (_phase)
        {
            case Phase.Listening when peak >= Audible:
                SilenceOriginal();
                break;
            case Phase.Checking:
                var elapsed = DateTime.UtcNow - _checkStart;
                if (elapsed >= Timing.CaptureSilenceSettle && peak > 0) _heardAfterSilence = true;
                if (elapsed < Timing.CaptureSilenceProbe) break;
                if (!_heardAfterSilence)
                {
                    Fail("the recording went silent when the original was muted (press Test to try the other way, or use Full control)");
                    return;
                }
                _phase = Phase.Running;
                ShowRunning();
                break;
            case Phase.Running:
                base.Tick();
                break;
        }
    }

    /// <summary>The program is heard: journal it, silence the original, open the processed playback, start the check.</summary>
    private void SilenceOriginal()
    {
        if (Mixer.Find(Key) is not { } entry) return;
        Journal.Note(Key, entry, Mixer.DirectRouteOf(Key), silenced: true, routed: false);
        Mixer.Silence(Key, _method);
        _silenced = true;
        Processor.GateOpen = true;
        _checkStart = DateTime.UtcNow;
        _heardAfterSilence = false;
        _phase = Phase.Checking;
        ShowWaiting("Effects starting (checking the recording)");
    }

    public override void OnProgramChanged()
    {
        if (Mixer.Find(Key) is { } entry) _level.Set(entry.Volume, entry.Pan, entry.Muted);
    }

    /// <summary>A new process of the program (or one closed): record the new set; the original stays silenced meanwhile.</summary>
    public override void OnStreamsChanged()
    {
        if (IsStopped) return;
        var streams = Mixer.OpenStreams(Key);
        var roots = ProcessTree.Roots(streams.Select(s => s.ProcessId));
        if (roots.Count > 0 && roots.ToHashSet().SetEquals(_roots)) return;
        StopPipe();
        _roots = roots;
        if (streams.Count == 0) return;
        StartPipe(roots.Select(PipeSource.Program).ToList(), () => { });
    }

    protected override void Undo()
    {
        if (!_silenced) return;
        Mixer.Unsilence(Key);
        Journal.Forget(Key);
        _silenced = false;
    }
}
