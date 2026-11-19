using OmniControl.App.Features.Volume.Audio.Engine.Dsp;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// A processed or a combined microphone: its recording devices are recorded and mixed (a
/// combined microphone's members each at their own level and mute), the chain runs (and, for a combined microphone, its own level and
/// mute) and the result plays into its cable, whose recording side apps use as their microphone. The microphones themselves are not
/// changed, so there is nothing to undo.
internal sealed class MicRun : EngineRun
{
    private readonly UserLevel? _level;

    /// <param name="level">A combined microphone's level and mute; null for one microphone (its own Windows level already applies).</param>
    public MicRun(RunSpec spec, string name, MixerService mixer, EngineJournal journal, UserLevel? level, string cableName)
        : base(spec, name, mixer, journal, new ChainProcessor(level, gateOpen: true))
    {
        _level = level;
        RunningText = EffectsRules.AppsRecordFrom(cableName);
    }

    public override void Start()
    {
        ApplyLevel();
        ShowWaiting("Starting", StatusKind.Starting);
        StartPipe(Spec.Sources.Select(PipeSource.Recording).ToList(), () =>
        {
            ApplyLevel(); // the members' gains need the pipe
            ShowRunning();
        });
    }

    protected override string RunningText { get; }

    public override void OnProgramChanged() => ApplyLevel();

    private void ApplyLevel()
    {
        if (Mixer.CombinedOf(Key) is not { } combo) return;
        _level?.Set(combo.Volume, 0, combo.Muted);
        for (var i = 0; i < Spec.Sources.Count; i++) Pipe?.SetSourceGain(i, combo.GainOf(Spec.Sources[i]));
    }

    protected override void Undo() { }
}
