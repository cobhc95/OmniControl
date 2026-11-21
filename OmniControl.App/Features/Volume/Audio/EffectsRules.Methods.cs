namespace OmniControl.App.Features.Volume.Audio;

/// One way a row's effects can run (an entry of <see cref="EffectsRules.ProgramRoutes"/> or <see cref="EffectsRules.MasterRoutes"/>): its
/// stored id (<see cref="AppAudioEntry.FxRoute"/>, <see cref="MixerSettings.MasterMethod"/>, <see cref="InputFxEntry.Method"/>; never renamed
/// except Automatic's, from empty to "auto" when Off took the empty id, <see cref="MixerSettings.UpgradeMethods"/>), name, one plain line
/// for the Method pop-down and its tooltip.
public sealed record EffectsRoute(string Id, string Name, string Description, string Tip);

/// What the master's or a microphone's Method pop-down and the planner need to know about that device on this PC (the planner makes it,
/// <see cref="Engine.RoutePlanner"/>).
/// <param name="CablePairs">How many cable pairs OmniControl may use.</param>
/// <param name="NoCable">Why no cable can be given out when <paramref name="CablePairs"/> is 0 (<see cref="EffectsRules.NoCableKind"/>).</param>
/// <param name="ApoWhy">Why Equalizer APO can't run on the master's real speakers or the microphone (null = it can).</param>
/// <param name="SeveralOutputs">The master is set to play on several devices or a combined device (only the cable bus does that).</param>
/// <param name="HasEffects">The master or microphone has effects to run.</param>
public sealed record MasterFacts(int CablePairs, StatusKind NoCable, string? ApoWhy, bool SeveralOutputs, bool HasEffects)
{
    public static readonly MasterFacts Unknown = new(0, StatusKind.NeedsCable, null, false, false);

    /// Where Equalizer APO stands on the master's speakers (<see cref="EffectsRules.ApoNowOn"/>); null = not read yet.
    public string? ApoNow { get; init; }

    /// The same as a state (<see cref="EffectsRules.ApoStandOn"/>): the master's chip and the Automatic card read it.
    public ApoStand ApoStand { get; init; } = ApoStand.Running;

    /// The Equalizer APO card's facts and label (<see cref="EffectsRules.ApoCardOf"/>); null = not read yet.
    public (string Facts, string Label)? ApoCard { get; init; }
}

/// How effects run per row: a program's method (<see cref="ProgramRoutes"/>) and the master's
/// (<see cref="MasterRoutes"/>), the rule that resolves the master's Automatic, and each entry's live availability in the Method
/// pop-downs (<see cref="OfferOf"/>, <see cref="MasterOfferOf"/>). The planner applies them (<see cref="Engine.RoutePlanner"/>).
public static partial class EffectsRules
{
    // ---- Off: every row's default ----

    /// The default of every row (master, program, microphone): OmniControl routes and processes nothing for it, the sound goes straight to
    /// its device and its effects (kept) do not run until another method is picked. First in every Method pop-down.
    public static readonly EffectsRoute MethodOff = new("", "Off — no effects",
        "Nothing routed; sound goes straight to its device.",
        "OmniControl leaves this sound alone: no cable, no recording, no Equalizer APO. Pick another method to use effects; effects already added are kept.");

    /// The line under a Method chip set to Off, its short status and its full line (the tooltips, the FX window).
    public const string MethodOffFact = "pick a method to use effects", MethodOffWord = "Off",
        MethodOffLine = "Method Off: nothing is routed or processed. Pick a method to use effects";

    // ---- a program's method ----

    /// A program's Automatic: its own virtual cable while a whole cable pair is free, else Capture and replay (the rule
    /// itself is in <see cref="Engine.RoutePlanner"/>).
    public static readonly EffectsRoute RouteAutomatic = new(AutomaticId, "Automatic",
        "A cable if one is free, else recording.",
        "Uses a free virtual cable; with none free the program is recorded instead, so its effects still run.");

    /// Always through its own virtual cable; with none free its effects wait.
    public static readonly EffectsRoute RouteCable = new("cable", "Virtual cable",
        "Through its own virtual cable.",
        "Windows sends the program to a free virtual cable and OmniControl processes that. Needs a cable pair nobody else uses.");

    /// Always recorded (process loopback), never a cable.
    public static readonly EffectsRoute RouteCapture = new("capture", "Recording",
        "Recorded and replayed; no cable.",
        "OmniControl records the program, turns its own sound down to near silence while it is open (0 in Windows' Volume mixer) and plays the processed copy. Adds a little delay.");

    /// Its own cable with Equalizer APO on that cable's playback side: Equalizer APO runs the program's chain in Windows, OmniControl
    /// only passes the cable's sound on to where the program plays (no processing of its own).
    public static readonly EffectsRoute RouteApoCable = new("apo-cable", "Equalizer APO on a cable",
        "Its own cable; Equalizer APO runs the effects.",
        "Windows sends the program to a cable with Equalizer APO on it (ticked for that cable in Equalizer APO's Device Selector); Equalizer APO runs the equaliser, gain, IR loader and VST2 plug-ins, VST3 plug-ins are kept but not run, and OmniControl passes the cable's sound on to the speakers. Needs a second cable pair. Your own settings in Equalizer APO's config.txt that are not limited to a device would apply on that cable too.");

    /// Every way a program's effects can run, in the order a picker lists them.
    public static IReadOnlyList<EffectsRoute> ProgramRoutes { get; } = new[] { MethodOff, RouteAutomatic, RouteCable, RouteCapture, RouteApoCable };

    /// The route stored for a program (an unknown or empty id = <see cref="MethodOff"/>).
    public static EffectsRoute RouteOf(string? id) => ProgramRoutes.FirstOrDefault(r => r.Id == id) ?? MethodOff;

    /// Automatic's stored id, the same for the master, programs and microphones.
    internal const string AutomaticId = "auto";

    /// A program run's running words: which way its effects run (the long form; the Mixer row shows <see cref="Brief"/>).
    public const string RunningByRecording = "Effects running by recording", RunningThroughCable = "Effects running through a cable",
        RunningInApoOnCable = "Chain sent to Equalizer APO on its cable, passed on to the speakers";

    /// Why a program can't use Equalizer APO on a cable now, or null when it can.
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

    /// Why no cable can run Equalizer APO: it is not ticked on one in its Device Selector (what "Choose devices" fixes).
    public const string ApoNotOnCable = "Equalizer APO is not on a cable";

    /// A setup that needs two independent cable pairs (a program's Equalizer APO on a cable).
    public const string NeedsSecondCable = "needs a second cable pair";

    // ---- the master's method ----

    /// The master's Automatic: nothing routed without effects; with effects the cable bus while a cable pair is switched on, otherwise
    /// Equalizer APO on the speakers.
    public static readonly EffectsRoute MasterAutomatic = new(AutomaticId, "Automatic",
        "Direct without effects; with effects the cable bus if a cable is on, else Equalizer APO.",
        "Without effects nothing is routed: the master plays straight on the speakers. With effects (or several speakers) it uses the cable bus when a virtual cable is switched on under Wiring > Virtual cables; with none, or when only Equalizer APO can run the master, Equalizer APO on the speakers.");

    /// The master through the cable bus: its cable is Windows' default device, OmniControl's engine runs the chain.
    public static readonly EffectsRoute MasterBus = new("bus", "Cable bus",
        "Through a cable: any effects, several speakers.",
        "The master's cable becomes Windows' default device; OmniControl records it, runs the master's effects (VST3 too) and plays the result on the speakers you choose. Adds a little delay and holds one cable pair.");

    /// The master in Equalizer APO on the real speakers: the cable stays free and Windows' default is the speakers.
    public static readonly EffectsRoute MasterApo = new("apo", "Equalizer APO",
        "In Windows: frees the cable. No VST3, one speaker.",
        "The master's effects run in Equalizer APO on the real speakers, which stay Windows' default device, so the volume keys work as usual and the cable is free for a program, a combined microphone or a combined speaker. Equalizer APO runs the equaliser, gain, IR loader and VST2 plug-ins; VST3 plug-ins are kept but not run.");

    /// Every way the master can run, in the order its pop-down lists them.
    public static IReadOnlyList<EffectsRoute> MasterRoutes { get; } = new[] { MethodOff, MasterAutomatic, MasterBus, MasterApo };

    /// The master's stored method (an unknown or empty id = <see cref="MethodOff"/>).
    public static EffectsRoute MasterRouteOf(string? id) => MasterRoutes.FirstOrDefault(r => r.Id == id) ?? MethodOff;

    /// How the master runs: as chosen; Off = Equalizer APO's way with nothing sent to it (the planner gives it no effects), so nothing is
    /// routed; Automatic = the cable bus while the master plays on several devices, or has effects and either a cable pair is switched on
    /// or Equalizer APO can't run them (then the bus's own status says what is missing); otherwise Equalizer APO, which without effects
    /// writes nothing, so the master plays straight on the speakers.
    public static RunMethod MasterMethodOf(EffectsRoute chosen, MasterFacts facts)
    {
        if (chosen.Id == MasterBus.Id) return RunMethod.Bus;
        if (chosen.Id == MasterApo.Id || chosen.Id == MethodOff.Id) return RunMethod.Apo;
        return facts.SeveralOutputs || facts.HasEffects && (facts.CablePairs > 0 || facts.ApoWhy is not null) ? RunMethod.Bus : RunMethod.Apo;
    }

    /// How the master's Method pop-down entry for <paramref name="route"/> stands now (a disabled entry cannot be chosen; its tip says why).
    public static RouteOffer MasterOfferOf(EffectsRoute route, MasterFacts facts, RunMethod current) =>
        route == MasterApo && facts.SeveralOutputs ? Unavailable(route, "the master plays on several devices")
        : DeviceOfferOf(route, facts, current, "these speakers");

    /// The master's and a microphone's entries (their ids are the same): the cable needs an allowed pair, Equalizer APO says where it
    /// really stands on the device (what the method does moves to its tooltip), Automatic says what runs now.
    private static RouteOffer DeviceOfferOf(EffectsRoute route, MasterFacts facts, RunMethod current, string where)
    {
        if (route == MethodOff) return new RouteOffer(true, route.Description, route.Tip);
        if (route.Id == MasterBus.Id)
            return facts.CablePairs == 0 ? Unavailable(route, Brief(facts.NoCable)) : new RouteOffer(true, route.Description, route.Tip);
        if (route.Id == MasterApo.Id)
        {
            if (facts.ApoWhy is { } why && facts.HasEffects) return Unavailable(route, why);
            return new RouteOffer(true, facts.ApoCard?.Facts ?? route.Description, $"{facts.ApoNow} {route.Description} {route.Tip}".TrimStart(),
                facts.ApoCard?.Label ?? string.Empty);
        }
        return new RouteOffer(true, current != RunMethod.Apo ? "Currently: cable bus" : !facts.HasEffects ? CurrentlyDirect : facts.ApoStand switch
        {
            ApoStand.Running => "Currently: Equalizer APO",
            ApoStand.NotRunning => "Currently: Equalizer APO, but it isn't running, so effects are off",
            _ => $"Currently: Equalizer APO, but it isn't on {where} yet, so effects are off",
        }, route.Tip);
    }

    /// Automatic with no effects: nothing processes or routes the device.
    public const string CurrentlyDirect = "Currently: direct, no effects";

    // ---- a microphone's method (the same ids as the master's) ----

    /// A microphone's Automatic: with effects its cable while a cable pair is switched on, otherwise Equalizer APO on the microphone.
    public static readonly EffectsRoute MicAutomatic = new(AutomaticId, "Automatic",
        "Cable bus if a cable is on, else Equalizer APO.",
        "With effects, uses the cable bus when a virtual cable is switched on under Wiring > Virtual cables (any effects); with none, or when only Equalizer APO can run them, Equalizer APO on the microphone. Without effects nothing is routed.");

    /// A microphone through its cable: OmniControl records it, runs its effects and plays the result into a cable apps record from.
    public static readonly EffectsRoute MicCable = new("bus", "Cable bus",
        "Through a cable: any effects. Needs VB-CABLE.",
        "OmniControl records the microphone, runs its effects (VST3 too) and plays the result into a virtual cable; apps record from that cable's output instead of the microphone. Adds a little delay and holds one cable pair.");

    /// A microphone in Equalizer APO on the microphone itself: no cable, apps keep recording from the microphone.
    public static readonly EffectsRoute MicApo = new("apo", "Equalizer APO",
        "In Windows: no cable, no delay. No VST3.",
        "The microphone's effects run in Equalizer APO on the microphone itself, so apps keep recording from it and no cable is used. Equalizer APO runs the equaliser, gain, IR loader and VST2 plug-ins; VST3 plug-ins are kept but not run.");

    /// Every way a microphone's effects can run, in the order its pop-down lists them; a combined microphone's (OmniControl mixes it, so only its cable runs it).
    public static IReadOnlyList<EffectsRoute> MicRoutes { get; } = new[] { MethodOff, MicAutomatic, MicCable, MicApo };
    public static IReadOnlyList<EffectsRoute> CombinedMicRoutes { get; } = new[] { MicCable, MicApo };

    /// A microphone's stored method (an unknown or empty id = <see cref="MethodOff"/>).
    public static EffectsRoute MicRouteOf(string? id) => MicRoutes.FirstOrDefault(r => r.Id == id) ?? MethodOff;

    /// How a microphone runs: <see cref="RunMethod.Cable"/> or <see cref="RunMethod.Apo"/>, by the master's rule (<see cref="MasterMethodOf"/>).
    public static RunMethod MicMethodOf(EffectsRoute chosen, MasterFacts facts) =>
        MasterMethodOf(chosen, facts) == RunMethod.Bus ? RunMethod.Cable : RunMethod.Apo;

    /// How a microphone's Method pop-down entry stands now.
    public static RouteOffer MicOfferOf(EffectsRoute route, MasterFacts facts, RunMethod current) => DeviceOfferOf(route, facts, current, "this microphone");

    /// A combined microphone's facts: its cables, and Equalizer APO never (it runs on one device; OmniControl mixes the members first).
    public static MasterFacts CombinedMicFacts(CableOffer cables) => new(cables.Pairs, cables.NoCable, CombinedMicNoApo, false, true);

    /// Why a combined microphone can't run in Equalizer APO.
    public const string CombinedMicNoApo = "OmniControl mixes a combined microphone, so its effects run on its cable";

    private static RouteOffer Unavailable(EffectsRoute route, string why)
    {
        var reason = Lowered(why);
        return new RouteOffer(false, $"Not available: {reason}", $"{route.Name} is not available: {reason}.");
    }

    /// The master in Equalizer APO with no effects: it plays straight on the speakers (the row's status and its tooltip).
    public const string MasterDirect = "No master effects: it plays straight on the speakers";

    /// Why the master's devices are one choice while it runs in Equalizer APO (the Wiring cell's note).
    public const string ApoOneSpeaker = "Equalizer APO plays on one speaker. Use Cable bus for several.";

    /// The master's cable line in its tooltip: what the cable is used for now.
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

/// What OmniControl's runs use the cables for now (besides the master bus): the master's cable line.
[Flags]
public enum CableUse
{
    None = 0,
    /// A program on its own cable.
    Program = 1,
    /// A program on a cable with Equalizer APO on it.
    ApoProgram = 2,
    /// A combined or processed microphone.
    Microphone = 4,
    /// Combined speakers.
    Speaker = 8,
}
