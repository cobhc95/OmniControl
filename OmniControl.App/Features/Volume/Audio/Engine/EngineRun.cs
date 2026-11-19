using OmniControl.App.Features.Volume.Audio.Engine.Dsp;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// One row's engine while it runs (the shared part of every run: <see cref="ProgramEngine"/>, <see cref="CableRoute"/>,
/// <see cref="BusRun"/>, <see cref="FanOutRun"/>, <see cref="MicRun"/>): the spec it was made from, the chain, the audio pipe and the
/// status. Lives on the UI thread; the pipe's own threads only ever post back to it. <see cref="Stop"/> always puts the original
/// sound back first and is safe to call more than once.
internal abstract class EngineRun : IDisposable
{
    private DspChain _chain = DspChain.Empty;
    private readonly StableDelay _delay = new();
    private bool _stopped;

    protected EngineRun(RunSpec spec, string name, MixerService mixer, EngineJournal journal, ChainProcessor processor)
    {
        Spec = spec;
        Key = spec.Key;
        Name = name;
        Mixer = mixer;
        Journal = journal;
        Processor = processor;
    }

    /// What this run was made from; a new plan with the same spec keeps it running.
    public RunSpec Spec { get; }
    public string Key { get; }
    protected string Name { get; }
    protected MixerService Mixer { get; }
    protected EngineJournal Journal { get; }
    protected ChainProcessor Processor { get; }
    protected AudioPipe? Pipe { get; private set; }
    protected bool IsStopped => _stopped;

    public EngineStatus Status { get; private set; } = new(EngineState.Waiting, "Starting effects…", Kind: StatusKind.Starting);

    /// The status changed (UI thread).
    public event Action<EngineRun>? Changed;

    /// The chain to run from now on (edits apply live).
    public void SetChain(DspChain chain)
    {
        Processor.SetChain(chain);
        _chain = chain;
        if (Status.IsRunning) ShowRunning();
    }

    /// Starts the effects (UI thread).
    public abstract void Start();

    /// Called every <see cref="Shared.Services.Timing.EngineTick"/> while the run exists.
    public virtual void Tick()
    {
        if (Status.IsRunning) ShowRunning();
    }

    /// The user changed the row's volume, pan or mute.
    public virtual void OnProgramChanged() { }

    /// The program's streams changed (a process opened or closed).
    public virtual void OnStreamsChanged() { }

    /// The programs the master bus carries changed (only the bus uses it).
    public virtual void SetRouted(IReadOnlyCollection<string> keys) { }

    /// Puts the program's own sound back (silence or routing), then stops the pipe.
    protected abstract void Undo();

    /// Stops for good: undoes everything first, then stops the pipe.
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        Undo();
        StopPipe();
    }

    /// Something went wrong: the original sound comes back at once and the row says why (<paramref name="detail"/>: Windows' answer, tooltip only).
    protected void Fail(string reason, string? detail = null, StatusKind kind = StatusKind.Stopped)
    {
        Stop();
        SetStatus(EngineState.Failed, $"Effects stopped: {reason}. Playing without effects.", detail, kind);
    }

    /// How this run's effects run, as its row's short status names it.
    protected RunMethod Method => Spec.Kind switch
    {
        RunKind.Capture => RunMethod.Recording,
        RunKind.Bus => RunMethod.Bus,
        RunKind.ApoCable => RunMethod.ApoCable,
        _ => RunMethod.Cable,
    };

    protected void SetStatus(EngineState state, string text, string? detail = null, StatusKind kind = StatusKind.Other, int? delayMs = null)
    {
        Status = new EngineStatus(state, text, detail, kind, Method, delayMs);
        Changed?.Invoke(this);
    }

    /// The plug-ins' reported latency in the running chain, in ms (read live: a plug-in may change it while it runs).
    public int PluginDelayMs => PipeFormat.ToMs(_chain.LatencyFrames);

    /// The estimated delay of the processed sound: the pipe's own plus what the plug-ins add (OmniControl does not compensate their latency).
    public int DelayMs => (Pipe?.DelayMs ?? 0) + PluginDelayMs;

    /// The delay a row shows: <see cref="DelayMs"/> rounded and held steady (<see cref="StableDelay"/>); the raw figure until a first measurement exists.
    public int SteadyDelayMs => _delay.Shown ?? StableDelay.Round(DelayMs);

    /// The live meters of the effect built from <paramref name="slot"/> in the running chain (null: it has none or does not run here).
    public DynamicsReading? ReadingOf(FxSlot slot) => Status.IsRunning && _chain.ReadoutOf(slot) is { } r ? new DynamicsReading(r.ReductionDb, r.LevelDb) : null;

    /// The tap that records the sound entering the effect built from <paramref name="slot"/> in the running chain (null: it has none or does not run here).
    public SpectrumTap? TapOf(FxSlot slot) => Status.IsRunning ? _chain.TapOf(slot) : null;

    /// The running chain has at least one effect that runs (an empty chain only passes the sound on).
    protected bool HasEffects => _chain.StageCount > 0;

    /// The running status: <see cref="RunningText"/> with the delay.
    protected void ShowRunning()
    {
        var shown = _delay.Feed(DelayMs);
        var text = $"{RunningText} · {(shown is { } ms ? $"about {ms} ms delay" : "delay not measured yet")}{EngineStatus.SkippedNote(_chain.Skipped)}";
        if (Status.IsRunning && Status.Text == text) return;
        SetStatus(EngineState.Running, text, null, StatusKind.Running, shown);
    }

    /// What the run does, in a few words, before its delay ("Effects running", "Playing on 2 devices").
    protected virtual string RunningText => "Effects running";

    protected void ShowWaiting(string text, StatusKind kind)
    {
        if (Status.State == EngineState.Waiting && Status.Text == text) return; // called every tick by some runs
        SetStatus(EngineState.Waiting, text, null, kind);
    }

    /// Starts the pipe off the UI thread (Windows calls are slow), playing on the spec's outputs, and calls <paramref name="started"/> back on it.
    protected async void StartPipe(IReadOnlyList<PipeSource> sources, Action started)
    {
        AudioPipe pipe;
        try
        {
            pipe = await Task.Run(() => AudioPipe.StartAsync(sources, Spec.Outputs, Processor));
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message);
            return;
        }
        if (_stopped)
        {
            pipe.Dispose(); // stopped while it was starting
            return;
        }
        Pipe = pipe;
        pipe.Failed += reason => Dispatcher.UIThread.Post(() => { if (!_stopped) Fail(reason); });
        started();
    }

    /// Stops only the pipe (to start a new one for changed streams); the program stays held.
    protected void StopPipe()
    {
        Pipe?.Dispose();
        Pipe = null;
    }

    public void Dispose() => Stop();
}
