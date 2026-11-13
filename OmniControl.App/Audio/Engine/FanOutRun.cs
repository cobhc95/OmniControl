using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// Combined speakers (Full control): whatever is sent to the device's cable (by OmniControl or by any app that picked it) is recorded
/// and played on every member device. No effects: nothing of the user's is changed, so there is nothing to undo.
/// </summary>
internal sealed class FanOutRun : EngineRun
{
    public FanOutRun(RunSpec spec, string name, MixerService mixer, EngineJournal journal)
        : base(spec, name, mixer, journal, new ChainProcessor(level: null, gateOpen: true)) { }

    public override void Start()
    {
        ShowWaiting("Starting");
        StartPipe(new[] { PipeSource.Playback(Spec.Cable!) }, ShowRunning);
    }

    protected override string RunningText => $"Playing on {Spec.Outputs.Count} devices";

    protected override void Undo() { }
}
