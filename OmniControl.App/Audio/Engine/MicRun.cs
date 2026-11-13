using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// A processed or combined microphone (Full control): its recording devices are recorded and mixed, the chain runs (and, for a
/// combined microphone, its own level and mute) and the result plays into its cable, whose recording side apps use as their
/// microphone. The microphones themselves are not changed, so there is nothing to undo.
/// </summary>
internal sealed class MicRun : EngineRun
{
    private readonly UserLevel? _level;

    /// <param name="level">A combined microphone's level and mute; null for one microphone (its own Windows level already applies).</param>
    public MicRun(RunSpec spec, string name, MixerService mixer, EngineJournal journal, UserLevel? level, string cableName)
        : base(spec, name, mixer, journal, new ChainProcessor(level, gateOpen: true))
    {
        _level = level;
        RunningText = $"Apps hear it on {cableName}";
    }

    public override void Start()
    {
        ApplyLevel();
        ShowWaiting("Starting");
        StartPipe(Spec.Sources.Select(PipeSource.Recording).ToList(), ShowRunning);
    }

    protected override string RunningText { get; }

    public override void OnProgramChanged() => ApplyLevel();

    private void ApplyLevel()
    {
        if (Mixer.CombinedOf(Key) is { } combo) _level?.Set(combo.Volume, 0, combo.Muted);
    }

    protected override void Undo() { }
}
