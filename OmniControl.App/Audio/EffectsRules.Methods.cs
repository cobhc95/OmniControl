namespace OmniControl.App.Audio;

/// <summary>
/// One way a row's effects can run (an entry of <see cref="EffectsRules.ProgramRoutes"/> or <see cref="EffectsRules.MasterRoutes"/>): its
/// stored id (<see cref="AppAudioEntry.FxRoute"/>, <see cref="MixerSettings.MasterMethod"/>; never renamed), name, one plain line for
/// the Method pop-down and its tooltip.
/// </summary>
public sealed record EffectsRoute(string Id, string Name, string Description, string Tip);

/// <summary>
/// What the master's Method pop-down and the planner need to know about the master on this PC (the planner makes it,
/// <see cref="Engine.RoutePlanner"/>).
/// </summary>
/// <param name="CablePairs">How many cable pairs OmniControl may use.</param>
/// <param name="NoCable">Why no cable can be given out when <paramref name="CablePairs"/> is 0 (<see cref="EffectsRules.NoCableKind"/>).</param>
/// <param name="ApoWhy">Why Equalizer APO can't run on the master's real speakers (null = it can).</param>
/// <param name="SeveralOutputs">The master is set to play on several devices or a combined device (only the cable bus does that).</param>
/// <param name="HasEffects">The master has effects to run.</param>
public sealed record MasterFacts(int CablePairs, StatusKind NoCable, string? ApoWhy, bool SeveralOutputs, bool HasEffects)
{
    public static readonly MasterFacts Unknown = new(0, StatusKind.NeedsCable, null, false, false);
}

/// <summary>
/// How effects run per row: a program's method (<see cref="ProgramRoutes"/>) and the master's
/// (<see cref="MasterRoutes"/>), the rule that resolves the master's Automatic, and each entry's live availability in the Method
/// pop-downs (<see cref="OfferOf"/>, <see cref="MasterOfferOf"/>). The planner applies them (<see cref="Engine.RoutePlanner"/>).
/// </summary>
public static partial class EffectsRules
{
    // ---- a program's method ----

    /// <summary>
    /// The default for a program: its own virtual cable while a whole cable pair is free, else Capture and replay (the rule
    /// itself is in <see cref="Engine.RoutePlanner"/>).
    /// </summary>
    public static readonly EffectsRoute RouteAutomatic = new("", "Automatic",
        "A cable if one is free, else recording.",
        "Uses a free virtual cable; with none free the program is recorded instead, so its effects still run.");

    /// <summary>Always through its own virtual cable; with none free its effects wait.</summary>
    public static readonly EffectsRoute RouteCable = new("cable", "Virtual cable",
        "Through its own virtual cable.",
        "Windows sends the program to a free virtual cable and OmniControl processes that. Needs a cable pair nobody else uses.");

    /// <summary>Always recorded (process loopback), never a cable.</summary>
    public static readonly EffectsRoute RouteCapture = new("capture", "Recording",
        "Recorded and replayed; no cable.",
        "OmniControl records the program, turns its own sound down to near silence while it is open (0 in Windows' Volume mixer) and plays the processed copy. Adds a little delay.");

    /// <summary>
    /// Its own cable with Equalizer APO on that cable's playback side: Equalizer APO runs the program's chain in Windows, OmniControl
    /// only passes the cable's sound on to where the program plays (no processing of its own).
    /// </summary>
    public static readonly EffectsRoute RouteApoCable = new("apo-cable", "Equalizer APO on a cable",
        "Its own cable; Equalizer APO runs the effects.",
        "Windows sends the program to a cable with Equalizer APO on it (ticked for that cable in Equalizer APO's Device Selector); Equalizer APO runs the equaliser, gain, IR loader and VST2 plug-ins, VST3 plug-ins are kept but not run, and OmniControl passes the cable's sound on to the speakers. Needs a second cable pair. Your own settings in Equalizer APO's config.txt that are not limited to a device would apply on that cable too.");

    /// <summary>Every way a program's effects can run, in the order a picker lists them.</summary>
    public static IReadOnlyList<EffectsRoute> ProgramRoutes { get; } = new[] { RouteAutomatic, RouteCable, RouteCapture, RouteApoCable };

    /// <summary>The route stored for a program (an unknown or empty id = <see cref="RouteAutomatic"/>).</summary>
    public static EffectsRoute RouteOf(string? id) => ProgramRoutes.FirstOrDefault(r => r.Id == id) ?? RouteAutomatic;

    /// <summary>A program run's running words: which way its effects run (the long form; the Mixer row shows <see cref="Brief"/>).</summary>
    public const string RunningByRecording = "Effects running by recording", RunningThroughCable = "Effects running through a cable",
        RunningInApoOnCable = "Chain sent to Equalizer APO on its cable, passed on to the speakers";

    /// <summary>Why a program can't use Equalizer APO on a cable now, or null when it can.</summary>
    /// <param name="pairs">Cable pairs OmniControl may use.</param>
    /// <param name="apoOnACable">Equalizer APO runs on at least one allowed cable's playback side.</param>
    /// <param name="free">A whole pair with Equalizer APO on it is free.</param>
    /// <param name="noCable">Why there is no pair at all when <paramref name="pairs"/> is 0 (<see cref="NoCableKind"/>).</param>
    public static string? ApoCableMissing(int pairs, bool apoOnACable, bool free, StatusKind noCable = StatusKind.Other) =>
        pairs == 0 && noCable is StatusKind.NeedsCable or StatusKind.CablesOff ? Brief(noCable)
        : pairs < 2 ? NeedsSecondCable
        : !apoOnACable ? ApoNotOnCable
        : !free ? "the cable with Equalizer APO is in use"
        : null;

    /// <summary>Why no cable can run Equalizer APO: it is not ticked on one in its Device Selector (what "Choose devices" fixes).</summary>
    public const string ApoNotOnCable = "Equalizer APO is not on a cable";

    /// <summary>A setup that needs two independent cable pairs (a program's Equalizer APO on a cable).</summary>
    public const string NeedsSecondCable = "needs a second cable pair";

    // ---- the master's method ----

    /// <summary>The default for the master: the cable bus while a cable pair is allowed, otherwise Equalizer APO on the speakers.</summary>
    public static readonly EffectsRoute MasterAutomatic = new("", "Automatic",
        "Cable bus if a cable is allowed, else Equalizer APO.",
        "Uses the cable bus whenever a virtual cable is allowed (the master can play on several speakers); with none, or when only Equalizer APO can run the master, Equalizer APO on the speakers.");

    /// <summary>The master through the cable bus: its cable is Windows' default device, OmniControl's engine runs the chain.</summary>
    public static readonly EffectsRoute MasterBus = new("bus", "Cable bus",
        "Through a cable: any effects, several speakers.",
        "The master's cable becomes Windows' default device; OmniControl records it, runs the master's effects (VST3 too) and plays the result on the speakers you choose. Adds a little delay and holds one cable pair.");

    /// <summary>The master in Equalizer APO on the real speakers: the cable stays free and Windows' default is the speakers.</summary>
    public static readonly EffectsRoute MasterApo = new("apo", "Equalizer APO",
        "In Windows: frees the cable. No VST3, one speaker.",
        "The master's effects run in Equalizer APO on the real speakers, which stay Windows' default device, so the volume keys work as usual and the cable is free for a program, a combined microphone or a combined speaker. Equalizer APO runs the equaliser, gain, IR loader and VST2 plug-ins; VST3 plug-ins are kept but not run. If the master has no effects, nothing needs installing.");

    /// <summary>Every way the master can run, in the order its pop-down lists them.</summary>
    public static IReadOnlyList<EffectsRoute> MasterRoutes { get; } = new[] { MasterAutomatic, MasterBus, MasterApo };

    /// <summary>The master's stored method (an unknown or empty id = <see cref="MasterAutomatic"/>).</summary>
    public static EffectsRoute MasterRouteOf(string? id) => MasterRoutes.FirstOrDefault(r => r.Id == id) ?? MasterAutomatic;

    /// <summary>
    /// How the master runs: as chosen; Automatic = the cable bus while a cable pair is
    /// allowed, the master plays on several devices, or Equalizer APO can't run it (then the bus's own status says what is missing),
    /// otherwise Equalizer APO.
    /// </summary>
    public static RunMethod MasterMethodOf(EffectsRoute chosen, MasterFacts facts)
    {
        if (chosen == MasterBus) return RunMethod.Bus;
        if (chosen == MasterApo) return RunMethod.Apo;
        return facts.CablePairs > 0 || facts.SeveralOutputs || facts.ApoWhy is not null && facts.HasEffects ? RunMethod.Bus : RunMethod.Apo;
    }

    /// <summary>How the master's Method pop-down entry for <paramref name="route"/> stands now (a disabled entry cannot be chosen; its tip says why).</summary>
    public static RouteOffer MasterOfferOf(EffectsRoute route, MasterFacts facts, RunMethod current)
    {
        if (route == MasterBus)
            return facts.CablePairs == 0 ? Unavailable(route, Brief(facts.NoCable)) : new RouteOffer(true, route.Description, route.Tip);
        if (route == MasterApo)
        {
            if (facts.SeveralOutputs) return Unavailable(route, "the master plays on several devices");
            if (facts.ApoWhy is { } why)
                return facts.HasEffects ? Unavailable(route, why) : new RouteOffer(true, NothingToInstall, $"{route.Tip} {why}.");
            return new RouteOffer(true, route.Description, route.Tip);
        }
        return new RouteOffer(true, current == RunMethod.Apo ? "Currently: Equalizer APO" : "Currently: cable bus", route.Tip);
    }

    /// <summary>Equalizer APO for a master with no effects: nothing runs, so nothing is missing.</summary>
    private const string NothingToInstall = "The master has no effects now, so nothing needs installing.";

    private static RouteOffer Unavailable(EffectsRoute route, string why)
    {
        var reason = Lowered(why);
        return new RouteOffer(false, $"Not available: {reason}", $"{route.Name} is not available: {reason}.");
    }

    /// <summary>The master in Equalizer APO with no effects: it plays straight on the speakers (the row's status and its tooltip).</summary>
    public const string MasterDirect = "No master effects: it plays straight on the speakers";

    /// <summary>Why the master's devices are one choice while it runs in Equalizer APO (the Wiring cell's note).</summary>
    public const string ApoOneSpeaker = "Equalizer APO plays on one speaker. Use Cable bus for several.";

    /// <summary>The master's cable line in its tooltip: what the cable is used for now.</summary>
    public static string MasterCableLine(RunMethod master, CableUse uses) =>
        master == RunMethod.Bus ? "Cable: the master bus" : $"Cable: {CableUseWords(uses)}";

    private static string CableUseWords(CableUse uses)
    {
        var words = new List<string>();
        if (uses.HasFlag(CableUse.Program)) words.Add("a program");
        if (uses.HasFlag(CableUse.ApoProgram)) words.Add("a program with Equalizer APO");
        if (uses.HasFlag(CableUse.Microphone)) words.Add("a microphone");
        if (uses.HasFlag(CableUse.Speaker)) words.Add("a combined speaker");
        return words.Count == 0 ? "free" : string.Join(", ", words);
    }
}

/// <summary>What OmniControl's runs use the cables for now (besides the master bus): the master's cable line.</summary>
[Flags]
public enum CableUse
{
    None = 0,
    /// <summary>A program on its own cable.</summary>
    Program = 1,
    /// <summary>A program on a cable with Equalizer APO on it.</summary>
    ApoProgram = 2,
    /// <summary>A combined or processed microphone.</summary>
    Microphone = 4,
    /// <summary>Combined speakers.</summary>
    Speaker = 8,
}
