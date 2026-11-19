using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;

namespace OmniControl.App.Features.Volume.Pages.Easy.Flow;

/// One thing a question screen lets the user tick: an app, a microphone or a speaker device.
public sealed record EasyOption(string Id, string Name, string Tag = "");

/// A combined device that exists now, with what apps pick as it (a microphone's cable recording side).
public sealed record EasyCombinedFact(string Key, CombinedKind Kind, string Name, IReadOnlyList<string> Members, string? RecordingName);

/// What the virtual cables can give a new combined device now (the answer of the page's own cable rules).
/// <param name="FreeName">The free cable, or null.</param>
/// <param name="RecordingName">What apps pick as the microphone when the free cable is used ("CABLE Output").</param>
/// <param name="Missing">Why no cable at all can be given out: none installed or all switched off; <see cref="StatusKind.Other"/> when some are allowed.</param>
/// <param name="BusName">The master bus's cable when it holds the one that would be free; null = it does not.</param>
/// <param name="CanFreeBus">The master can run in Equalizer APO instead, which frees the bus's cable.</param>
public sealed record EasyCableFact(string? FreeName, string? RecordingName, StatusKind Missing, string? BusName, bool CanFreeBus);

/// A snapshot of everything the Easy setup checklists read, made from the live services each time something changes
/// (<see cref="EasyEnvironment"/>) and worked on by pure rules (<see cref="EasyChecks"/>), so every check is tested with a made-up one.
/// Defaults describe a PC where everything is in place.
public sealed record EasyFacts
{
    /// The PC has not been looked at yet (no answer from the effects support service); every check waits.
    public bool Looking { get; init; }

    // ---- Equalizer APO and the cables ----
    public bool ApoInstalled { get; init; } = true;
    /// How many virtual cable pairs OmniControl may use now (0 = none installed, or all switched off).
    public int CablesReady { get; init; } = 1;
    /// A virtual cable is installed (allowed or switched off).
    public bool CableInstalled { get; init; } = true;
    /// Equalizer APO's Device Selector can fix a device it is missing on.
    public bool DeviceSelectorFixes { get; init; } = true;

    // ---- card 1: the master ----
    /// Why Equalizer APO does not cover the master's speakers, or null when it does.
    public string? SpeakersApoWhy { get; init; }
    /// The master runs through the cable bus (so Equalizer APO is not needed for it).
    public bool MasterOnBus { get; init; }
    public bool MasterFxOn { get; init; }
    /// The five gains the master makes now (flat while its effects or equaliser are off).
    public IReadOnlyList<int> MasterGains { get; init; } = EasyEq.Flat();
    /// The five gains stored in the master's equaliser: what the user chose, even if it is switched off.
    public IReadOnlyList<int> MasterStoredGains { get; init; } = EasyEq.Flat();

    // ---- card 2: apps ----
    public IReadOnlyList<EasyOption> Programs { get; init; } = Array.Empty<EasyOption>();
    /// Why a program's effects cannot run on this PC (recording does not work), or null.
    public string? ProgramsWhy { get; init; }
    /// Apps (keys) whose own equaliser is on.
    public IReadOnlySet<string> OwnEqPrograms { get; init; } = new HashSet<string>();

    // ---- card 3: microphones ----
    public IReadOnlyList<EasyOption> Microphones { get; init; } = Array.Empty<EasyOption>();
    /// The five gains a microphone's chain makes now, by endpoint id (flat while off); not listed = flat.
    public IReadOnlyDictionary<string, int[]> MicGains { get; init; } = new Dictionary<string, int[]>();
    /// Microphones (endpoint ids) with a look on (a non-flat equaliser, effects on).
    public IReadOnlySet<string> OwnEqMicrophones { get; init; } = new HashSet<string>();

    // ---- cards 4 and 5: combining ----
    public IReadOnlyList<EasyOption> Speakers { get; init; } = Array.Empty<EasyOption>();
    public EasyCableFact CableForMicrophone { get; init; } = new("CABLE Input", "CABLE Output", StatusKind.Other, null, false);
    public EasyCableFact CableForSpeakers { get; init; } = new("CABLE Input", "CABLE Output", StatusKind.Other, null, false);
    public IReadOnlyList<EasyCombinedFact> Combined { get; init; } = Array.Empty<EasyCombinedFact>();
    /// The name a new combined microphone / speakers gets when none is typed (free of the names in use).
    public string SuggestedMicrophoneName { get; init; } = "Combined microphone";
    public string SuggestedSpeakersName { get; init; } = "Both speakers";
    /// What the master is set to play on (combined device keys or endpoint ids); empty = one real device.
    public IReadOnlyList<string> MasterPlaysOn { get; init; } = Array.Empty<string>();
}
