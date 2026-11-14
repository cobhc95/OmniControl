using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// The master bus (Full control): the cable is recorded, the master chain runs and the result plays on the master's devices (one or
/// several). The cable is Windows' default playback device (<see cref="BusDefault"/>), so system sounds and every program that
/// follows the default arrive there; programs that follow the master are also sent to the cable by per-program routing (each
/// journaled before it moves), which holds them there when they picked their device themselves. Stopping routes every program back
/// first.
/// </summary>
internal sealed class BusRun : EngineRun
{
    private readonly HashSet<string> _routed = new();
    /// <summary>When sound was first seen arriving at the cable while the bus recorded nothing from it (null = the bus hears what the cable plays).</summary>
    private DateTime? _deafSince;

    public BusRun(RunSpec spec, MixerService mixer, EngineJournal journal)
        : base(spec, "Master", mixer, journal, new ChainProcessor(level: null, gateOpen: true)) { }

    public override void Start()
    {
        ShowWaiting("Effects starting", StatusKind.Starting);
        StartPipe(new[] { PipeSource.Playback(Spec.Cable!) }, ShowRunning);
    }

    protected override string RunningText =>
        Spec.Outputs.Count > 1 ? $"Playing on {Spec.Outputs.Count} devices" : HasEffects ? "Effects running" : "Playing through the master bus";

    /// <summary>
    /// Watchdog: sound reaches the cable (its own meter) but the bus's recording of it stays silent for <see cref="Services.Timing.BusDeafLimit"/>:
    /// the bus passes nothing on while Windows' default is its cable, so the run fails and the default goes back to the real device. A
    /// silent cable proves nothing (nobody is playing), so it never trips.
    /// </summary>
    public override void Tick()
    {
        base.Tick();
        if (Pipe is null || IsStopped || !Status.IsRunning) return;
        var heard = Pipe.TakeCapturePeak() > 0;
        if (heard || !Mixer.DevicePeak(Spec.Cable!, out var sent) || sent < ProgramEngine.Audible)
        {
            _deafSince = null;
            return;
        }
        _deafSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _deafSince < Services.Timing.BusDeafLimit) return;
        var real = Spec.Outputs.FirstOrDefault() is { } id ? Mixer.DeviceNameOf(id) : "the real speakers";
        Fail($"the master bus got no sound, so the default went back to {real}", kind: StatusKind.WentSilent);
    }

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
            if (!Mixer.HoldRouting(key, Spec.Cable!, out var error)) Mixer.Report?.Invoke($"Couldn't send {entry.Name} to the master's cable: {error}.");
        }
    }

    private void Release(string key)
    {
        // A program another run holds now (it got effects of its own) keeps that run's cable and journal entry.
        if (Mixer.ReleaseRouting(key, Spec.Cable!)) Journal.Forget(key);
        _routed.Remove(key);
    }

    protected override void Undo()
    {
        foreach (var key in _routed.ToList()) Release(key);
    }
}
