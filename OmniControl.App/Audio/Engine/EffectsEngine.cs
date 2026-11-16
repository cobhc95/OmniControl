using Avalonia.Threading;
using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// Runs what <see cref="RoutePlanner"/> decides for every Volume row and keeps it in step with the settings (UI thread): the runs
/// (<see cref="ProgramEngine"/>, <see cref="CableRoute"/>, <see cref="BusRun"/>, <see cref="FanOutRun"/>, <see cref="MicRun"/>),
/// each program's own device, and the master's chain in the user's Equalizer APO (or OmniControl
/// APO, <see cref="ApoChainWriter"/>, <see cref="ApoConfigFiles"/>), whose line is taken out again whenever no chain runs there.
/// A run whose spec is unchanged keeps running. Without effects the sound plays unprocessed, and <see cref="StatusOf"/> says why.
/// A failed run stays stopped until the user changes that row or the routing. Dispose stops everything, puts every program's own
/// sound back and saves the plug-ins' settings (it does not unload them).
/// </summary>
public sealed class EffectsEngine : IDisposable
{
    private readonly MixerService _mixer;
    private readonly EffectsSupportService _support;
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
    private readonly BusDefault _busDefault;
    /// <summary>The master bus of the latest plan (null when none is planned).</summary>
    private BusPlan? _bus;
    private IReadOnlySet<string> _cablesInUse = new HashSet<string>();
    private CableOffer _cables = CableOffer.None;
    /// <summary>The rows whose chain the last plan sends to Equalizer APO (the master there, programs on a cable with Equalizer APO).</summary>
    private IReadOnlySet<string> _sentToApo = new HashSet<string>();
    private MasterFacts _masterFacts = MasterFacts.Unknown;
    private RunMethod _masterMethod = RunMethod.Apo;
    private CableUse _cableUses;
    private string? _apoCableWhy = EffectsRules.NeedsSecondCable;
    private bool _disposed;

    /// <summary>A row's status changed (key null = the master). UI thread.</summary>
    public event Action<string?>? StatusChanged;

    /// <summary>
    /// What the plan offers changed (a cable freed or taken, the master's method, Equalizer APO on a cable...): every menu and gate that
    /// reads <see cref="CablesFor"/>, <see cref="MasterFacts"/> or <see cref="CablesInUse"/> asks again. UI thread.
    /// </summary>
    public event Action? PlanChanged;

    public EffectsEngine(MixerService mixer, EffectsSupportService support, EngineJournal journal, PluginChainHost plugins, Action<string> report)
    {
        _mixer = mixer;
        _support = support;
        _journal = journal;
        _plugins = plugins;
        _report = report;
        _apo = new ApoChains(report);
        _busDefault = new BusDefault(mixer, report);
        _tick.Tick += OnTick;
        _fxGap.Tick += OnFxGapEnded;
        mixer.Updated += Reconcile;
        mixer.FxChanged += OnFxChanged;
        mixer.ProgramChanged += OnProgramChanged;
        mixer.RoutingChanged += OnRoutingChanged;
        support.Changed += OnSupportChanged;
        // Equalizer APO runs a VST2 from its saved settings: a newly saved state is written into its line (files change only when the text does).
        plugins.StatesSaved += Reconcile;
        Reconcile();
    }

    /// <summary>What the row's effects and routing are doing; key null = the master.</summary>
    public EngineStatus StatusOf(string? key) => _status.GetValueOrDefault(key ?? MixerService.MasterKey, EngineStatus.Off);

    /// <summary>The cables OmniControl's runs use now (the Wiring menu does not offer them as plain devices).</summary>
    public IReadOnlySet<string> CablesInUse => _cablesInUse;

    /// <summary>
    /// What the virtual cables can give the program <paramref name="key"/> now (its Method menu): how many pairs there are, whether one
    /// is free for it (the one it holds already counts) and whether the master bus holds one.
    /// </summary>
    public CableOffer CablesFor(string key) =>
        _runs.GetValueOrDefault(key)?.Spec.Kind == RunKind.Cable ? _cables with { Free = true } : _cables;

    /// <summary>Why the program <paramref name="key"/> can't use Equalizer APO on a cable now (null = it can; the one it holds counts).</summary>
    public string? ApoCableWhyFor(string key) => _runs.GetValueOrDefault(key)?.Spec.Kind == RunKind.ApoCable ? null : _apoCableWhy;

    /// <summary>What the master's method depends on now (its Method pop-down), from the last plan.</summary>
    public MasterFacts MasterFacts => _masterFacts;

    /// <summary>How the master runs now: <see cref="RunMethod.Bus"/> or <see cref="RunMethod.Apo"/>.</summary>
    public RunMethod MasterMethod => _masterMethod;

    /// <summary>What the cables are used for now besides the master bus.</summary>
    public CableUse CableUses => _cableUses;

    /// <summary>
    /// How the row's effects run, as the FX window and the tooltips name it: through a cable, except the master in Equalizer APO and a
    /// program on a cable with Equalizer APO on it. Key null = the master.
    /// </summary>
    public ModeMethod MethodFor(string? key)
    {
        if (key is null || key == MixerService.MasterKey)
            return _masterMethod == RunMethod.Apo ? EffectsRules.ApoOnSpeakers : EffectsRules.MethodFor(key);
        return _runs.GetValueOrDefault(key)?.Spec.Kind == RunKind.ApoCable ? EffectsRules.ApoOnCable : EffectsRules.MethodFor(key);
    }

    /// <summary>How the row's effects run right now (none while nothing runs): what a run or Equalizer APO does, in the short status' words.</summary>
    public RunMethod MethodOf(string? key)
    {
        var id = key ?? MixerService.MasterKey;
        if (_runs.TryGetValue(id, out var run)) return run.Spec.Kind switch
        {
            RunKind.Capture => RunMethod.Recording,
            RunKind.Bus => RunMethod.Bus,
            RunKind.ApoCable => RunMethod.ApoCable,
            _ => RunMethod.Cable,
        };
        return _sentToApo.Contains(id) ? RunMethod.Apo : RunMethod.None;
    }

    /// <summary>The cable (its playback side's endpoint id) the row's run uses now, or null when no run of that row uses one; key null = the master.</summary>
    public string? CableOf(string? key) => _runs.GetValueOrDefault(key ?? MixerService.MasterKey)?.Spec.Cable;

    /// <summary>The row (its key; the master as <see cref="MixerService.MasterKey"/>) whose run uses one of these cable inputs now, or null.</summary>
    public string? RowOnCable(IReadOnlyCollection<string> playbackIds) =>
        _runs.Values.FirstOrDefault(r => r.Spec.Cable is { } cable && playbackIds.Contains(cable))?.Key;

    /// <summary>
    /// Where the row's effects are processed now and the delay they add (the FX window's Latency); key null = the master. Only a
    /// running status counts: its own run, or the chain sent to Equalizer APO.
    /// </summary>
    public RowLoad LoadOf(string? key)
    {
        var id = key ?? MixerService.MasterKey;
        if (!StatusOf(key).IsRunning) return RowLoad.None;
        if (_runs.TryGetValue(id, out var run) && RunsChain(run.Spec.Kind)) return new RowLoad(LoadPlace.Own, run.SteadyDelayMs, run.PluginDelayMs);
        return _sentToApo.Contains(id) ? RowLoad.InApo : RowLoad.None;
    }

    /// <summary>
    /// The live meters of a dynamics effect (compressor, limiter) of the row's chain, read from the stage that processes the sound now;
    /// null when OmniControl's own engine does not run it (effects off, Equalizer APO, not started, switched off). Key null = the master.
    /// </summary>
    public DynamicsReading? ReadingOf(string? key, FxSlot slot) =>
        _runs.TryGetValue(key ?? MixerService.MasterKey, out var run) ? run.ReadingOf(slot) : null;

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
            if (_runs.TryGetValue(key, out var run) && RunsChain(run.Spec.Kind)) run.SetChain(BuildChain(ChainKeyOf(run.Spec)));
        }
        Reconcile(); // switching effects on or off, or the first or last enabled effect, may start or stop a run
    }

    private void OnProgramChanged(string key)
    {
        if (_runs.TryGetValue(key, out var run)) run.OnProgramChanged();
    }

    /// <summary>What the PC offers changed: a master bus that was given up (it heard nothing) may try again.</summary>
    private void OnSupportChanged()
    {
        _failed.Remove(MixerService.MasterKey);
        Reconcile();
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
        if (_support.Facts is not { } facts) return; // nothing starts before the first look at what this PC offers (it reconciles then)
        var playing = _mixer.Settings.Apps.Select(a => a.Key).Where(k => _mixer.OpenStreams(k).Count > 0).ToHashSet();
        var sounding = _mixer.Running.Where(r => r.IsPlaying).Select(r => r.Key).ToHashSet();
        var holding = RunsOf(RunKind.Cable);
        var plan = RoutePlanner.Make(new PlanInput(facts.Support, facts.CaptureWorks, _mixer.Settings, playing, sounding,
            holding, _mixer.Devices, _mixer.RecordingDevices, _failed.Keys.ToHashSet(), RunsOf(RunKind.Capture)));
        var offered = (_cables, _masterFacts, _masterMethod, _cableUses, _apoCableWhy);
        var inUse = _cablesInUse;
        _cablesInUse = plan.CablesInUse;
        _cables = new CableOffer(plan.CablePairs, plan.CableFree, plan.Bus is not null, plan.Master.NoCable);
        _sentToApo = plan.ToApo.ToHashSet();
        _bus = plan.Bus;
        _masterFacts = plan.Master;
        _masterMethod = plan.MasterMethod;
        _cableUses = plan.CableUses;
        _apoCableWhy = plan.ApoCableWhy;
        var offerChanged = offered != (_cables, _masterFacts, _masterMethod, _cableUses, _apoCableWhy) || !inUse.SetEquals(_cablesInUse);
        RememberCables(plan.NewCables);
        foreach (var (key, device) in plan.Direct) _mixer.SetDirectRoute(key, device);

        foreach (var (key, run) in _runs.ToList())
            if (!plan.Runs.TryGetValue(key, out var spec) || !spec.SameAs(run.Spec)) StopRun(key);
        foreach (var spec in plan.Runs.Values) Keep(spec, facts);
        if (_runs.GetValueOrDefault(MixerService.MasterKey) is { } bus) bus.SetRouted(plan.BusRoutes);
        SyncDefault();

        // The status of a row sent to Equalizer APO is ApplyApo's.
        foreach (var key in RowKeys().Where(k => !_runs.ContainsKey(k) && !_testing.Contains(k) && !plan.ToApo.Contains(k)))
            SetStatus(key, _failed.GetValueOrDefault(key) ?? plan.Blocked.GetValueOrDefault(key, EngineStatus.Off));
        ApplyApo(plan, facts.Support.Apo);
        _tick.IsEnabled = _runs.Count > 0;
        if (offerChanged) PlanChanged?.Invoke();
    }

    private HashSet<string> RunsOf(RunKind kind) => _runs.Values.Where(r => r.Spec.Kind == kind).Select(r => r.Key).ToHashSet();

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
        created.SetChain(RunsChain(spec.Kind) ? BuildChain(ChainKeyOf(spec)) : DspChain.Empty);
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
                    : null; // the planner plans Capture only when the last Test did not fail
            case RunKind.Cable or RunKind.ApoCable:
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

    /// <summary>Whether OmniControl's own engine runs a chain in this kind of run (combined speakers only copy; Equalizer APO runs a program's chain on its cable).</summary>
    private static bool RunsChain(RunKind kind) => kind is not (RunKind.FanOut or RunKind.ApoCable);

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

    /// <summary>The row's chain; empty while its effects are switched off (a run that only routes, such as the master bus, must not apply them).</summary>
    private DspChain BuildChain(string? key) => _mixer.IsFxOn(key) ? ChainBuilder.Build(_mixer.FxChain(key), _plugins.Get) : DspChain.Empty;

    private void OnRunChanged(EngineRun run)
    {
        // A failed run plans again (after this event): a given-up bus plays nothing, so no program run may keep playing into its cable,
        // and a failed program's cable goes to the next program that wants one.
        if (run.Status.State == EngineState.Failed) FailRun(run.Key, run.Status);
        else SetStatus(run.Key, run.Status);
        if (run.Key == MixerService.MasterKey) SyncDefault(); // the bus is up (the default may become its cable) or failed (it goes back)
    }

    /// <summary>Windows' default playback device follows the master bus: its cable while the bus runs, the real device otherwise.</summary>
    private void SyncDefault()
    {
        if (_disposed) return;
        var exists = _runs.TryGetValue(MixerService.MasterKey, out var bus); // a failed run is stopped and removed at once
        _busDefault.Sync(_bus, exists, exists && bus!.Status.IsRunning);
    }

    private void StopRun(string key)
    {
        if (!_runs.Remove(key, out var run)) return;
        run.Changed -= OnRunChanged;
        run.Stop();
        if (_runs.Count == 0 && !_disposed) _plugins.ReleaseUnused(new HashSet<FxSlot>());
    }

    /// <summary>
    /// The chains the plan sends to Equalizer APO, each in its own Device section (<see cref="ApoChains.RowsFor"/>): the master on
    /// the real speakers (method Equalizer APO), a
    /// program on its cable; with none, OmniControl's line and file are taken out of every config folder. A program's run on a cable
    /// whose chain could not be written fails (its run only passes the cable on); every other row takes the status Equalizer APO gives.
    /// </summary>
    private void ApplyApo(RoutePlan plan, ApoState apo)
    {
        var statuses = _apo.Apply(ApoChains.RowsFor(plan, apo, _mixer.FxChain, NameOf), apo);
        foreach (var (key, status) in statuses)
        {
            if (!_runs.ContainsKey(key)) SetStatus(key == MixerService.MasterKey ? null : key, status);
            else if (status.IsProblem) FailRun(key, status);
        }
    }

    /// <summary>A run that can't go on: it stops, the original sound comes back, the failure stands until the row or the routing changes.</summary>
    private void FailRun(string key, EngineStatus status)
    {
        _failed[key] = status;
        _report(StatusMessage.Compose($"{NameOf(key)}: {status.Brief}", $"{NameOf(key)}: {status.Text}"));
        StopRun(key);
        SetStatus(key, status);
        Dispatcher.UIThread.Post(Reconcile); // its cable goes to the next program that wants one
    }

    private void SetStatus(string? key, EngineStatus status)
    {
        var id = key ?? MixerService.MasterKey;
        if (_status.TryGetValue(id, out var old) && old == status) return;
        _status[id] = status;
        StatusChanged?.Invoke(id == MixerService.MasterKey ? null : id);
    }

    /// <summary>"Test" on a program's effects (can it be recorded while silenced?): the row's own engine pauses while the test runs.</summary>
    public async Task<TestOutcome> TestAsync(string key)
    {
        if (!_testing.Add(key)) return EffectsRules.TestAlreadyRunning();
        StopRun(key);
        try
        {
            var test = new CaptureTest(_mixer, _journal, BuildChain(key));
            var (result, outcome) = await test.RunAsync(key);
            _mixer.Settings.CaptureSilence = result;
            _mixer.Save();
            _support.NotifyTested();
            return outcome;
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
        _support.Changed -= OnSupportChanged;
        _plugins.StatesSaved -= Reconcile;
        foreach (var key in _runs.Keys.ToList()) StopRun(key);
        _busDefault.Release(); // after every program is routed back: Windows' default goes back to the real speakers
        _busDefault.Dispose();
        if (_support.Current is { } current) _apo.Clear(current.Apo); // effects in Equalizer APO run only while OmniControl runs
        _plugins.SaveStates(); // the plug-ins' owner unloads them as the very last step of exit
    }
}
