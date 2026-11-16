using OmniControl.Core.Engine;

namespace OmniControl.App.Audio;

/// <summary>
/// The one home of the rules (and their words) for "can this be chosen or pressed now?" on the Volume page (the owner's rule: only what
/// works is selectable; the rest is greyed out with the reason, and follows the live state). Every rule is pure: the page passes the live
/// facts in and shows the answer; nothing here reads Windows or the engine. The Method menus (<see cref="OfferOf"/>,
/// <see cref="MasterOfferOf"/>) keep their rules next to their data.
/// </summary>
public static partial class EffectsRules
{
    internal static string Capitalised(string text) => $"{char.ToUpperInvariant(text[0])}{text[1..]}";

    /// <summary>A sentence's first letter in lower case, to follow "Not available: "; a name it starts with keeps its capital.</summary>
    internal static string Lowered(string text) =>
        Names.Any(n => text.StartsWith(n, StringComparison.Ordinal)) ? text : $"{char.ToLowerInvariant(text[0])}{text[1..]}";

    /// <summary>The names a reason can start with (they keep their capital letter).</summary>
    private static readonly string[] Names = { "Equalizer APO", "OmniControl", "Windows", "VB-CABLE" };

    // ---- combining ----

    /// <summary>"+ Combine microphones" with no pair apps can record from (a cable with a recording side).</summary>
    public const string NoRecordableCable = "No cable apps can record from";

    /// <summary>
    /// Whether a NEW combined device can be created and run now: it needs a cable that is free (a microphone one apps can record from).
    /// The master bus holding the only pair is said so. Null support = not looked at yet: open.
    /// </summary>
    /// <param name="microphones">True for a combined microphone, false for combined speakers.</param>
    /// <param name="freeCable">A cable that fits this kind is free now.</param>
    /// <param name="busCableName">The master bus's cable (bus method), or null.</param>
    public static Gate CombineGate(bool microphones, AudioEffectsSupport? support, bool freeCable, string? busCableName)
    {
        if (support is null) return Gate.Open;
        var noun = microphones ? "a combined microphone" : "combined speakers";
        if (CablesMissing(support) is { } missing) return new Gate($"{missing}: {noun} needs a virtual cable", missing);
        if (microphones && !support.Cables.Any(c => c.RecordingId is not null)) return new Gate($"{NoRecordableCable}: apps record a combined microphone from one", NoRecordableCable);
        if (freeCable) return Gate.Open;
        if (busCableName is null) return new Gate($"{Brief(StatusKind.NoFreeCable)}: every cable pair is in use", Brief(StatusKind.NoFreeCable));
        return microphones
            ? new Gate(BusHoldsCable(busCableName), Brief(StatusKind.NoFreeCable))
            : new Gate($"{Brief(StatusKind.NoFreeCable)}: the master bus uses {busCableName}", Brief(StatusKind.NoFreeCable));
    }

    /// <summary>
    /// A combined speaker as a "plays on" choice: the gate of what it is combined with (<paramref name="baseGate"/>) first, then whether it has a cable to play
    /// through (a combined speaker without one plays nowhere, and its row says <paramref name="cableStatus"/>).
    /// </summary>
    public static Gate CombinedChoiceGate(Gate baseGate, StatusKind cableStatus, string name) =>
        !baseGate.Allowed || !IsCableMissing(cableStatus) ? baseGate : new Gate($"{name} has no cable yet ({Brief(cableStatus)})");

    // ---- the Virtual cables switches ----

    /// <summary>
    /// A cable switch's tooltip; while something uses the cable (<paramref name="user"/>: the row's name, "the master bus", or what keeps
    /// it) it says that switching it off stops that.
    /// </summary>
    public static string CableSwitchTip(string? user) => user is null
        ? "On: OmniControl may use this cable. Off: it never does."
        : $"On: OmniControl may use this cable. Off: it never does, and {user} stops until another cable is free.";

    /// <summary>The footer sentence after a cable that a run uses was switched off.</summary>
    public static string CableSwitchedOffStops(string cable, string user) => $"{cable} switched off: {user} stops until another cable is free";

    // ---- a method chosen earlier that can no longer run ----

    /// <summary>The one step offered on the master's row when it was set to the cable bus and no cable can be given out: use Equalizer APO instead.</summary>
    public const string UseApoLabel = "Use Equalizer APO", UseApoTip = "Runs the master's effects in Equalizer APO on the speakers instead of the cable bus";

    // ---- the Test ----

    /// <summary>Why the recording Test can't run on a program now (it must be playing), or null when it can.</summary>
    public static string? TestUnavailable(string program, bool playing) => playing ? null : $"Play sound in {program} first, then press Test";
}
