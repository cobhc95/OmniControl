using Avalonia.Threading;
using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// One row's engine while it runs (the shared part of every run: <see cref="ProgramEngine"/>, <see cref="CableRoute"/>,
/// <see cref="BusRun"/>, <see cref="FanOutRun"/>, <see cref="MicRun"/>): the spec it was made from, the chain, the audio pipe and the
/// status. Lives on the UI thread; the pipe's own threads only ever post back to it. <see cref="Stop"/> always puts the original
/// sound back first and is safe to call more than once.
/// </summary>
internal abstract class EngineRun : IDisposable
{
    private DspChain _chain = DspChain.Empty;
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

    /// <summary>What this run was made from; a new plan with the same spec keeps it running.</summary>
    public RunSpec Spec { get; }
    public string Key { get; }
    protected string Name { get; }
    protected MixerService Mixer { get; }
    protected EngineJournal Journal { get; }
    protected ChainProcessor Processor { get; }
    protected AudioPipe? Pipe { get; private set; }
    protected bool IsStopped => _stopped;

    public EngineStatus Status { get; private set; } = new(EngineState.Waiting, "Starting effects…");

    /// <summary>The status changed (UI thread).</summary>
    public event Action<EngineRun>? Changed;

    /// <summary>The chain to run from now on (edits apply live).</summary>
    public void SetChain(DspChain chain)
    {
        Processor.SetChain(chain);
        _chain = chain;
        if (Status.IsRunning) ShowRunning();
    }

    /// <summary>Starts the effects (UI thread).</summary>
    public abstract void Start();

    /// <summary>Called every <see cref="Services.Timing.EngineTick"/> while the run exists.</summary>
    public virtual void Tick()
    {
        if (Status.IsRunning) ShowRunning();
    }

    /// <summary>The user changed the row's volume, pan or mute.</summary>
    public virtual void OnProgramChanged() { }

    /// <summary>The program's streams changed (a process opened or closed).</summary>
    public virtual void OnStreamsChanged() { }

    /// <summary>The programs the master bus carries changed (only the bus uses it).</summary>
    public virtual void SetRouted(IReadOnlyCollection<string> keys) { }

    /// <summary>Puts the program's own sound back (silence or routing), then stops the pipe.</summary>
    protected abstract void Undo();

    /// <summary>Stops for good: undoes everything first, then stops the pipe.</summary>
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        Undo();
        StopPipe();
    }

    /// <summary>Something went wrong: the original sound comes back at once and the row says why.</summary>
    protected void Fail(string reason)
    {
        Stop();
        SetStatus(EngineState.Failed, $"Effects stopped: {reason}. Playing without effects.");
    }

    protected void SetStatus(EngineState state, string text)
    {
        Status = new EngineStatus(state, text);
        Changed?.Invoke(this);
    }

    /// <summary>The plug-ins' reported latency in the running chain, in ms (read live: a plug-in may change it while it runs).</summary>
    public int PluginDelayMs => PipeFormat.ToMs(_chain.LatencyFrames);

    /// <summary>The estimated delay of the processed sound: the pipe's own plus what the plug-ins add (OmniControl does not compensate their latency).</summary>
    public int DelayMs => (Pipe?.DelayMs ?? 0) + PluginDelayMs;

    /// <summary>The processing time of this run so far.</summary>
    public CpuSample Cpu => Processor.Cpu.Read();

    /// <summary>The running status: <see cref="RunningText"/> with the delay.</summary>
    protected void ShowRunning()
    {
        var text = $"{RunningText} · about {DelayMs} ms delay{EngineStatus.SkippedNote(_chain.Skipped)}";
        if (Status.IsRunning && Status.Text == text) return;
        SetStatus(EngineState.Running, text);
    }

    /// <summary>What the run does, in a few words, before its delay ("Effects running", "Playing on 2 devices").</summary>
    protected virtual string RunningText => "Effects running";

    protected void ShowWaiting(string text) => SetStatus(EngineState.Waiting, text);

    /// <summary>Starts the pipe off the UI thread (Windows calls are slow), playing on the spec's outputs, and calls <paramref name="started"/> back on it.</summary>
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

    /// <summary>Stops only the pipe (to start a new one for changed streams); the program stays held.</summary>
    protected void StopPipe()
    {
        Pipe?.Dispose();
        Pipe = null;
    }

    public void Dispose() => Stop();
}
