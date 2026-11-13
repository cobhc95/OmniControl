using Avalonia.Threading;
using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// Runs what <see cref="RoutePlanner"/> decides for every Volume row and keeps it in step with the settings (UI thread): the runs
/// (<see cref="ProgramEngine"/>, <see cref="CableRoute"/>, <see cref="BusRun"/>, <see cref="FanOutRun"/>, <see cref="MicRun"/>),
/// each program's own device, and in Fast mode the master's and the microphones' chains in the user's Equalizer APO (or OmniControl
/// APO, <see cref="ApoChainWriter"/>, <see cref="ApoConfigFiles"/>), whose line is taken out again whenever no chain runs there.
/// A run whose spec is unchanged keeps running. Without effects the sound plays unprocessed, and <see cref="StatusOf"/> says why.
/// A failed run stays stopped until the user changes that row or the routing. Dispose stops everything, puts every program's own
/// sound back and saves the plug-ins' settings (it does not unload them).
/// </summary>
public sealed class EffectsEngine : IDisposable
{
    private readonly MixerService _mixer;
    private readonly SoundModeService _modes;
    private readonly EngineJournal _journal;
    private readonly PluginChainHost _plugins;
    private readonly Action<string> _report;
    private readonly Dictionary<string, EngineRun> _runs = new();
    private readonly Dictionary<string, EngineStatus> _status = new();
    private readonly Dictionary<string, EngineStatus> _failed = new();
    private readonly HashSet<string> _testing = new();
    private readonly DispatcherTimer _tick = new() { Interval = Timing.EngineTick };
    private readonly DispatcherTimer _fxGap = new() { Interval = Timing.EngineChainApplyGap };
    /// <summary>Rows (the master as <see cref="MixerService.MasterKey"/>) whose chain changed inside the current gap.</summary>
    private readonly HashSet<string> _fxPending = new();
    private readonly ApoChains _apo;
    private IReadOnlySet<string> _cablesInUse = new HashSet<string>();
    /// <summary>The rows whose chain the last plan sends to Equalizer APO (Fast mode's master and microphones).</summary>
    private IReadOnlySet<string> _sentToApo = new HashSet<string>();
    private bool _disposed;

    /// <summary>A row's status changed (key null = the master). UI thread.</summary>
    public event Action<string?>? StatusChanged;

    public EffectsEngine(MixerService mixer, SoundModeService modes, EngineJournal journal, PluginChainHost plugins, Action<string> report)
    {
        _mixer = mixer;
        _modes = modes;
        _journal = journal;
        _plugins = plugins;
        _report = report;
        _apo = new ApoChains(report);
        _tick.Tick += OnTick;
        _fxGap.Tick += OnFxGapEnded;
        mixer.Updated += Reconcile;
        mixer.FxChanged += OnFxChanged;
        mixer.ProgramChanged += OnProgramChanged;
        mixer.RoutingChanged += OnRoutingChanged;
        modes.Changed += Reconcile;
        // Equalizer APO runs a VST2 from its saved settings: a newly saved state is written into its line (files change only when the text does).
        plugins.StatesSaved += Reconcile;
        Reconcile();
    }

    /// <summary>What the row's effects and routing are doing; key null = the master.</summary>
    public EngineStatus StatusOf(string? key) => _status.GetValueOrDefault(key ?? MixerService.MasterKey, EngineStatus.Off);

    /// <summary>The cables OmniControl's runs use now (the Wiring menu does not offer them as plain devices).</summary>
    public IReadOnlySet<string> CablesInUse => _cablesInUse;

    /// <summary>The cable (its playback side's endpoint id) the row's run uses now, or null when no run of that row uses one; key null = the master.</summary>
    public string? CableOf(string? key) => _runs.GetValueOrDefault(key ?? MixerService.MasterKey)?.Spec.Cable;

    /// <summary>
    /// Where the row's effects are processed now and what that costs (the FX window's Latency and CPU); key null = the master. Only a
    /// running status counts: its own run, or the chain sent to Equalizer APO.
    /// </summary>
    public RowLoad LoadOf(string? key)
    {
        var id = key ?? MixerService.MasterKey;
        if (!StatusOf(key).IsRunning) return RowLoad.None;
        if (_runs.TryGetValue(id, out var run)) return new RowLoad(LoadPlace.Own, run.DelayMs, run.PluginDelayMs, run.Cpu);
        return _sentToApo.Contains(id) ? RowLoad.InApo : RowLoad.None;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        foreach (var run in _runs.Values.ToList()) run.Tick();
    }

    /// <summary>The first edit applies at once; edits inside the following gap are gathered and the latest applied when it ends.</summary>
    private void OnFxChanged(string? key)
    {
        _fxPending.Add(key ?? MixerService.MasterKey);
        if (_fxGap.IsEnabled) return;
        ApplyPendingFx();
        _fxGap.Start();
    }

    private void OnFxGapEnded(object? sender, EventArgs e)
    {
        if (_fxPending.Count == 0) _fxGap.Stop();
        else ApplyPendingFx();
    }

    private void ApplyPendingFx()
    {
        var keys = _fxPending.ToList();
        _fxPending.Clear();
        foreach (var key in keys)
        {
            _failed.Remove(key); // the user changed this row: a failed run may try again
            if (_runs.TryGetValue(key, out var run) && run.Spec.Kind != RunKind.FanOut) run.SetChain(BuildChain(ChainKeyOf(run.Spec)));
        }
        Reconcile(); // switching effects on or off, or the first or last enabled effect, may start or stop a run
    }

    private void OnProgramChanged(string key)
    {
        if (_runs.TryGetValue(key, out var run)) run.OnProgramChanged();
    }

    private void OnRoutingChanged()
    {
        _failed.Clear(); // the user changed the routing: every failed run may try again
        Reconcile();
    }

    /// <summary>Plans again and brings every run, every program's own device and the master's APO line in line with the plan.</summary>
    private void Reconcile()
    {
        if (_disposed) return;
        _journal.RestoreRunning();
        if (_modes.Facts is not { } facts) return; // nothing starts before the first look at what this PC offers (it reconciles then)
        var playing = _mixer.Settings.Apps.Select(a => a.Key).Where(k => _mixer.OpenStreams(k).Count > 0).ToHashSet();
        var plan = RoutePlanner.Make(new PlanInput(_mixer.SoundMode, facts.Support, facts.CaptureWorks, _mixer.Settings, playing,
            _mixer.Devices, _mixer.RecordingDevices));
        _cablesInUse = plan.CablesInUse;
        _sentToApo = plan.ToApo.ToHashSet();
        RememberCables(plan.NewCables);
        foreach (var (key, device) in plan.Direct) _mixer.SetDirectRoute(key, device);

        foreach (var (key, run) in _runs.ToList())
            if (!plan.Runs.TryGetValue(key, out var spec) || !spec.SameAs(run.Spec)) StopRun(key);
        foreach (var spec in plan.Runs.Values) Keep(spec, facts);
        if (_runs.GetValueOrDefault(MixerService.MasterKey) is { } bus) bus.SetRouted(plan.BusRoutes);

        var fast = _mixer.SoundMode == SoundModes.Fast; // in Fast the master's status and the status of a microphone sent to the APO are ApplyApo's
        foreach (var key in RowKeys().Where(k => !_runs.ContainsKey(k) && !_testing.Contains(k) && !(fast && (k == MixerService.MasterKey || plan.ToApo.Contains(k)))))
            SetStatus(key, _failed.GetValueOrDefault(key) ?? plan.Blocked.GetValueOrDefault(key, EngineStatus.Off));
        ApplyApo(plan, facts.Support.Apo);
        _tick.IsEnabled = _runs.Count > 0;
    }

    /// <summary>Every row the engine reports on: the master, programs, recording devices with a chain, combined devices.</summary>
    private IEnumerable<string> RowKeys() =>
        new[] { MixerService.MasterKey }
            .Concat(_mixer.Settings.Apps.Select(a => a.Key))
            .Concat(_mixer.Settings.Inputs.Select(i => MixerService.InputPrefix + i.Id))
            .Concat(_mixer.Settings.Combined.Select(c => c.Key));

    /// <summary>Keeps a run of this spec: the one already running, or a new one (unless it failed and nothing changed since).</summary>
    private void Keep(RunSpec spec, SoundFacts facts)
    {
        if (_testing.Contains(spec.Key)) return;
        if (_failed.TryGetValue(spec.Key, out var failed))
        {
            SetStatus(spec.Key, failed);
            return;
        }
        if (_runs.TryGetValue(spec.Key, out var run))
        {
            run.OnStreamsChanged();
            return;
        }
        if (Create(spec, facts) is not { } created) return;
        _runs[spec.Key] = created;
        created.Changed += OnRunChanged;
        created.SetChain(spec.Kind == RunKind.FanOut ? DspChain.Empty : BuildChain(ChainKeyOf(spec)));
        created.Start();
        SetStatus(spec.Key, created.Status);
    }

    private EngineRun? Create(RunSpec spec, SoundFacts facts)
    {
        var name = NameOf(spec.Key);
        switch (spec.Kind)
        {
            case RunKind.Capture:
                return CaptureTestResult.MethodFor(_mixer.Settings.CaptureSilence) is { } method
                    ? new ProgramEngine(spec, name, _mixer, _journal, method)
                    : null; // the planner only plans Capture when the last Test did not fail
            case RunKind.Cable:
                return new CableRoute(spec, name, _mixer, _journal);
            case RunKind.Bus:
                return new BusRun(spec, _mixer, _journal);
            case RunKind.FanOut:
                return new FanOutRun(spec, name, _mixer, _journal);
            default:
                var cable = facts.Support.Cables.FirstOrDefault(c => c.PlaybackId == spec.Cable);
                return new MicRun(spec, name, _mixer, _journal, CombinedDevice.IsKey(spec.Key) ? new UserLevel() : null,
                    cable?.RecordingName ?? $"{cable?.Name} Output");
        }
    }

    /// <summary>The chain a run plays: the master's for the bus, else the row's own.</summary>
    private static string? ChainKeyOf(RunSpec spec) => spec.Kind == RunKind.Bus ? null : spec.Key;

    private string NameOf(string key) =>
        key == MixerService.MasterKey ? "Master"
        : MixerService.IsInput(key) ? _mixer.InputName(key)
        : _mixer.CombinedOf(key)?.Name ?? _mixer.Find(key)?.Name ?? key;

    /// <summary>A combined device or processed microphone got a cable: remember it, so apps that picked that cable keep finding it.</summary>
    private void RememberCables(IReadOnlyDictionary<string, string> cables)
    {
        foreach (var (key, cable) in cables)
        {
            if (_mixer.CombinedOf(key) is { } combo) combo.Cable = cable;
            else if (MixerService.IsInput(key)) _mixer.InputEntry(key).Cable = cable;
        }
        if (cables.Count > 0) _mixer.Save();
    }

    private DspChain BuildChain(string? key) => ChainBuilder.Build(_mixer.FxChain(key), _plugins.Get);

    private void OnRunChanged(EngineRun run)
    {
        if (run.Status.State == EngineState.Failed)
        {
            _failed[run.Key] = run.Status;
            _report($"{NameOf(run.Key)}: {run.Status.Text}");
            StopRun(run.Key);
        }
        SetStatus(run.Key, run.Status);
    }

    private void StopRun(string key)
    {
        if (!_runs.Remove(key, out var run)) return;
        run.Changed -= OnRunChanged;
        run.Stop();
        if (_runs.Count == 0 && !_disposed) _plugins.ReleaseUnused(new HashSet<FxSlot>());
    }

    /// <summary>
    /// Fast mode: the master's chain (with effects on and something enabled) goes to Equalizer APO on the default playback device, and
    /// each microphone's to Equalizer APO on that microphone, each in its own Device section; otherwise, and always in Full control,
    /// OmniControl's line and file are taken out of every config folder (<see cref="ApoChains"/>).
    /// </summary>
    private void ApplyApo(RoutePlan plan, ApoState apo)
    {
        var rows = plan.ToApo.Select(key => key == MixerService.MasterKey
                ? new ApoRow(key, NameOf(key), _mixer.FxChain(null), apo.Output, SoundModes.ApoMissing(apo), "Playing without effects.")
                : new ApoRow(key, $"Microphone: {NameOf(key)}", _mixer.FxChain(key), apo.MicrophoneOf(MixerService.InputIdOf(key)),
                    SoundModes.MicApoMissing(apo, MixerService.InputIdOf(key)), "Recording without effects."))
            .ToList();
        var statuses = _apo.Apply(rows, apo);
        if (_mixer.SoundMode != SoundModes.Fast) return; // Full control: the master's and the microphones' status come from their runs or the plan
        SetStatus(null, statuses.GetValueOrDefault(MixerService.MasterKey, EngineStatus.Off));
        foreach (var (key, status) in statuses.Where(s => s.Key != MixerService.MasterKey)) SetStatus(key, status);
    }

    private void SetStatus(string? key, EngineStatus status)
    {
        var id = key ?? MixerService.MasterKey;
        if (_status.TryGetValue(id, out var old) && old == status) return;
        _status[id] = status;
        StatusChanged?.Invoke(id == MixerService.MasterKey ? null : id);
    }

    /// <summary>"Test" on a program's effects (Fast mode): the row's own engine pauses while the test runs.</summary>
    public async Task<string> TestAsync(string key)
    {
        if (!_testing.Add(key)) return "A test is already running for this program.";
        StopRun(key);
        try
        {
            var test = new CaptureTest(_mixer, _journal, BuildChain(key));
            var (result, message) = await test.RunAsync(key);
            _mixer.Settings.CaptureSilence = result;
            _mixer.Save();
            _modes.NotifyTested();
            return message;
        }
        finally
        {
            _testing.Remove(key);
            _failed.Remove(key);
            Reconcile();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tick.Stop();
        _tick.Tick -= OnTick;
        _fxGap.Stop();
        _fxGap.Tick -= OnFxGapEnded;
        _fxPending.Clear();
        _mixer.Updated -= Reconcile;
        _mixer.FxChanged -= OnFxChanged;
        _mixer.ProgramChanged -= OnProgramChanged;
        _mixer.RoutingChanged -= OnRoutingChanged;
        _modes.Changed -= Reconcile;
        _plugins.StatesSaved -= Reconcile;
        foreach (var key in _runs.Keys.ToList()) StopRun(key);
        if (_modes.Current is { } current) ApoChains.Clear(current.Apo); // effects in Equalizer APO run only while OmniControl runs
        _plugins.SaveStates(); // the plug-ins' owner unloads them as the very last step of exit
    }
}
