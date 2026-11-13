using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// Full control for one program: its output is sent to its own cable (per-program routing, journaled), the cable is recorded
/// (loopback of its playback side), the chain runs and the result plays on the program's outputs (several at once, the master bus,
/// or Windows' default). The program's session volume still applies before the cable, so the row's sliders work as usual. Stopping
/// always routes the program back first.
/// </summary>
internal sealed class CableRoute : EngineRun
{
    private bool _routed;

    public CableRoute(RunSpec spec, string name, MixerService mixer, EngineJournal journal)
        : base(spec, name, mixer, journal, new ChainProcessor(level: null, gateOpen: true)) { }

    public override void Start()
    {
        if (Mixer.Find(Key) is not { } entry || Spec.Cable is not { } cable) return;
        Journal.Note(Key, entry, Mixer.DirectRouteOf(Key), silenced: false, routed: true);
        _routed = true;
        if (!Mixer.HoldRouting(Key, cable, out var error))
        {
            Fail($"Windows refused to send the program to the cable ({error})");
            return;
        }
        ShowWaiting("Effects starting");
        StartPipe(new[] { PipeSource.Playback(cable) }, ShowRunning);
    }

    protected override string RunningText => Spec.Outputs.Count > 1 ? $"Playing on {Spec.Outputs.Count} devices" : "Effects running";

    protected override void Undo()
    {
        if (!_routed) return;
        Mixer.ReleaseRouting(Key);
        Journal.Forget(Key);
        _routed = false;
    }
}
