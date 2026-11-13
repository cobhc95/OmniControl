using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// The master bus (Full control): every program that follows the master is sent to one cable (per-program routing, each journaled
/// before it moves), the cable is recorded, the master chain runs and the result plays on the master's devices (one or several).
/// Windows' default device is not changed, so its own system sounds still play there directly. Stopping routes every program back
/// first.
/// </summary>
internal sealed class BusRun : EngineRun
{
    private readonly HashSet<string> _routed = new();

    public BusRun(RunSpec spec, MixerService mixer, EngineJournal journal)
        : base(spec, "Master", mixer, journal, new ChainProcessor(level: null, gateOpen: true)) { }

    public override void Start()
    {
        ShowWaiting("Effects starting");
        StartPipe(new[] { PipeSource.Playback(Spec.Cable!) }, ShowRunning);
    }

    protected override string RunningText => Spec.Outputs.Count > 1 ? $"Playing on {Spec.Outputs.Count} devices" : "Effects running";

    /// <summary>Sends newly listed programs to the bus's cable and puts back those no longer listed.</summary>
    public override void SetRouted(IReadOnlyCollection<string> keys)
    {
        if (IsStopped) return;
        foreach (var key in _routed.Where(k => !keys.Contains(k)).ToList()) Release(key);
        foreach (var key in keys.Where(k => !_routed.Contains(k)))
        {
            if (Mixer.Find(key) is not { } entry) continue;
            Journal.Note(key, entry, Mixer.DirectRouteOf(key), silenced: false, routed: true);
            _routed.Add(key);
            if (!Mixer.HoldRouting(key, Spec.Cable!, out var error)) Mixer.Report?.Invoke($"Couldn't send {entry.Name} to the master's cable: {error}");
        }
    }

    private void Release(string key)
    {
        Mixer.ReleaseRouting(key);
        Journal.Forget(key);
        _routed.Remove(key);
    }

    protected override void Undo()
    {
        foreach (var key in _routed.ToList()) Release(key);
    }
}
