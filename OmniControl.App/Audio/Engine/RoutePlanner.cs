using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>The kinds of engine runs (what <see cref="EffectsEngine"/> starts for a row).</summary>
internal enum RunKind
{
    /// <summary>Fast: a program recorded, its original silenced, the processed copy played (<see cref="ProgramEngine"/>).</summary>
    Capture,
    /// <summary>Full control: a program sent to its own cable, processed, played on its outputs (<see cref="CableRoute"/>).</summary>
    Cable,
    /// <summary>Full control: the master bus, every program that follows the master sent to one cable, processed, played on the master's outputs (<see cref="BusRun"/>).</summary>
    Bus,
    /// <summary>Full control: combined speakers, the cable played on every member (<see cref="FanOutRun"/>).</summary>
    FanOut,
    /// <summary>Full control: a processed or combined microphone, recorded, mixed, processed, played into its cable (<see cref="MicRun"/>).</summary>
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

/// <summary>What the planner was given: the mode, what the PC has, the stored settings and which programs have sound streams now.</summary>
internal sealed record PlanInput(
    SoundMode Mode,
    AudioEffectsSupport Support,
    bool CaptureWorks,
    MixerSettings Settings,
    IReadOnlySet<string> Playing,
    IReadOnlyList<AudioDevice> Playback,
    IReadOnlyList<AudioDevice> Recording);

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
    /// Fast mode: the rows whose chain goes to Equalizer APO, each limited to its own device: the master (<see cref="MixerService.MasterKey"/>)
    /// when it has effects to run, and every plugged-in microphone (input key) that has. Empty in Full control.
    /// </summary>
    public List<string> ToApo { get; } = new();
}

/// <summary>
/// Decides from the settings and what the PC has which engine runs exist and where every program plays; pure (no Windows calls, no
/// state), so the whole routing logic is in one place and can be read top to bottom. Full control gives each consumer its own cable:
/// first the combined devices and processed microphones that remember a cable (apps point at it), then the master bus, then the
/// other combined devices and microphones, then playing programs, each in the order of the settings.
/// </summary>
internal static class RoutePlanner
{
    public static RoutePlan Make(PlanInput p)
    {
        var plan = new RoutePlan();
        var full = p.Mode == SoundModes.Full;
        var cables = new CableBox(p.Support.Cables);
        var masterFx = p.Settings.MasterFxOn && p.Settings.MasterFx.Any(s => s.Enabled);

        if (!full)
        {
            if (masterFx) plan.ToApo.Add(MixerService.MasterKey);
            plan.ToApo.AddRange(PluggedInMics(p).Select(i => MixerService.InputPrefix + i.Id));
            foreach (var c in p.Settings.Combined) plan.Blocked[c.Key] = Unavailable(SoundModes.CombinedNeedsFull);
            foreach (var app in p.Settings.Apps) PlanFastProgram(p, plan, app);
            return plan;
        }

        // Full control: cables first, in the order above.
        var mics = PluggedInMics(p).ToList();
        foreach (var c in p.Settings.Combined.Where(c => cables.TakeRemembered(c.Cable))) plan.ComboCables[c.Key] = c.Cable;
        var micCables = mics.Where(i => cables.TakeRemembered(i.Cable)).ToDictionary(i => i.Id, i => i.Cable);

        var busNeeded = masterFx || p.Settings.MasterPlaysOn.Count > 1;
        var busCable = busNeeded ? cables.TakeAny() : null;

        foreach (var c in p.Settings.Combined.Where(c => !plan.ComboCables.ContainsKey(c.Key)))
            if (cables.TakeAny() is { } cable) Remember(plan, plan.ComboCables, c.Key, cable);
        foreach (var i in mics.Where(i => !micCables.ContainsKey(i.Id)))
            if (cables.TakeAny() is { } cable) Remember(plan, micCables, i.Id, cable, MixerService.InputPrefix + i.Id);

        foreach (var c in p.Settings.Combined) PlanCombined(p, plan, c, cables);
        foreach (var i in mics) PlanMic(plan, MixerService.InputPrefix + i.Id, micCables.GetValueOrDefault(i.Id), new[] { i.Id }, cables);
        PlanMaster(p, plan, busNeeded, busCable, cables);
        foreach (var app in p.Settings.Apps) PlanFullProgram(p, plan, app, busCable, cables);
        plan.CablesInUse.UnionWith(cables.Used);
        return plan;
    }

    private static bool Active(bool on, List<FxSlot> chain) => on && chain.Any(s => s.Enabled);

    /// <summary>The microphones with effects to run that are plugged in now (an unplugged one simply has nothing to process).</summary>
    private static IEnumerable<InputFxEntry> PluggedInMics(PlanInput p) =>
        p.Settings.Inputs.Where(i => Active(i.FxOn, i.Fx) && p.Recording.Any(d => d.Id == i.Id));

    private static void Remember(RoutePlan plan, Dictionary<string, string> into, string id, string cable, string? key = null)
    {
        into[id] = cable;
        plan.NewCables[key ?? id] = cable;
    }

    private static EngineStatus Unavailable(string reason) => new(EngineState.Unavailable, reason);

    private static readonly EngineStatus WaitingForSound = new(EngineState.Waiting, "Starts when the program plays");

    // ---- Fast ----

    private static void PlanFastProgram(PlanInput p, RoutePlan plan, AppAudioEntry app)
    {
        var outputs = app.PlaysOn.Where(t => p.Playback.Any(d => d.Id == t)).ToList();
        plan.Direct[app.Key] = outputs.FirstOrDefault();
        var wantsRun = Active(app.FxOn, app.Fx) || outputs.Count > 1;
        if (!wantsRun)
        {
            if (app.PlaysOn.Any(CombinedDevice.IsKey)) plan.Blocked[app.Key] = Unavailable(SoundModes.CombinedNeedsFull);
            return;
        }
        if (!p.CaptureWorks)
            plan.Blocked[app.Key] = Unavailable($"{SoundModes.CaptureMissing(new SoundFacts(p.Support, p.CaptureWorks, true))}. Playing without effects.");
        else if (!p.Playing.Contains(app.Key)) plan.Blocked[app.Key] = WaitingForSound;
        else plan.Runs[app.Key] = new RunSpec(app.Key, RunKind.Capture, null, outputs.Count > 0 ? outputs.Cast<string?>().ToList() : new string?[] { null }, Array.Empty<string>());
    }

    // ---- Full control ----

    private static void PlanCombined(PlanInput p, RoutePlan plan, CombinedDevice c, CableBox cables)
    {
        if (!plan.ComboCables.TryGetValue(c.Key, out var cable))
        {
            plan.Blocked[c.Key] = Unavailable(cables.Missing);
            return;
        }
        if (c.Kind == CombinedKind.Microphones)
        {
            PlanMic(plan, c.Key, cable, c.Members.Where(m => p.Recording.Any(d => d.Id == m)).ToList(), cables);
            return;
        }
        var members = c.Members.Where(m => p.Playback.Any(d => d.Id == m) && m != cable).Cast<string?>().ToList();
        if (members.Count == 0) plan.Blocked[c.Key] = Unavailable("None of its devices is plugged in");
        else plan.Runs[c.Key] = new RunSpec(c.Key, RunKind.FanOut, cable, members, Array.Empty<string>());
    }

    private static void PlanMic(RoutePlan plan, string key, string? cable, IReadOnlyList<string> sources, CableBox cables)
    {
        if (cable is null) plan.Blocked[key] = Unavailable(cables.Missing);
        else if (sources.Count == 0) plan.Blocked[key] = Unavailable("None of its microphones is plugged in");
        else plan.Runs[key] = new RunSpec(key, RunKind.Mic, cable, new string?[] { cable }, sources);
    }

    private static void PlanMaster(PlanInput p, RoutePlan plan, bool busNeeded, string? busCable, CableBox cables)
    {
        if (!busNeeded) return;
        if (busCable is null)
        {
            plan.Blocked[MixerService.MasterKey] = Unavailable($"{cables.Missing}. Playing without effects.");
            return;
        }
        var outputs = (p.Settings.MasterPlaysOn.Count > 1 ? Resolve(p, plan, p.Settings.MasterPlaysOn) : new[] { p.Support.DefaultDeviceId })
            .Where(o => o is not null && o != busCable).ToList();
        if (outputs.Count == 0)
        {
            // Never play the bus into its own cable (a loop): that happens when the cable itself is Windows' default device.
            plan.Blocked[MixerService.MasterKey] = Unavailable("Windows' default device is a virtual cable; choose real speakers for the master");
            return;
        }
        plan.Runs[MixerService.MasterKey] = new RunSpec(MixerService.MasterKey, RunKind.Bus, busCable, outputs, Array.Empty<string>());
    }

    private static void PlanFullProgram(PlanInput p, RoutePlan plan, AppAudioEntry app, string? busCable, CableBox cables)
    {
        var outputs = Resolve(p, plan, app.PlaysOn).ToList();
        plan.Direct[app.Key] = outputs.Count == 1 ? outputs[0] : null;
        var playing = p.Playing.Contains(app.Key);
        if (!Active(app.FxOn, app.Fx) && outputs.Count <= 1)
        {
            if (outputs.Count == 0 && busCable is not null && playing) plan.BusRoutes.Add(app.Key);
            return;
        }
        if (!playing)
        {
            plan.Blocked[app.Key] = WaitingForSound;
            return;
        }
        if (cables.TakeAny() is not { } cable)
        {
            plan.Blocked[app.Key] = Unavailable($"{cables.Missing}. Playing without effects.");
            return;
        }
        if (outputs.Count == 0) outputs.Add(busCable); // follows the master: into the master bus, or Windows' default when there is none
        plan.Runs[app.Key] = new RunSpec(app.Key, RunKind.Cable, cable, outputs, Array.Empty<string>());
    }

    /// <summary>The playback endpoints of "plays on" targets that can be used now: plugged-in devices, and combined devices with a cable.</summary>
    private static IEnumerable<string?> Resolve(PlanInput p, RoutePlan plan, IEnumerable<string> targets) =>
        targets.Select(t => CombinedDevice.IsKey(t) ? plan.ComboCables.GetValueOrDefault(t) : p.Playback.Any(d => d.Id == t) ? t : null)
            .Where(t => t is not null).Distinct();

    /// <summary>The installed cables not given out yet.</summary>
    private sealed class CableBox
    {
        private readonly List<string> _free;
        private readonly bool _none;

        public CableBox(IReadOnlyList<VirtualCable> cables)
        {
            _free = cables.Select(c => c.PlaybackId).ToList();
            _none = _free.Count == 0;
        }

        public List<string> Used { get; } = new();

        /// <summary>Why a consumer got no cable.</summary>
        public string Missing => _none ? SoundModes.NeedsCable : SoundModes.NeedsAnotherCable;

        public bool TakeRemembered(string cable) => cable.Length > 0 && Take(cable);

        public string? TakeAny() => _free.Count > 0 && Take(_free[0]) ? Used[^1] : null;

        private bool Take(string cable)
        {
            if (!_free.Remove(cable)) return false;
            Used.Add(cable);
            return true;
        }
    }
}
