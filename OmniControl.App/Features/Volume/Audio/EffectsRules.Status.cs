namespace OmniControl.App.Features.Volume.Audio;

/// The way a row's effects really run, as the Mixer row's short status names it ("Recording", "Cable", "Bus", "APO", "APO cable").
public enum RunMethod { None, Recording, Cable, Bus, Apo, ApoCable }

/// What a row's effects are doing or why not (the fact behind its short status, <see cref="EffectsRules.Brief"/>).
public enum StatusKind
{
    /// No specific fact: the short status falls back on the state alone.
    Other,
    Starting,
    /// Armed: open and silent, its effects apply from its first sound.
    Ready,
    Checking,
    /// The program is closed.
    StartsWhenPlays,
    /// The program is open and silent and waits for sound.
    WaitingForSound,
    Running,
    NoFreeCable,
    NeedsCable,
    /// Cables are installed but the user switched every one off (Wiring > Virtual cables).
    CablesOff,
    NeedsAnotherCable,
    CannotRecord,
    NothingPluggedIn,
    NeedsApo,
    /// The user chose Equalizer APO for the master, but it can't run on the speakers.
    ApoNotApplied,
    ApoFailed,
    /// Equalizer APO's config no longer includes OmniControl's effects, and Keep in place is off.
    ApoDisplaced,
    /// The master in Equalizer APO with no effects: nothing runs, it plays straight on the speakers.
    Direct,
    WentSilent,
    /// Windows has not moved the program to its cable yet.
    NotMoved,
    /// Windows refused to move the program to its cable.
    CouldNotMove,
    Stopped,
}

/// What a program's Method menu needs to know about the virtual cables: how many pairs OmniControl may use, whether one is free for it,
/// whether the master bus holds one, and, with no pair, whether none is installed or all are switched off (<see cref="EffectsRules.NoCableKind"/>).
public sealed record CableOffer(int Pairs, bool Free, bool BusHolds, StatusKind NoCable = StatusKind.NeedsCable)
{
    public static readonly CableOffer None = new(0, false, false);
}

/// What the Method menu of a program may offer on this PC.
/// <param name="Cables">The virtual cables, from the engine's last plan.</param>
/// <param name="RecordingWhy">Why a program can't be recorded here (null = it can).</param>
/// <param name="Current">How the program's effects run right now (none while nothing runs).</param>
/// <param name="ApoCableWhy">Why the program can't use Equalizer APO on a cable now (null = it can).</param>
public sealed record RouteFacts(CableOffer Cables, string? RecordingWhy, RunMethod Current, string? ApoCableWhy = EffectsRules.NeedsSecondCable);

/// One entry of a program's Method menu as it stands now: whether it can be chosen, its live description, and its tooltip (the reason when it can't).
public sealed record RouteOffer(bool Available, string Description, string Tip);

/// A test's result in two sizes: one short line for the row and the footer, the full explanation for their tooltips.
public sealed record TestOutcome(string Brief, string Full);

/// The one home of the short words a Mixer row, the Wiring tab and the footer use for what the effects engine is doing:
/// the form is "Method · NN ms" with the method the engine really uses; the explanation, what to do next and the technical detail
/// go to tooltips and the FX window's longer line.
public static partial class EffectsRules
{
    /// The unit after a delay figure, kept in one place.
    private const string DelayUnit = " ms";
    private const string Join = " · ";

    private static string MethodWord(RunMethod method) => method switch
    {
        RunMethod.Recording => "Recording",
        RunMethod.Cable => "Cable",
        RunMethod.Bus => "Bus",
        RunMethod.Apo => "APO",
        RunMethod.ApoCable => "APO cable",
        _ => string.Empty,
    };

    /// The row's short status: "Recording · 70 ms", "Cable · 10 ms", "Bus · 40 ms", "APO", "Recording ready", "No free cable"... No sentence
    /// punctuation and no figure unless the effects run and the delay is known.
    public static string Brief(StatusKind kind, RunMethod method = RunMethod.None, int? delayMs = null)
    {
        var word = MethodWord(method);
        return kind switch
        {
            StatusKind.Running => word.Length == 0 ? "Running" : delayMs is { } d ? $"{word}{Join}{d}{DelayUnit}" : word,
            StatusKind.Ready => word.Length == 0 ? "Ready" : $"{word} ready",
            StatusKind.Starting => "Starting",
            StatusKind.Checking => "Checking",
            StatusKind.StartsWhenPlays => "Starts when it plays",
            StatusKind.WaitingForSound => "Waiting for sound",
            StatusKind.NoFreeCable => "No free cable",
            StatusKind.NeedsCable => NeedsCable,
            StatusKind.CablesOff => CablesOff,
            StatusKind.NeedsAnotherCable => NeedsAnotherCable,
            StatusKind.CannotRecord => "Can't record",
            StatusKind.NothingPluggedIn => "Nothing plugged in",
            StatusKind.NeedsApo => "Needs APO",
            StatusKind.ApoNotApplied => "Equalizer APO not applied",
            StatusKind.Direct => "Direct",
            StatusKind.ApoFailed => "APO write failed",
            StatusKind.ApoDisplaced => "Taken out of Equalizer APO",
            StatusKind.WentSilent => word.Length == 0 ? "Went silent" : $"{word} went silent",
            StatusKind.NotMoved => "Not on its cable yet",
            StatusKind.CouldNotMove => "Windows would not move it",
            StatusKind.Stopped => "Engine stopped",
            _ => string.Empty,
        };
    }

    /// The short status of a state that carries no specific fact.
    public const string NotAvailable = "Not available";

    /// What to do next, or what the method means: the tooltip's second line; null = nothing to add.
    public static string? Advice(StatusKind kind, RunMethod method) => kind switch
    {
        StatusKind.Running or StatusKind.Ready => method switch
        {
            RunMethod.Recording => "Recording: OmniControl records the program, turns its own sound down and plays the processed copy.",
            RunMethod.Cable => "Cable: Windows sends the program to a virtual cable and OmniControl processes it there.",
            RunMethod.Bus => "Bus: every program that follows the master goes through the master's virtual cable.",
            RunMethod.Apo => "APO: Equalizer APO processes the sound inside Windows.",
            RunMethod.ApoCable => "APO cable: Windows sends the program to a cable with Equalizer APO on it; OmniControl passes the cable on to the speakers.",
            _ => null,
        },
        StatusKind.NoFreeCable => "Every virtual cable pair is in use. Use Recording for this program, or free a cable.",
        StatusKind.ApoNotApplied => "Choose devices in Equalizer APO, or choose Cable bus as the master's method.",
        StatusKind.ApoDisplaced => "Put back in the FX window, or switch on Keep OmniControl's effects in place.",
        StatusKind.Direct => "The cable is free for a program, a combined microphone or a combined speaker.",
        StatusKind.NeedsCable => "Install VB-CABLE to use a virtual cable.",
        StatusKind.CablesOff => "Switch a cable on under Wiring > Virtual cables.",
        StatusKind.NeedsAnotherCable => "Every cable is in use. Add another virtual cable.",
        StatusKind.WentSilent => "The original sound was put back. Choose another method, or press Test.",
        StatusKind.NotMoved => "Pause and play the program, or restart it.",
        StatusKind.CouldNotMove => "The original sound was put back.",
        StatusKind.Stopped => "The original sound was put back.",
        _ => null,
    };

    // ---- how a program's effects run: the Method menu ----

    /// How the Method menu's entry for <paramref name="route"/> stands on this PC (a disabled entry cannot be chosen; its tip says why).
    public static RouteOffer OfferOf(EffectsRoute route, RouteFacts facts)
    {
        if (route == RouteApoCable)
            return facts.ApoCableWhy is { } missing ? Unavailable(route, missing) : new RouteOffer(true, route.Description, route.Tip);
        if (route == RouteCable)
        {
            if (CableMissingWhy(facts.Cables) is { } why) return new RouteOffer(false, $"Not available: {why}", $"Virtual cable is not available: {why}.");
            return new RouteOffer(true, route.Description, route.Tip);
        }
        if (route == RouteCapture)
        {
            if (facts.RecordingWhy is { } why)
            {
                var reason = Lowered(why);
                return new RouteOffer(false, $"Not available: {reason}", $"Recording is not available: {reason}.");
            }
            return new RouteOffer(true, route.Description, route.Tip);
        }
        return new RouteOffer(true, CurrentlyUsing(facts.Current) ?? route.Description, route.Tip);
    }

    /// Why a program can't use a virtual cable now, or null when it can.
    private static string? CableMissingWhy(CableOffer cables) =>
        cables.Pairs == 0 ? cables.NoCable == StatusKind.CablesOff ? "every cable is switched off" : "no cable pair installed"
        : cables.Free ? null
        : cables.BusHolds && cables.Pairs == 1 ? "the master bus uses the cable"
        : "every cable pair is in use";

    private static string? CurrentlyUsing(RunMethod method) => method switch
    {
        RunMethod.Recording => "Currently: recording",
        RunMethod.Cable => "Currently: cable",
        _ => null,
    };

    /// The Use Recording step offered when a program set to Virtual cable has no cable (its tooltip).
    public const string UseRecordingLabel = "Use Recording", UseRecordingTip = "Records the program instead of using a cable, so its effects run";

    // ---- the Test button ----

    private const string TestFailed = "Test failed";

    /// The Test of a program that works: the short line is "Test passed · 70 ms".
    public static TestOutcome TestPassed(int delayMs, SilenceMethod method, int effects, string skipped) => new(
        $"Test passed{Join}{delayMs}{DelayUnit}",
        $"It works on this PC: the recording kept the sound after {TestHow(method)}. Extra delay about {delayMs}{DelayUnit}. Effects that ran: {effects}{skipped}.");

    public static TestOutcome TestWentSilent(string program) => new(
        $"{TestFailed}{Join}went silent",
        $"Turning {program} down also silenced its recording, every way. Programs can't be recorded on this PC; use a virtual cable.");

    public static TestOutcome TestNotListed() => new($"{TestFailed}{Join}not listed", "This program is no longer in the list.");

    public static TestOutcome TestNotPlaying(string program) => new($"{TestFailed}{Join}not playing", $"Start playing sound in {program}, then press Test again.");

    public static TestOutcome TestCannotRecord(string program, string why) => new($"{TestFailed}{Join}can't record", $"Windows refused to record {program}: {why}");

    public static TestOutcome TestHeardNothing(string program) =>
        new($"{TestFailed}{Join}heard nothing", $"Couldn't hear {program}. Play something (not paused, not muted) and press Test again.");

    public static TestOutcome TestRunning() => new("Testing", "Testing… keep the program playing");

    public static TestOutcome TestAlreadyRunning() => new("Test running", "A test is already running for this program.");

    private static string TestHow(SilenceMethod method) => method switch
    {
        SilenceMethod.Mute => "muting the original",
        SilenceMethod.ZeroVolume => "turning the original's volume to 0",
        _ => "turning the original down to near silence",
    };
}
