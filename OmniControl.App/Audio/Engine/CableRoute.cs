using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// Full control for one program (<see cref="RunKind.Cable"/>, and <see cref="RunKind.ApoCable"/> with no chain of its own): its output is
/// sent to its own cable (per-program routing, journaled), the cable is recorded (loopback of its playback side; its recording side
/// when Equalizer APO is on it), the chain runs and the result plays on the program's outputs (several at once, the master bus,
/// or Windows' default). The program's session volume still applies before the cable, so the row's sliders work as usual. Stopping
/// always routes the program back first.
/// </summary>
internal sealed class CableRoute : EngineRun
{
    private bool _routed;
    /// <summary>When the program was first heard on a device other than its cable (null = it plays only into the cable, or not at all).</summary>
    private DateTime? _outsideSince;

    public CableRoute(RunSpec spec, string name, MixerService mixer, EngineJournal journal)
        : base(spec, name, mixer, journal, new ChainProcessor(level: null, gateOpen: true)) { }

    public override void Start()
    {
        if (Mixer.Find(Key) is not { } entry || Spec.Cable is not { } cable) return;
        Journal.Note(Key, entry, Mixer.DirectRouteOf(Key), silenced: false, routed: true);
        _routed = true;
        if (!Mixer.HoldRouting(Key, cable, out var error))
        {
            Fail("Windows would not move it to its cable", error, StatusKind.CouldNotMove);
            return;
        }
        StartRecording();
    }

    /// <summary>
    /// Records the cable and plays it on. A cable with Equalizer APO on it is recorded on its recording side, where Equalizer APO's
    /// processed output arrives (a loopback of the playback side would not be sure to include it); the run itself then has no chain.
    /// </summary>
    private void StartRecording()
    {
        ShowWaiting("Effects starting", StatusKind.Starting);
        var source = Spec.Kind == RunKind.ApoCable ? PipeSource.Recording(Spec.Sources[0]) : PipeSource.Playback(Spec.Cable!);
        StartPipe(new[] { source }, ShowRunning);
    }

    protected override string RunningText =>
        Spec.Kind == RunKind.ApoCable ? SoundModes.RunningInApoOnCable
        : Spec.Outputs.Count > 1 ? $"Playing on {Spec.Outputs.Count} devices through a cable" : SoundModes.RunningThroughCable;

    /// <summary>
    /// Per-program routing moves a stream only when the program lets it (many reopen their stream only on their next play): while the
    /// program is still heard on another device for <see cref="Timing.CableMoveLimit"/> its sound bypasses the cable and the effects,
    /// so the row says so instead of "Effects running", and says "running" again once only the cable carries it.
    /// </summary>
    public override void Tick()
    {
        if (Pipe is null || IsStopped) return;
        if (Mixer.PeakOutside(Key, Spec.Cable!) < ProgramEngine.Audible)
        {
            _outsideSince = null;
            ShowRunning();
            return;
        }
        _outsideSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _outsideSince < Timing.CableMoveLimit) base.Tick();
        else if (Status.Text != NotMoved) ShowWaiting(NotMoved, StatusKind.NotMoved);
    }

    private const string NotMoved = "Not on its cable yet, so it plays without effects: pause and play it, or restart it";

    protected override void Undo()
    {
        if (!_routed) return;
        if (Mixer.ReleaseRouting(Key, Spec.Cable!)) Journal.Forget(Key);
        _routed = false;
    }
}
