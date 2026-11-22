using OmniControl.App.Features.Volume.Audio.Engine.Dsp;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// Runs what <see cref="RoutePlanner"/> decides for every Volume row and keeps it in step with the settings (UI thread): the runs
/// (<see cref="ProgramEngine"/>, <see cref="CableRoute"/>, <see cref="BusRun"/>, <see cref="FanOutRun"/>, <see cref="MicRun"/>),
/// each program's own device, and the master's chain in the user's Equalizer APO (or OmniControl
/// APO, <see cref="ApoChainWriter"/>, <see cref="ApoConfigFiles"/>), whose line is taken out again whenever no chain runs there and is
/// kept in place from what is really on disk (<see cref="ApoWatch"/> reads the folders off the UI thread, <see cref="ApoKeeper"/> puts
/// back what another program took out, or leaves it and says so while "Keep OmniControl's effects in place" is off).
/// A run whose spec is unchanged keeps running. Without effects the sound plays unprocessed, and <see cref="StatusOf"/> says why.
/// A failed run stays stopped until the user changes that row or the routing. Dispose stops everything, puts every program's own
/// sound back and saves the plug-ins' settings (it does not unload them).
public sealed partial class EffectsEngine : IDisposable
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
    /// Rows (the master as <see cref="MixerService.MasterKey"/>) whose chain changed inside the current gap.
    private readonly HashSet<string> _fxPending = new();
    private readonly ApoChains _apo;
    private readonly ApoKeeper _keeper;
    /// Reads the config folders and the IR files in use off the UI thread.
    private readonly ApoWatch _watch;
    /// What the watch compares the disk with; replaced (never changed) on the UI thread, read by the watch's worker.
    private volatile ApoWatchInput _watchInput = ApoWatchInput.None;
    private readonly BusDefault _busDefault;
    /// The master bus of the latest plan (null when none is planned).
    private BusPlan? _bus;
    private IReadOnlySet<string> _cablesInUse = new HashSet<string>();
    private CableOffer _cables = CableOffer.None;
    /// The rows whose chain the last plan sends to Equalizer APO (the master there, programs on a cable with Equalizer APO).
    private IReadOnlySet<string> _sentToApo = new HashSet<string>();
    private string? _masterApoDevice;
    private MasterFacts _masterFacts = MasterFacts.Unknown;
    private RunMethod _masterMethod = RunMethod.Apo;
    private IReadOnlyDictionary<string, (MasterFacts Facts, RunMethod Method)> _mics = new Dictionary<string, (MasterFacts, RunMethod)>();
    private CableUse _cableUses;
    private string? _apoCableWhy = EffectsRules.NeedsSecondCable;
    private bool _disposed;

    /// A row's status changed (key null = the master). UI thread.
    public event Action<string?>? StatusChanged;

    /// What the plan offers changed (a cable freed or taken, the master's method, Equalizer APO on a cable...): every menu and gate that
    /// reads <see cref="CablesFor"/>, <see cref="MasterFacts"/> or <see cref="CablesInUse"/> asks again. UI thread.
    public event Action? PlanChanged;

    /// Equalizer APO's config was found without OmniControl's effects, or with them again, or with effects of its own (<see cref="ApoDisplaced"/>, <see cref="ApoOwnEffects"/>). UI thread.
    public event Action? ApoPlacementChanged;

    /// The master's effects are planned in Equalizer APO on a device where installing it would make them run
    /// (<see cref="EffectsRules.InstallFixes"/>), after the user changed the master's effects or the routing in this run (never at start-up):
    /// raised on every such plan until it runs there; the listener decides whether to offer it. UI thread.
    public event Action? ApoWanted;

    /// A plug-in's saved settings changed (its editor was edited or closed): an FX window measures its chain again. UI thread.
    public event Action? PluginStatesSaved;

    private void OnPluginStatesSaved()
    {
        Reconcile();
        PluginStatesSaved?.Invoke();
    }

    /// The user changed the master's effects or the routing since the start (the first use of a master effect).
    private bool _masterTouched;

    public EffectsEngine(MixerService mixer, EffectsSupportService support, EngineJournal journal, PluginChainHost plugins, Action<string> report)
    {
        _mixer = mixer;
        _support = support;
        _journal = journal;
        _plugins = plugins;
        _report = report;
        _apo = new ApoChains(report, () => Dispatcher.UIThread.Post(() => Guard.Run("Effects: Equalizer APO chains again", Reconcile)),
            write => Dispatcher.UIThread.Post(() => Guard.Run("Effects: writing Equalizer APO's chains", write), DispatcherPriority.Background));
        _keeper = new ApoKeeper(_apo, () => mixer.KeepFxInPlace, report, OnApoPlacementChanged);
        _watch = new ApoWatch(ApoConfigFolders.Instance, () => _watchInput,
            found => Dispatcher.UIThread.Post(() => Guard.Run("Effects: Equalizer APO config check", () => OnApoFound(found))));
        _busDefault = new BusDefault(mixer, report);
        _tick.Tick += OnTick;
        _fxGap.Tick += OnFxGapEnded;
        mixer.Updated += Reconcile;
        mixer.FxChanged += OnFxChanged;
        mixer.ProgramChanged += OnProgramChanged;
        mixer.RoutingChanged += OnRoutingChanged;
        mixer.KeepFxInPlaceChanged += OnKeepFxInPlaceChanged;
        mixer.MicrophonesChanged += OnMicrophonesChanged;
        support.Changed += OnSupportChanged;
        // Equalizer APO runs a VST2 from its saved settings: a newly saved state is written into its line (files change only when the text does).
        plugins.StatesSaved += OnPluginStatesSaved;
        Reconcile();
    }

    /// What the row's effects and routing are doing; key null = the master.
    public EngineStatus StatusOf(string? key) => _status.GetValueOrDefault(key ?? MixerService.MasterKey, EngineStatus.Off);

    /// Sound runs through OmniControl itself now (a run or a test tone; Equalizer APO's work is Windows' audio service's).
    public bool Streams => _runs.Count > 0 || _testing.Count > 0;

    /// The cables OmniControl's runs use now (the Wiring menu does not offer them as plain devices).
    public IReadOnlySet<string> CablesInUse => _cablesInUse;

    /// What the virtual cables can give the program <paramref name="key"/> now (its Method menu): how many pairs there are, whether one
    /// is free for it (the one it holds already counts) and whether the master bus holds one.
    public CableOffer CablesFor(string key) =>
        _runs.GetValueOrDefault(key)?.Spec.Kind == RunKind.Cable ? _cables with { Free = true } : _cables;

    /// Why the program <paramref name="key"/> can't use Equalizer APO on a cable now (null = it can; the one it holds counts).
    public string? ApoCableWhyFor(string key) => _runs.GetValueOrDefault(key)?.Spec.Kind == RunKind.ApoCable ? null : _apoCableWhy;

    /// Equalizer APO's config no longer includes OmniControl's effects and is left so ("Keep OmniControl's effects in place" is off).
    public bool ApoDisplaced => _apo.AnyDisplaced;

    /// Equalizer APO's config.txt runs effects of its own besides OmniControl's chain (the last look at the disk).
    public bool ApoOwnEffects => _keeper.OwnEffects;

    /// "Put back": OmniControl's include line and file go back into every config folder that should have them.
    public void PutBackApo() => _keeper.PutBack();

    /// What the master's method depends on now (its Method pop-down), from the last plan.
    public MasterFacts MasterFacts => _masterFacts;

    /// How the master runs now: <see cref="RunMethod.Bus"/> or <see cref="RunMethod.Apo"/>.
    public RunMethod MasterMethod => _masterMethod;

    /// What a microphone's method depends on now (its Method pop-down), from the last plan; key = its input key.
    public MasterFacts MicFactsOf(string key) => _mics.TryGetValue(key, out var mic) ? mic.Facts : MasterFacts.Unknown;

    /// How a microphone runs now: <see cref="RunMethod.Cable"/> or <see cref="RunMethod.Apo"/>.
    public RunMethod MicMethodOf(string key) => _mics.TryGetValue(key, out var mic) ? mic.Method : RunMethod.Cable;

    /// What the cables are used for now besides the master bus.
    public CableUse CableUses => _cableUses;

    /// How the row's effects run, as the FX window and the tooltips name it: through a cable, except the master or a microphone in Equalizer APO and a
    /// program on a cable with Equalizer APO on it. Key null = the master.
    public ModeMethod MethodFor(string? key)
    {
        if (key is null || key == MixerService.MasterKey)
            return _masterMethod == RunMethod.Apo ? EffectsRules.ApoOnDevice : EffectsRules.MethodFor(key);
        if (MixerService.IsInput(key) && MicMethodOf(key) == RunMethod.Apo) return EffectsRules.ApoOnDevice;
        return _runs.GetValueOrDefault(key)?.Spec.Kind == RunKind.ApoCable ? EffectsRules.ApoOnCable : EffectsRules.MethodFor(key);
    }

    /// How the row's effects run right now (none while nothing runs): what a run or Equalizer APO does, in the short status' words.
    public RunMethod MethodOf(string? key)
    {
        var id = key ?? MixerService.MasterKey;
        if (_runs.TryGetValue(id, out var run)) return run.Method;
        return _sentToApo.Contains(id) ? RunMethod.Apo : RunMethod.None;
    }

    /// The playback device whose sound a display copy of the effect may be fed with (what you hear, after Equalizer APO): only for an
    /// effect Equalizer APO runs on the master, which OmniControl's own engine does not process. Null for everything else (a row in
    /// OmniControl's own engine has the real sound; a program on a cable with Equalizer APO is not fed). An effect in a device folder is fed
    /// only while its folder runs, so the feed is always its own device's sound, scaled back by that device's own volume (<see cref="DisplayHold"/>).
    public string? DisplayDeviceOf(string? key, FxSlot slot) =>
        (key is null || key == MixerService.MasterKey) && _sentToApo.Contains(MixerService.MasterKey) && FxCatalog.EnginesOf(slot).HasFlag(FxEngines.Apo)
            && FxFolders.ChainFor(_mixer.FxChain(null), _masterApoDevice).Contains(slot) ? _masterApoDevice : null;

    /// Equalizer APO, where OmniControl writes chains, blends a mix below 100 % (<see cref="EffectsRules.ApoBlends"/>): the FX curves read it.
    public bool ApoBlends => EffectsRules.ApoBlends(_support.Current?.Apo ?? Core.Engine.Apo.ApoState.None);

    /// The tap on the sound entering the effect in the row's own run now (the real signal); null when OmniControl's engine does not run it.
    internal SpectrumTap? OwnTapOf(string? key, FxSlot slot) =>
        _runs.TryGetValue(key ?? MixerService.MasterKey, out var run) ? run.TapOf(slot) : null;

    /// The cable (its playback side's endpoint id) the row's run uses now, or null when no run of that row uses one; key null = the master.
    public string? CableOf(string? key) => _runs.GetValueOrDefault(key ?? MixerService.MasterKey)?.Spec.Cable;

    /// The row (its key; the master as <see cref="MixerService.MasterKey"/>) whose run uses one of these cable inputs now, or null.
    public string? RowOnCable(IReadOnlyCollection<string> playbackIds) =>
        _runs.Values.FirstOrDefault(r => r.Spec.Cable is { } cable && playbackIds.Contains(cable))?.Key;

    /// Where the row's effects are processed now and the delay they add (the FX window's Latency); key null = the master. Only a
    /// running status counts: its own run, or the chain sent to Equalizer APO.
    public RowLoad LoadOf(string? key)
    {
        var id = key ?? MixerService.MasterKey;
        if (!StatusOf(key).IsRunning) return RowLoad.None;
        if (_runs.TryGetValue(id, out var run) && RunsChain(run.Spec.Kind)) return new RowLoad(LoadPlace.Own, run.SteadyDelayMs, run.PluginDelayMs);
        return _sentToApo.Contains(id) ? RowLoad.InApo : RowLoad.None;
    }

    /// The live meters of a dynamics effect (compressor, limiter) of the row's chain, read from the stage that processes the sound now;
    /// null when OmniControl's own engine does not run it (effects off, Equalizer APO, not started, switched off). Key null = the master.
    public DynamicsReading? ReadingOf(string? key, FxSlot slot) =>
        _runs.TryGetValue(key ?? MixerService.MasterKey, out var run) ? run.ReadingOf(slot) : null;

    private void OnTick(object? sender, EventArgs e)
    {
        foreach (var run in _runs.Values.ToList()) run.Tick();
        ReleaseRemovedPlugins();
    }

    /// The first edit applies at once; edits inside the following gap are gathered and the latest applied when it ends.
    private void OnFxChanged(string? key)
    {
        _masterTouched |= key is null or MixerService.MasterKey;
        _fxPending.Add(key ?? MixerService.MasterKey);
        if (_fxGap.IsEnabled) return;
        ApplyPendingFx();
        _fxGap.Start();
    }

    private void OnFxGapEnded(object? sender, EventArgs e)
    {
        if (_fxPending.Count == 0)
        {
            _fxGap.Stop();
            _releaseDue = true;
            ReleaseRemovedPlugins();
        }
        else ApplyPendingFx();
    }

    /// Chains changed and plug-ins they no longer hold wait to be unloaded.
    private bool _releaseDue;

    /// Unloads plug-ins no running chain uses any more (a removed effect), once every run's audio thread has started a block with its
    /// latest chain (<see cref="EngineRun.ChainSettled"/>), so it never meets a freed plug-in; until then it is tried again each tick.
    private void ReleaseRemovedPlugins()
    {
        if (_disposed || !_releaseDue || _runs.Values.Any(r => !r.ChainSettled)) return;
        _releaseDue = false;
        var inUse = _runs.Values.Where(r => RunsChain(r.Spec.Kind) && _mixer.IsFxOn(ChainKeyOf(r.Spec)))
            .SelectMany(r => _mixer.FxChain(ChainKeyOf(r.Spec))).ToHashSet<FxSlot>(ReferenceEqualityComparer.Instance);
        _plugins.ReleaseUnused(inUse);
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

    /// What the PC offers changed: a master bus that was given up (it heard nothing) may try again.
    private void OnSupportChanged()
    {
        _failed.Remove(MixerService.MasterKey);
        Reconcile();
    }

    /// "Use microphones" changed: off stops every microphone run and takes the microphones' chains out of Equalizer APO at once; either
    /// way a microphone's failed run may try again.
    private void OnMicrophonesChanged()
    {
        foreach (var key in _failed.Keys.Where(k => MixerService.IsInput(k) || _mixer.CombinedOf(k)?.Kind == CombinedKind.Microphones).ToList()) _failed.Remove(key);
        Reconcile();
    }

    private void OnRoutingChanged()
    {
        _masterTouched = true;
        _failed.Clear(); // the user changed the routing: every failed run may try again
        Reconcile();
    }

    /// Plans again and brings every run, every program's own device and the master's APO line in line with the plan.
    private void Reconcile()
    {
        if (_disposed) return;
        _journal.RestoreRunning();
        if (_support.Facts is not { } facts) return; // nothing starts before the first look at what this PC offers (it reconciles then)
        var playing = _mixer.Settings.Apps.Select(a => a.Key).Where(k => _mixer.Holds.OpenStreams(k).Count > 0).ToHashSet();
        var sounding = _mixer.Running.Where(r => r.IsPlaying).Select(r => r.Key).ToHashSet();
        var holding = RunsOf(RunKind.Cable);
        var plan = RoutePlanner.Make(new PlanInput(facts.Support, facts.CaptureWorks, _mixer.Settings, playing, sounding,
            holding, _mixer.Devices, _mixer.RecordingDevices, _failed.Keys.ToHashSet(), RunsOf(RunKind.Capture)));
        var offered = (_cables, _masterFacts, _masterMethod, _cableUses, _apoCableWhy);
        var inUse = _cablesInUse;
        _cablesInUse = plan.CablesInUse;
        _cables = new CableOffer(plan.CablePairs, plan.CableFree, plan.Bus is not null, plan.Master.NoCable);
        _sentToApo = plan.ToApo.ToHashSet();
        _masterApoDevice = plan.MasterApoDevice;
        _bus = plan.Bus;
        _masterFacts = plan.Master;
        _masterMethod = plan.MasterMethod;
        var micsChanged = !_mics.OrderBy(m => m.Key).SequenceEqual(plan.Mics.OrderBy(m => m.Key));
        _mics = plan.Mics;
        _cableUses = plan.CableUses;
        _apoCableWhy = plan.ApoCableWhy;
        var offerChanged = offered != (_cables, _masterFacts, _masterMethod, _cableUses, _apoCableWhy) || !inUse.SetEquals(_cablesInUse) || micsChanged;
        RememberCables(plan.NewCables);
        foreach (var (key, device) in plan.Direct) _mixer.SetDirectRoute(key, device);

        foreach (var (key, run) in _runs.ToList())
            if (!plan.Runs.TryGetValue(key, out var spec) || !spec.SameAs(run.Spec)) StopRun(key);
        foreach (var spec in plan.Runs.Values) Keep(spec, facts);
        if (_runs.GetValueOrDefault(MixerService.MasterKey) is { } bus)
        {
            bus.SetRouted(plan.BusRoutes);
            if (_chainDevice != plan.Bus?.Real.Id && FxFolders.HasFolders(_mixer.FxChain(null))) bus.SetChain(BuildChain(null)); // the playing device changed: its folder's effects take over
        }
        _chainDevice = plan.Bus?.Real.Id;
        SyncDefault();

        // The status of a row sent to Equalizer APO is ApplyApo's.
        foreach (var key in RowKeys().Where(k => !_runs.ContainsKey(k) && !_testing.Contains(k) && !plan.ToApo.Contains(k)))
            SetStatus(key, _failed.GetValueOrDefault(key) ?? plan.Blocked.GetValueOrDefault(key, EngineStatus.Off));
        ApplyApo(plan, facts.Support.Apo);
        _tick.IsEnabled = _runs.Count > 0;
        if (offerChanged) PlanChanged?.Invoke();
    }

    private HashSet<string> RunsOf(RunKind kind) => _runs.Values.Where(r => r.Spec.Kind == kind).Select(r => r.Key).ToHashSet();

    /// Every row the engine reports on: the master, programs, recording devices with a chain, combined devices.
    private IEnumerable<string> RowKeys() =>
        new[] { MixerService.MasterKey }
            .Concat(_mixer.Settings.Apps.Select(a => a.Key))
            .Concat(_mixer.Settings.Inputs.Select(i => MixerService.InputPrefix + i.Id))
            .Concat(_mixer.Settings.Combined.Select(c => c.Key));

    /// Keeps a run of this spec: the one already running, or a new one (unless it failed and nothing changed since).
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

    /// The chain a run plays: the master's for the bus, else the row's own.
    private static string? ChainKeyOf(RunSpec spec) => spec.Kind == RunKind.Bus ? null : spec.Key;

    /// Whether OmniControl's own engine runs a chain in this kind of run (combined speakers only copy; Equalizer APO runs a program's chain on its cable).
    private static bool RunsChain(RunKind kind) => kind is not (RunKind.FanOut or RunKind.ApoCable);

    private string NameOf(string key) =>
        key == MixerService.MasterKey ? "Master"
        : MixerService.IsInput(key) ? _mixer.InputName(key)
        : _mixer.CombinedOf(key)?.Name ?? _mixer.Find(key)?.Name ?? key;

    /// A combined device or processed microphone got a cable: remember it, so apps that picked that cable keep finding it.
    private void RememberCables(IReadOnlyDictionary<string, string> cables)
    {
        foreach (var (key, cable) in cables)
        {
            if (_mixer.CombinedOf(key) is { } combo) combo.Cable = cable;
            else if (MixerService.IsInput(key)) _mixer.InputEntry(key).Cable = cable;
        }
        if (cables.Count > 0) _mixer.Save();
    }

    /// The row's chain; empty while its effects are switched off (a run that only routes, such as the master bus, must not apply them).
    /// <remarks>The master's chain is what runs for the device the bus plays on now (<see cref="FxFolders.ChainFor"/>).</remarks>
    private DspChain BuildChain(string? key) => _mixer.IsFxOn(key)
        ? key is null ? ChainBuilder.BuildParts(FxFolders.PartsFor(_mixer.FxChain(null), _bus?.Real.Id), _plugins.Get) : ChainBuilder.Build(_mixer.FxChain(key), _plugins.Get)
        : DspChain.Empty;

    /// The device the master bus plays on when its chain was last built (a folder's effects follow it).
    private string? _chainDevice;

    private void OnRunChanged(EngineRun run)
    {
        // A failed run plans again (after this event): a given-up bus plays nothing, so no program run may keep playing into its cable,
        // and a failed program's cable goes to the next program that wants one.
        if (run.Status.State == EngineState.Failed) FailRun(run.Key, run.Status);
        else SetStatus(run.Key, run.Status);
        if (run.Key == MixerService.MasterKey) SyncDefault(); // the bus is up (the default may become its cable) or failed (it goes back)
    }

    /// Windows' default playback device follows the master bus: its cable while the bus runs, the real device otherwise.
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

    /// The chains the plan sends to Equalizer APO, each in its own Device section (<see cref="ApoChains.RowsFor"/>): the master on
    /// the real speakers (method Equalizer APO), a
    /// program on its cable; with none, OmniControl's line and file are taken out of every config folder. A program's run on a cable
    /// whose chain could not be written fails (its run only passes the cable on); every other row takes the status Equalizer APO gives.
    private void ApplyApo(RoutePlan plan, ApoState apo)
    {
        var statuses = _apo.Apply(ApoChains.RowsFor(plan, apo, _mixer.FxChain, NameOf), apo);
        foreach (var (key, status) in statuses)
        {
            if (!_runs.ContainsKey(key)) SetStatus(key == MixerService.MasterKey ? null : key, status);
            else if (status.IsProblem) FailRun(key, status);
        }
        _watchInput = new ApoWatchInput(_apo.Target, IrFilesInUse());
        _watch.Follow(_apo.Target.Folders);
        _watch.Request(); // every change, device change and support change is checked against the real files
        if (_masterTouched && plan.ToApo.Contains(MixerService.MasterKey) && plan.MasterApoDevice is { } device && EffectsRules.InstallFixes(apo, apo.PlaybackOf(device)))
            ApoWanted?.Invoke();
    }

    /// The IR files of every chain whose effects are on, with the version each was loaded at: a file already known keeps the version it was
    /// loaded at, so a replacement is noticed by the watch (and loaded again) instead of being taken as the new normal.
    private IReadOnlyDictionary<string, long> IrFilesInUse()
    {
        var known = _watchInput.IrFiles;
        return RowKeys().Select(k => k == MixerService.MasterKey ? null : k).Where(_mixer.IsFxOn)
            .SelectMany(_mixer.FxChain).Where(s => s.Enabled && s.Kind == FxCatalog.IrKind && s.Path.Length > 0)
            .Select(s => s.Path).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, path => known.TryGetValue(path, out var stamp) ? stamp : Ir.IrFileStamp.Of(path), StringComparer.OrdinalIgnoreCase);
    }

    /// One look at the disk (UI thread): woken from sleep, a replaced IR file, and OmniControl's part of the config folders.
    private void OnApoFound(ApoFindings found)
    {
        if (_disposed) return;
        if (found.Resumed) _support.Refresh(); // the devices and Equalizer APO's state may differ after sleep; the answer reconciles
        if (found.IrReplaced)
        {
            ReloadIrFiles();
            return; // the reconcile writes the new chains and asks for a fresh look
        }
        if (!_keeper.Take(found)) _watch.Request();
    }

    /// An IR file in use was replaced under the same name: the runs that play one load it again, and the APO chains are rewritten.
    private void ReloadIrFiles()
    {
        foreach (var run in _runs.Values.Where(r => RunsChain(r.Spec.Kind)))
            if (_mixer.FxChain(ChainKeyOf(run.Spec)).Any(s => s.Kind == FxCatalog.IrKind)) run.SetChain(BuildChain(ChainKeyOf(run.Spec)));
        _watchInput = _watchInput with { IrFiles = new Dictionary<string, long>() }; // the versions just loaded are taken afresh
        Reconcile();
    }

    /// The keeper found the config changed (displaced, put back, own effects): the rows say so.
    private void OnApoPlacementChanged()
    {
        // A program's run on an Equalizer APO cable that stopped because its line was taken out may start again once it is back.
        if (!_apo.AnyDisplaced)
            foreach (var key in _failed.Where(f => f.Value.Kind == StatusKind.ApoDisplaced).Select(f => f.Key).ToList()) _failed.Remove(key);
        Reconcile();
        ApoPlacementChanged?.Invoke();
    }

    /// Switched on: whatever was taken out goes back at once. Switched off: nothing changes until another program takes it out.
    private void OnKeepFxInPlaceChanged()
    {
        if (_mixer.KeepFxInPlace) _keeper.PutBack();
    }

    /// A run that can't go on: it stops, the original sound comes back, the failure stands until the row or the routing changes.
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

    /// "Test" on a program's effects (can it be recorded while silenced?): the row's own engine pauses while the test runs.
    public async Task<TestOutcome> TestAsync(string key)
    {
        if (!_testing.Add(key)) return EffectsRules.TestAlreadyRunning();
        StopRun(key);
        try
        {
            var test = new CaptureTest(_mixer, _journal, BuildChain(key));
            var (result, outcome) = await test.RunAsync(key);
            _mixer.KeepCaptureSilence(result);
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
        _mixer.KeepFxInPlaceChanged -= OnKeepFxInPlaceChanged;
        _mixer.MicrophonesChanged -= OnMicrophonesChanged;
        _support.Changed -= OnSupportChanged;
        _plugins.StatesSaved -= OnPluginStatesSaved;
        foreach (var key in _runs.Keys.ToList()) StopRun(key);
        _busDefault.Release(); // after every program is routed back: Windows' default goes back to the real speakers
        _busDefault.Dispose();
        _watch.Dispose(); // first, so nothing is put back while the line is taken out
        _keeper.Dispose();
        if (_support.Current is { } current) _apo.Clear(current.Apo); // effects in Equalizer APO run only while OmniControl runs
        _plugins.SaveStates(); // the plug-ins' owner unloads them as the very last step of exit
    }
}
