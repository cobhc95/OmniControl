using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>The kinds of engine runs (what <see cref="EffectsEngine"/> starts for a row).</summary>
internal enum RunKind
{
    /// <summary>
    /// A program recorded, its original silenced, the processed copy played (<see cref="ProgramEngine"/>): every processed program in
    /// Fast; in Full control a program that records instead of using a cable (into the master bus's cable when it follows the master).
    /// </summary>
    Capture,
    /// <summary>Full control: a program sent to its own cable, processed, played on its outputs (<see cref="CableRoute"/>).</summary>
    Cable,
    /// <summary>
    /// Full control: a program sent to its own cable that has Equalizer APO on it; Equalizer APO runs its chain, and the cable's recording
    /// side is only passed on to its outputs (<see cref="CableRoute"/> with no chain of its own).
    /// </summary>
    ApoCable,
    /// <summary>Full control: the master bus, every program that follows the master sent to one cable, processed, played on the master's outputs (<see cref="BusRun"/>).</summary>
    Bus,
    /// <summary>Full control: combined speakers, the cable played on every member (<see cref="FanOutRun"/>).</summary>
    FanOut,
    /// <summary>
    /// A processed microphone (Full control) or a combined microphone (either mode), recorded, mixed, processed, played into its cable
    /// (<see cref="MicRun"/>).
    /// </summary>
    Mic,
}

/// <summary>
/// One run to have: its row key, kind, the cable it uses (playback side), where it plays (null = Windows' default device) and, for
/// a microphone, which recording devices it records. Two specs that are the same need no restart.
/// </summary>
internal sealed record RunSpec(string Key, RunKind Kind, string? Cable, IReadOnlyList<string?> Outputs, IReadOnlyList<string> Sources)
{
    public bool SameAs(RunSpec other) =>
        Kind == other.Kind && Cable == other.Cable && Outputs.SequenceEqual(other.Outputs) && Sources.SequenceEqual(other.Sources);
}

/// <summary>
/// What the planner was given: the mode, what the PC has, the stored settings, which programs have sound streams now
/// (<paramref name="Playing"/>, open even while silent), which of them are heard now (<paramref name="Sounding"/>), which hold a
/// cable run now (<paramref name="Holding"/>), the rows whose run failed (<paramref name="Failed"/>; the master there = the bus
/// was given up because it heard nothing: it then takes no cable and nothing is sent into it until the routing changes), and the
/// programs recorded by a Capture run now (<paramref name="Capturing"/>; null = none).
/// </summary>
internal sealed record PlanInput(
    SoundMode Mode,
    AudioEffectsSupport Support,
    bool CaptureWorks,
    MixerSettings Settings,
    IReadOnlySet<string> Playing,
    IReadOnlySet<string> Sounding,
    IReadOnlySet<string> Holding,
    IReadOnlyList<AudioDevice> Playback,
    IReadOnlyList<AudioDevice> Recording,
    IReadOnlySet<string> Failed,
    IReadOnlySet<string>? Capturing = null)
{
    public bool BusGivenUp => Failed.Contains(MixerService.MasterKey);

    /// <summary>The program has a run now (a cable or a recording), which is kept while it pauses.</summary>
    public bool HasRun(string key) => Holding.Contains(key) || Capturing?.Contains(key) == true;
}

/// <summary>The planner's answer (see <see cref="RoutePlanner.Make"/>).</summary>
internal sealed class RoutePlan
{
    /// <summary>The runs to have, by row key (the master as <see cref="MixerService.MasterKey"/>).</summary>
    public Dictionary<string, RunSpec> Runs { get; } = new();

    /// <summary>Rows that want something the plan can't give now, and why (a missing cable, the wrong mode, waiting for sound).</summary>
    public Dictionary<string, EngineStatus> Blocked { get; } = new();

    /// <summary>Each program's own device when no run holds it (null = Windows' default).</summary>
    public Dictionary<string, string?> Direct { get; } = new();

    /// <summary>Programs the master bus sends to its cable.</summary>
    public List<string> BusRoutes { get; } = new();

    /// <summary>Combined devices and microphones that got a different cable than the one remembered (key -> cable to remember).</summary>
    public Dictionary<string, string> NewCables { get; } = new();

    /// <summary>Every cable a run uses (not offered as a plain "plays on" device).</summary>
    public HashSet<string> CablesInUse { get; } = new();

    /// <summary>The cable a combined device uses now, by its key.</summary>
    public Dictionary<string, string> ComboCables { get; } = new();

    /// <summary>
    /// The rows whose chain goes to Equalizer APO, each limited to its own device: the master (<see cref="MixerService.MasterKey"/>) when
    /// it has effects to run and runs there (always in Fast mode; in Full control when its method is Equalizer APO), every plugged-in
    /// microphone with effects (Fast mode), and each program run on a cable with Equalizer APO on it (<see cref="RunKind.ApoCable"/>,
    /// written for that run's cable).
    /// </summary>
    public List<string> ToApo { get; } = new();

    /// <summary>What the master's method depends on (its Method pop-down reads it), and how the master runs (<see cref="RunMethod.Bus"/> or <see cref="RunMethod.Apo"/>).</summary>
    public MasterFacts Master { get; set; } = MasterFacts.Unknown;
    public RunMethod MasterMethod { get; set; }

    /// <summary>
    /// The real speakers the master's Equalizer APO chain is written for in Full control (null = Windows' default playback device, as in
    /// Fast mode, or none plugged in).
    /// </summary>
    public string? MasterApoDevice { get; set; }

    /// <summary>Why a program can't use Equalizer APO on a cable now (null = a pair with Equalizer APO on it is free).</summary>
    public string? ApoCableWhy { get; set; }

    /// <summary>What the cables are used for besides the master bus (the help table's current row, the master's cable line).</summary>
    public CableUse CableUses { get; set; }

    /// <summary>The live facts of the help table (its "Works" column and current row).</summary>
    public HelpFacts Help { get; set; } = HelpFacts.Unknown;

    /// <summary>How many virtual cable pairs are installed, and whether one is still free after every run took its own (the Method menu offers a cable only then).</summary>
    public int CablePairs { get; set; }
    public bool CableFree { get; set; }

    /// <summary>Full control: the master bus as planned (its cable and the real device the master plays on); null when no bus run is planned.</summary>
    public BusPlan? Bus { get; set; }
}

/// <summary>The master bus a plan runs: its cable (which should be Windows' default playback device) and the real device behind it.</summary>
internal sealed record BusPlan(string Cable, MasterDevice.Choice Real);

/// <summary>
/// Decides from the settings and what the PC has which engine runs exist and where every program plays; pure (no Windows calls, no
/// state), so the whole routing logic is in one place and can be read top to bottom. The master's method comes first
/// (<see cref="SoundModes.MasterMethodOf"/>): in Fast mode and with the master in Equalizer APO its chain goes to Equalizer APO on the
/// speakers (<see cref="RoutePlan.ToApo"/>) and no cable is taken for it. Full control gives each consumer its own cable:
/// first the master bus when the master runs on it (it always wins), then the combined devices and processed microphones that remember a cable (apps point at
/// it), then the other combined devices and microphones, each in the order of the settings, then programs with open streams (heard
/// ones first, <see cref="InCableOrder"/>); a processed program with no free pair, or set to recording, is recorded instead (Capture and
/// replay, the rule in <see cref="PlanFullProgram"/>). Fast mode gives cables to combined microphones only (remembered first). Every run
/// takes a whole cable pair (<see cref="CableBox"/>).
/// </summary>
internal static class RoutePlanner
{
    public static RoutePlan Make(PlanInput p)
    {
        var plan = new RoutePlan();
        var full = p.Mode == SoundModes.Full;
        var cables = new CableBox(p.Support);
        var masterFx = FxSlot.AnyRuns(p.Settings.MasterFxOn, p.Settings.MasterFx);
        var real = RealDevice(p);
        plan.Master = MasterFactsOf(p, cables, real, masterFx, full);
        plan.MasterMethod = SoundModes.MasterMethodOf(SoundModes.MasterRouteOf(p.Settings.MasterMethod), plan.Master);

        if (!full)
        {
            if (masterFx) plan.ToApo.Add(MixerService.MasterKey);
            plan.ToApo.AddRange(PluggedInMics(p).Select(i => MixerService.InputPrefix + i.Id));
            // Combined microphones need only a cable apps can record from, not the bus or Equalizer APO, so they run in Fast mode too;
            // combined speakers are mixed for the master and programs, which Fast mode does not route through a cable.
            var micCombos = p.Settings.Combined.Where(IsMicrophones).ToList();
            TakeRememberedCables(plan, micCombos, cables);
            TakeNewCables(plan, micCombos, cables);
            foreach (var c in p.Settings.Combined)
                if (IsMicrophones(c)) PlanCombined(p, plan, c, cables, busCable: null);
                else plan.Blocked[c.Key] = Unavailable(SoundModes.CombinedNeedsFull, StatusKind.NeedsFull);
            foreach (var app in p.Settings.Apps) PlanFastProgram(p, plan, app);
            plan.ApoCableWhy = ApoCableWhy(p, cables);
            return Finish(p, plan, cables);
        }

        // Full control. The master bus first (Windows' default device is the bus, so every program, effect and route passes through
        // it): a remembered cable never takes the bus's place. Unless the bus was given up: then programs that follow the master play on
        // Windows' default again, which is the real device once more. With the master in Equalizer APO there is no bus: the real
        // speakers stay the default and every cable is free for the others.
        var onBus = plan.MasterMethod == RunMethod.Bus;
        var busCable = onBus && !p.BusGivenUp ? cables.TakeAny() : null;

        var mics = PluggedInMics(p).ToList();
        TakeRememberedCables(plan, p.Settings.Combined, cables);
        var micCables = mics.Where(i => cables.TakeRemembered(i.Cable)).ToDictionary(i => i.Id, i => i.Cable);
        TakeNewCables(plan, p.Settings.Combined, cables);
        foreach (var i in mics.Where(i => !micCables.ContainsKey(i.Id)))
            if (cables.TakeRecordable() is { } cable) Remember(plan, micCables, i.Id, cable, MixerService.InputPrefix + i.Id);

        foreach (var c in p.Settings.Combined) PlanCombined(p, plan, c, cables, busCable);
        foreach (var i in mics) PlanMic(plan, MixerService.InputPrefix + i.Id, micCables.GetValueOrDefault(i.Id), new[] { i.Id }, cables, busCable);
        if (!onBus) PlanMasterApo(plan, real.Id, masterFx);
        else if (!p.BusGivenUp) PlanMaster(p, plan, busCable, cables); // a given-up bus keeps the status of its failure (the engine's)
        // Without the bus, a cable pair that is still Windows' default (the moment after the bus let go, or the user's own choice)
        // carries every program that follows the default: no program run may take it.
        if (!onBus) cables.Reserve(p.Playback.FirstOrDefault(d => d.IsDefault)?.Id);
        plan.ApoCableWhy = ApoCableWhy(p, cables); // what is left for programs
        foreach (var app in InCableOrder(p)) PlanFullProgram(p, plan, app, busCable, cables);
        return Finish(p, plan, cables);
    }

    private static RoutePlan Finish(PlanInput p, RoutePlan plan, CableBox cables)
    {
        plan.CablesInUse.UnionWith(cables.Used);
        plan.CablePairs = cables.Pairs;
        plan.CableFree = cables.AnyFree;
        plan.CableUses = UsesOf(plan.Runs.Values);
        plan.Help = new HelpFacts(plan.Master.Full, plan.MasterMethod, plan.CablePairs, plan.Master.NoCable, plan.Master.ApoWhy, plan.Master.HasEffects,
            p.Support.Cables.Any(c => ApoOn(p, c)), p.Support.Cables.Any(c => c.RecordingId is not null), plan.CableUses,
            p.Support.Found.Count(f => f.IsPair && p.Settings.FreedCables.Contains(f.Id)),
            p.Settings.Combined.Any(IsMicrophones), p.Settings.Combined.Any(c => !IsMicrophones(c)));
        return plan;
    }

    /// <summary>What the runs use cables for (the master bus not counted).</summary>
    private static CableUse UsesOf(IEnumerable<RunSpec> runs) => runs.Aggregate(CableUse.None, (uses, run) => uses | run.Kind switch
    {
        RunKind.Cable => CableUse.Program,
        RunKind.ApoCable => CableUse.ApoProgram,
        RunKind.Mic => CableUse.Microphone,
        RunKind.FanOut => CableUse.Speaker,
        _ => CableUse.None,
    });

    /// <summary>
    /// What decides the master's method: the cables allowed, whether Equalizer APO runs on the master's real speakers (Fast: on Windows'
    /// default device), whether the master is set to several devices, and whether it has effects.
    /// </summary>
    private static MasterFacts MasterFactsOf(PlanInput p, CableBox cables, MasterDevice.Choice real, bool masterFx, bool full)
    {
        var apo = p.Support.Apo;
        var why = full ? SoundModes.ApoMissingOn(apo, apo.PlaybackOf(real.Id)) : SoundModes.ApoMissing(apo);
        var several = p.Settings.MasterPlaysOn.Count > 1 || p.Settings.MasterPlaysOn.Any(CombinedDevice.IsKey);
        return new MasterFacts(full, cables.Pairs, SoundModes.NoCableKind(p.Support), why, several, masterFx);
    }

    /// <summary>
    /// Full control with the master in Equalizer APO: its chain goes to Equalizer APO on the real speakers when it has effects (whether
    /// Equalizer APO is there is the engine's status); the speakers stay Windows' default and nothing is routed for the master.
    /// </summary>
    private static void PlanMasterApo(RoutePlan plan, string? realId, bool masterFx)
    {
        plan.MasterApoDevice = realId;
        if (realId is null)
            plan.Blocked[MixerService.MasterKey] = Unavailable("No speakers are plugged in for the master to play on", StatusKind.NothingPluggedIn);
        else if (masterFx)
            plan.ToApo.Add(MixerService.MasterKey);
        else
            plan.Blocked[MixerService.MasterKey] = new EngineStatus(EngineState.Off, SoundModes.MasterDirect, Kind: StatusKind.Direct, Method: RunMethod.Apo);
    }

    /// <summary>Why a program can't use Equalizer APO on a cable (asked once the others took theirs, before any program did): too few pairs, none with Equalizer APO on it, none free.</summary>
    private static string? ApoCableWhy(PlanInput p, CableBox cables) =>
        SoundModes.ApoCableMissing(cables.Pairs, p.Support.Cables.Any(c => ApoOn(p, c)), cables.HasFree(c => ApoOn(p, c)), SoundModes.NoCableKind(p.Support));

    /// <summary>Equalizer APO runs on the cable's playback side, and the cable has a recording side to pass on (Equalizer APO's output arrives there).</summary>
    private static bool ApoOn(PlanInput p, VirtualCable cable) =>
        cable.RecordingId is not null && p.Support.Apo.PlaybackOf(cable.PlaybackId) is { } endpoint && p.Support.Apo.FolderFor(endpoint) is not null;

    /// <summary>
    /// Programs in the order they get a spare cable: one heard now that already holds one keeps it, then programs heard now, then
    /// silent ones that hold one, then the rest, each group in the order of the settings. So a program that only keeps a silent stream
    /// open (a browser always does) never takes the only spare cable from the one that is playing, and a run is not moved to another
    /// program while both play.
    /// </summary>
    private static IEnumerable<AppAudioEntry> InCableOrder(PlanInput p) =>
        p.Settings.Apps.OrderBy(a => (p.Sounding.Contains(a.Key) ? 0 : 2) + (p.Holding.Contains(a.Key) ? 0 : 1));

    private static bool IsMicrophones(CombinedDevice c) => c.Kind == CombinedKind.Microphones;

    /// <summary>Combined devices that remember a cable keep it while it is free (apps point at it).</summary>
    private static void TakeRememberedCables(RoutePlan plan, IEnumerable<CombinedDevice> combos, CableBox cables)
    {
        foreach (var c in combos.Where(c => cables.TakeRemembered(c.Cable))) plan.ComboCables[c.Key] = c.Cable;
    }

    /// <summary>The other combined devices get a free cable (a combined microphone only a whole pair apps can record from).</summary>
    private static void TakeNewCables(RoutePlan plan, IEnumerable<CombinedDevice> combos, CableBox cables)
    {
        foreach (var c in combos.Where(c => !plan.ComboCables.ContainsKey(c.Key)))
            if ((IsMicrophones(c) ? cables.TakeRecordable() : cables.TakeAny()) is { } cable) Remember(plan, plan.ComboCables, c.Key, cable);
    }

    /// <summary>The microphones with effects to run that are plugged in now (an unplugged one simply has nothing to process).</summary>
    private static IEnumerable<InputFxEntry> PluggedInMics(PlanInput p) =>
        p.Settings.Inputs.Where(i => FxSlot.AnyRuns(i.FxOn, i.Fx) && p.Recording.Any(d => d.Id == i.Id));

    private static void Remember(RoutePlan plan, Dictionary<string, string> into, string id, string cable, string? key = null)
    {
        into[id] = cable;
        plan.NewCables[key ?? id] = cable;
    }

    private static EngineStatus Unavailable(string reason, StatusKind kind) => new(EngineState.Unavailable, reason, Kind: kind);

    /// <summary>A closed program: its effects start when it plays.</summary>
    private static readonly EngineStatus ProgramClosed = new(EngineState.Waiting, "Starts when the program plays", Kind: StatusKind.StartsWhenPlays);

    /// <summary>An open but silent program that waits for sound before it takes a cable.</summary>
    private static readonly EngineStatus WaitingForSound = new(EngineState.Waiting, "Starts when the program plays", Kind: StatusKind.WaitingForSound);

    // ---- Fast ----

    private static void PlanFastProgram(PlanInput p, RoutePlan plan, AppAudioEntry app)
    {
        var outputs = app.PlaysOn.Where(t => p.Playback.Any(d => d.Id == t)).ToList();
        plan.Direct[app.Key] = outputs.FirstOrDefault();
        var wantsRun = FxSlot.AnyRuns(app.FxOn, app.Fx) || outputs.Count > 1;
        if (!wantsRun)
        {
            if (app.PlaysOn.Any(CombinedDevice.IsKey)) plan.Blocked[app.Key] = Unavailable(SoundModes.CombinedNeedsFull, StatusKind.NeedsFull);
            return;
        }
        if (CaptureBlocked(p) is { } blocked) plan.Blocked[app.Key] = blocked;
        else if (!p.Playing.Contains(app.Key)) plan.Blocked[app.Key] = ProgramClosed;
        else plan.Runs[app.Key] = new RunSpec(app.Key, RunKind.Capture, null, outputs.Count > 0 ? outputs.Cast<string?>().ToList() : new string?[] { null }, Array.Empty<string>());
    }

    // ---- Full control ----

    private static void PlanCombined(PlanInput p, RoutePlan plan, CombinedDevice c, CableBox cables, string? busCable)
    {
        var cable = plan.ComboCables.GetValueOrDefault(c.Key);
        if (IsMicrophones(c))
        {
            // Never a cable as a member (it would record OmniControl's own output); unplugged members are left out until they return.
            var sources = c.Members.Where(m => p.Recording.Any(d => d.Id == m) && !cables.IsRecordingSide(m)).ToList();
            PlanMic(plan, c.Key, cable, sources, cables, busCable);
            return;
        }
        if (cable is null)
        {
            plan.Blocked[c.Key] = Unavailable(cables.Missing, cables.MissingKind(program: false));
            return;
        }
        var members = c.Members.Where(m => p.Playback.Any(d => d.Id == m) && m != cable).Cast<string?>().ToList();
        if (members.Count == 0) plan.Blocked[c.Key] = Unavailable("None of its devices is plugged in", StatusKind.NothingPluggedIn);
        else plan.Runs[c.Key] = new RunSpec(c.Key, RunKind.FanOut, cable, members, Array.Empty<string>());
    }

    /// <summary>A processed or combined microphone: recorded, mixed and played into its cable, or why it can't be.</summary>
    /// <param name="busCable">The master bus's cable: when it holds the pair apps record from, the row's status says so.</param>
    private static void PlanMic(RoutePlan plan, string key, string? cable, IReadOnlyList<string> sources, CableBox cables, string? busCable)
    {
        if (cable is null)
            plan.Blocked[key] = new EngineStatus(EngineState.Unavailable, cables.Missing,
                cables.RecordableName(busCable) is { } held ? SoundModes.BusHoldsCable(held) : null, cables.MissingKind(program: false));
        else if (sources.Count == 0) plan.Blocked[key] = Unavailable(SoundModes.NoMicrophonePluggedIn, StatusKind.NothingPluggedIn);
        else plan.Runs[key] = new RunSpec(key, RunKind.Mic, cable, new string?[] { cable }, sources);
    }

    private static void PlanMaster(PlanInput p, RoutePlan plan, string? busCable, CableBox cables)
    {
        if (busCable is null)
        {
            plan.Blocked[MixerService.MasterKey] = Unavailable($"{cables.Missing}. Playing without effects.", cables.MissingKind(program: false));
            return;
        }
        var real = RealDevice(p);
        var outputs = (p.Settings.MasterPlaysOn.Count > 0 ? Resolve(p, plan, p.Settings.MasterPlaysOn) : new[] { real.Id })
            .Where(o => o is not null && o != busCable).ToList();
        if (outputs.Count == 0)
        {
            // Never play the bus into its own cable (a loop); with no real device plugged in there is nowhere to play.
            plan.Blocked[MixerService.MasterKey] = Unavailable("No speakers are plugged in for the master to play on", StatusKind.NothingPluggedIn);
            return;
        }
        plan.Runs[MixerService.MasterKey] = new RunSpec(MixerService.MasterKey, RunKind.Bus, busCable, outputs, Array.Empty<string>());
        plan.Bus = new BusPlan(busCable, real);
    }

    private static MasterDevice.Choice RealDevice(PlanInput p) =>
        MasterDevice.Real(p.Playback, p.Playback.FirstOrDefault(d => d.IsDefault)?.Id, p.Settings.RealMasterDevice);

    private static void PlanFullProgram(PlanInput p, RoutePlan plan, AppAudioEntry app, string? busCable, CableBox cables)
    {
        var outputs = Resolve(p, plan, app.PlaysOn).ToList();
        plan.Direct[app.Key] = outputs.Count == 1 ? outputs[0] : null;
        // A combined device chosen earlier whose cable is gone plays nowhere: the program follows the master instead, and its row says so.
        if (app.PlaysOn.FirstOrDefault(t => CombinedDevice.IsKey(t) && !plan.ComboCables.ContainsKey(t)) is { } lost
            && p.Settings.Combined.FirstOrDefault(c => c.Key == lost) is { } lostDevice)
            plan.Blocked[app.Key] = new EngineStatus(EngineState.Unavailable, $"{lostDevice.Name} has no cable: {cables.Missing.ToLowerInvariant()}. Playing on the master instead.",
                Kind: cables.MissingKind(program: false));
        // A failed run keeps its failure as the row's status (the engine's) and gives its cable to the next program; the program plays
        // where Windows' default sends it, which is the master bus, without being moved again (that may be what failed).
        if (p.Failed.Contains(app.Key)) return;
        var playing = p.Playing.Contains(app.Key);
        if (!FxSlot.AnyRuns(app.FxOn, app.Fx) && outputs.Count <= 1)
        {
            if (outputs.Count == 0 && busCable is not null && playing) plan.BusRoutes.Add(app.Key);
            return;
        }
        // A closed program has nothing to run (a run it already has is kept, so it is ready again the moment the program reopens).
        var heardOrHeld = p.Sounding.Contains(app.Key) || p.HasRun(app.Key);
        if (!playing && !heardOrHeld)
        {
            plan.Blocked[app.Key] = ProgramClosed;
            return;
        }
        // Follows the master: into the master bus, or, with no bus, the real speakers by name: "Windows' default" could still be a
        // cable this run records (when giving the default back failed), which would be a loop.
        if (outputs.Count == 0 && (busCable ?? RealDevice(p).Id) is { } follow) outputs.Add(follow);
        if (outputs.Count == 0)
        {
            plan.Blocked[app.Key] = Unavailable("No speakers are plugged in to play on", StatusKind.NothingPluggedIn);
            return;
        }
        // THE RULE for how a processed program runs (SoundModes.ProgramRoutes, the user's choice per program):
        // - Virtual cable: its own cable pair, or it waits for one.
        // - Recording: always Capture and replay (process loopback of the program, its original turned down), never a cable.
        // - Automatic: a cable while a whole pair is free (the preferred path: no recording, Windows mixes the program itself), else
        //   Capture and replay; a run already recording keeps recording while it plays or pauses (switching would move the program mid-play).
        // - An open program that is silent and has no run yet: a recording starts now, armed (its original turned down, the replay
        //   idle), so its effects apply from its first sound; it needs no cable. A cable is taken only once it is heard: a browser keeps
        //   a silent session open all the time, which must not take the cable from the program that plays.
        // - Equalizer APO on a cable: like Virtual cable, but only a pair with Equalizer APO on it; Equalizer APO runs the chain and the
        //   run only passes the cable's recording side on (no processing of its own).
        var route = SoundModes.RouteOf(app.FxRoute);
        var apo = route == SoundModes.RouteApoCable;
        var strict = route == SoundModes.RouteCable || apo; // a cable or nothing
        var record = route == SoundModes.RouteCapture || route == SoundModes.RouteAutomatic && p.Capturing?.Contains(app.Key) == true;
        // Never the cable it plays on itself (a loop).
        bool Fits(VirtualCable c) => !outputs.Contains(c.PlaybackId) && (!apo || ApoOn(p, c));
        if (!heardOrHeld)
        {
            // A program set to a cable says at once that none is free, silent or not: its choice is never left to fail unseen later.
            if (strict && !cables.HasFree(Fits))
            {
                plan.Blocked[app.Key] = NoCableFor(apo, plan, cables);
                return;
            }
            if (!record && (strict || cables.HasFree(Fits)))
            {
                plan.Blocked[app.Key] = WaitingForSound;
                return;
            }
            record = true;
        }
        if (!record && cables.TakeAny(Fits) is { } cable)
        {
            // Equalizer APO's output arrives at the cable's recording side, which the run passes on (ApoOn checked it exists).
            var sources = apo ? new[] { p.Support.Cables.First(c => c.PlaybackId == cable).RecordingId! } : Array.Empty<string>();
            plan.Runs[app.Key] = new RunSpec(app.Key, apo ? RunKind.ApoCable : RunKind.Cable, cable, outputs, sources);
            if (apo) plan.ToApo.Add(app.Key);
            return;
        }
        if (strict)
            plan.Blocked[app.Key] = NoCableFor(apo, plan, cables);
        else if (CaptureBlocked(p) is { } blocked)
            plan.Blocked[app.Key] = blocked;
        else
            // The recording is of the program's own processes, never of a device, so playing into the bus's cable is no loop.
            plan.Runs[app.Key] = new RunSpec(app.Key, RunKind.Capture, null, outputs, Array.Empty<string>());
    }

    /// <summary>Why a program set to a cable (Virtual cable, or Equalizer APO on a cable) has none: what is missing, and that it plays without effects.</summary>
    private static EngineStatus NoCableFor(bool apo, RoutePlan plan, CableBox cables)
    {
        if (!apo || plan.ApoCableWhy is not { } why) return Unavailable($"{cables.Missing}. Playing without effects.", cables.MissingKind(program: true));
        var kind = cables.Pairs == 0 ? cables.MissingKind(program: true)
            : why == SoundModes.NeedsSecondCable ? StatusKind.NeedsAnotherCable : cables.AnyFree ? StatusKind.NeedsApo : StatusKind.NoFreeCable;
        return Unavailable($"{SoundModes.Capitalised(why)}. Playing without effects.", kind);
    }

    /// <summary>Why Capture and replay can't run on this PC (old Windows, or the last Test found no way to silence the original), else null.</summary>
    private static EngineStatus? CaptureBlocked(PlanInput p) => p.CaptureWorks ? null
        : Unavailable($"{SoundModes.CaptureMissing(new SoundFacts(p.Support, p.CaptureWorks, true))}. Playing without effects.", StatusKind.CannotRecord);

    /// <summary>The playback endpoints of "plays on" targets that can be used now: plugged-in devices, and combined devices with a cable.</summary>
    private static IEnumerable<string?> Resolve(PlanInput p, RoutePlan plan, IEnumerable<string> targets) =>
        targets.Select(t => CombinedDevice.IsKey(t) ? plan.ComboCables.GetValueOrDefault(t) : p.Playback.Any(d => d.Id == t) ? t : null)
            .Where(t => t is not null).Distinct();
}
