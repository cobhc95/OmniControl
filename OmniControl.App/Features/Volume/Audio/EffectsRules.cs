using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Audio;

/// What this PC has, as the engine needs it: what Windows offers, and whether recording one program keeps working when it is muted.
public sealed record SoundFacts(AudioEffectsSupport Support, bool CaptureWorks);

/// Whether a control works now: null reason = it does; otherwise why not. <see cref="Note"/> is the same fact as one short calm
/// sentence for a small pop-down (null = use the reason).
public sealed record Gate(string? Reason, string? Note = null)
{
    public static readonly Gate Open = new((string?)null);
    public bool Allowed => Reason is null;
}

/// How effects run for one kind of row: a few words ("through the cable"), its expected delay ("about 30-80 ms delay") and the engine
/// that processes the sound (what an effect needs to run there: <see cref="FxKind.Engines"/>).
public sealed record ModeMethod(string How, string Delay, FxEngines Engine)
{
    public override string ToString() => $"{How} · {Delay}";
}

/// How Equalizer APO stands on a device (<see cref="EffectsRules.ApoStandOn"/>).
public enum ApoStand { Running, NotRunning, NotOn, NotInstalled }

/// The home of the effects engine's rules and words: how each kind of row runs its effects, the checks
/// and wording for Equalizer APO and the virtual cables. Pure; the pages and the engine read it.
public static partial class EffectsRules
{
    /// The delay of anything that is recorded and played again.
    private const string ReplayDelay = "30-80 ms";

    private static readonly ModeMethod ThroughCable = new("through the cable", $"about {ReplayDelay} delay", FxEngines.Program);
    private static readonly ModeMethod ProgramMethod = new("through a cable or by recording", $"about {ReplayDelay} delay", FxEngines.Program);

    /// How the effects of the row <paramref name="key"/> run: the master (null or <see cref="MixerService.MasterKey"/>) and a
    /// microphone (a recording device) through a cable, anything else (programs, combined devices) through a cable or by recording.
    public static ModeMethod MethodFor(string? key) =>
        key is null || key == MixerService.MasterKey || MixerService.IsInput(key) ? ThroughCable : ProgramMethod;

    /// A recorded program that is open but silent: its original is already turned down and the recording waits, so the effects apply from its first sound.
    public const string EffectsReady = "Effects ready for when it plays";

    /// How a row runs when its chain is in Equalizer APO on its own device: the master on the speakers, a microphone on itself.
    public static ModeMethod ApoOnDevice { get; } = new("in Windows (Equalizer APO)", "no extra delay", FxEngines.Apo);

    /// A program on a cable with Equalizer APO on it (<see cref="RouteApoCable"/>): Equalizer APO runs the chain, the cable adds its own short delay.
    public static ModeMethod ApoOnCable { get; } = new("in Equalizer APO on its cable", "a cable's short delay", FxEngines.Apo);

    /// A combined microphone with none of its members plugged in.
    public const string NoMicrophonePluggedIn = "None of its microphones is plugged in";

    /// A processed or combined microphone's running words: what apps pick as their microphone (Windows' name, not ours).
    public static string AppsRecordFrom(string recordingName) => $"Apps record it from {recordingName}";

    /// Why a microphone waits for a cable while the master bus holds the only pair apps can record from.
    public static string BusHoldsCable(string cableName) => $"The master bus uses {cableName}. Set the master's method to Equalizer APO to free it.";

    /// The tag and its tooltip on a recording device that records what the PC plays (<see cref="LoopbackInputs"/>, "Stereo Mix").
    public const string RecordsPlaybackTag = "Records the PC's sound", RecordsPlaybackTip = "In calls others may hear themselves.";

    /// Why Capture and replay can't run here, or null when it can (or nobody has tested yet).
    public static string? CaptureMissing(SoundFacts facts) =>
        !facts.Support.ProcessLoopback ? "Needs Windows 10 version 2004 or later"
        : !facts.CaptureWorks ? "Recording stops when the program is turned down here"
        : null;

    /// Why master effects can't run in the user's Equalizer APO on the default playback device, or null when they can. The checks and
    /// their order are Equalizer APO's own (its Device Selector).
    public static string? ApoMissing(ApoState apo) => ApoMissingOn(apo, apo.Output);

    /// Why a chain can't run in Equalizer APO on the playback device <paramref name="endpoint"/> (the real speakers while a cable may be
    /// the default, or a cable), or null when it can; null endpoint = not looked at, which counts as not on it.
    public static string? ApoMissingOn(ApoState apo, ApoEndpoint? endpoint) => EndpointMissing(apo, endpoint, "this device");

    private static string? EndpointMissing(ApoState apo, ApoEndpoint? endpoint, string where)
    {
        if (endpoint is not null && apo.FolderFor(endpoint) is not null) return null;
        if (endpoint is { Attachable: false }) return $"Equalizer APO can't run on {where}: Windows gives remote devices no effect slots";
        if (apo.UpstreamInstalled)
            return !apo.UpstreamRegistered ? "Equalizer APO is not registered"
                : endpoint?.UpstreamOn != true ? $"Equalizer APO is not on {where}"
                : endpoint.EnhancementsOff ? $"Audio enhancements are off on {where}"
                : !apo.ProtectedAudioDisabled ? "Equalizer APO is not loaded by Windows"
                : "Equalizer APO's config folder is missing";
        return "Equalizer APO is not installed";
    }

    /// Equalizer APO's own Device Selector is what fixes a device it is missing on (registers it, puts it on the device, turns
    /// enhancements on); OmniControl only opens it.
    public static bool DeviceSelectorFixes(ApoState apo) => apo.UpstreamDeviceSelector is not null && apo.UpstreamConfigFolder is not null;

    /// Installing Equalizer APO (its official installer, which OmniControl ships) is the step that would make a chain run on
    /// <paramref name="endpoint"/>: it is not installed and the device has effect slots. Never run by itself: it is offered.
    public static bool InstallFixes(ApoState apo, ApoEndpoint? endpoint) => endpoint is { Attachable: true } && !apo.UpstreamInstalled;

    /// Where Equalizer APO stands on the master's speakers, in one sentence (the master's Method menu, its Equalizer APO card): installed or
    /// not, on the speakers or not, and why Windows doesn't run it there when the registry says so. Whether it really started is not checked.
    /// <param name="unnamed">The device's words when Windows keeps no connector name for it.</param>
    public static string ApoNowOn(ApoState apo, ApoEndpoint? speakers, string unnamed = "the master's speakers")
    {
        var name = speakers?.Name is { Length: > 0 } connector ? connector : unnamed;
        if (speakers is { Attachable: false }) return $"Equalizer APO can't run on {name}: Windows gives remote devices no effect slots.";
        return ApoStandOn(apo, speakers) switch
        {
            ApoStand.NotInstalled => "Equalizer APO is not installed; OmniControl brings its official installer.",
            ApoStand.NotOn => $"Equalizer APO is installed, not on {name} yet.",
            ApoStand.NotRunning => $"{ApoMissingOn(apo, speakers)}.",
            _ => $"Equalizer APO is on {name}.",
        };
    }

    /// THE step that opens Equalizer APO's own Device Selector, and the one offered instead while it is not installed.
    public const string ApoDevicesLabel = "Equalizer APO devices…", ApoInstallLabel = "Install Equalizer APO…";

    /// How Equalizer APO stands on the master's speakers, from the registry only (Device Selector's own facts): on them and loaded by
    /// Windows counts as running; its check that it really started is Device Selector's, which OmniControl does not run.
    public static ApoStand ApoStandOn(ApoState apo, ApoEndpoint? speakers) =>
        !apo.UpstreamInstalled ? ApoStand.NotInstalled
        : speakers?.UpstreamOn != true ? ApoStand.NotOn
        : apo.FolderFor(speakers) is not null ? ApoStand.Running
        : ApoStand.NotRunning;

    /// The Equalizer APO card's facts in the master's Method menu ("✓ On Speakers", "✕ Not on Speakers") and the small label after its
    /// name ("installed separately": it is its own program).
    public static (string Facts, string Label) ApoCardOf(ApoState apo, ApoEndpoint? speakers, string unnamed = "these speakers")
    {
        var name = speakers?.Name is { Length: > 0 } connector ? connector : unnamed;
        return (ApoStandOn(apo, speakers) switch
        {
            ApoStand.NotInstalled => "Not installed",
            ApoStand.NotOn => $"✕ Not on {name}",
            ApoStand.NotRunning => $"✓ On {name} · ✕ Not running",
            _ => $"✓ On {name}",
        }, apo.UpstreamInstalled ? "installed separately" : string.Empty);
    }

    /// The fact under the master's or a microphone's chip while it is set to Equalizer APO and that does not run (null = the engine's own fact).
    public static string? ApoStandFact(ApoStand stand, string device = "speaker") => stand switch
    {
        ApoStand.NotRunning => "sound plays without effects",
        ApoStand.NotOn or ApoStand.NotInstalled => $"not on this {device} yet",
        _ => null,
    };

    /// A row's status once its chain is written: all OmniControl can know is that the chain was sent, not that Windows applied it.
    public const string ApoSent = "Chain sent to your Equalizer APO";

    /// The Equalizer APO OmniControl writes to blends a Mix below 100 % (<see cref="ApoState.Blends"/>; true while none runs). The FX
    /// window's curve and the folder's Mix read it.
    public static bool ApoBlends(ApoState apo) => !apo.UpstreamUsable || apo.Blends;

    /// A device folder's Mix while Equalizer APO runs it and is too old to blend (the slider stays at 100 %), and the skipped effects' reason.
    public static readonly string ApoNoBlend = $"This Equalizer APO is older than {ApoIds.BlendVersion} and can't blend, so Mix stays at 100 %.",
        ApoNoBlendReason = $"a mix below 100 % needs Equalizer APO {ApoIds.BlendVersion}";

    /// Equalizer APO's config.txt no longer includes OmniControl's file (its editor or another program wrote it back without the line).
    public const string ApoDisplaced = "Equalizer APO's config no longer includes OmniControl's effects";

    /// An IR Loader's Auto level is measured before its chain is written for Equalizer APO.
    public const string ApoMeasuringIr = "Measuring the IR file's level before sending the chain";

    /// The tag beside a chain run by Equalizer APO whose config.txt also runs effects of its own, and the tag's tooltip.
    public const string ApoOwnEffects = "Equalizer APO's own effects also run",
        ApoOwnEffectsTip = "config.txt holds effects of its own besides OmniControl's line; Equalizer APO runs them as well.";

    /// The tag in the FX window while a plug-in's meters or an equaliser's spectrum are fed with a capture of what you hear, and its tooltip.
    public const string DisplayFed = "Display fed with what you hear",
        DisplayFedTip = "Equalizer APO runs this effect inside Windows, where OmniControl can't see its sound, so the meters and analysers here are fed with a capture of the device, after Equalizer APO. "
            + "The capture is level-matched to the full-volume signal (scaled back up by the device's volume). "
            + "It is used for the display only, never played or sent anywhere, and may differ from what Equalizer APO is doing: dynamics meters such as gain reduction read the already-processed sound.",
        DisplayNotFed = "Display not fed: device muted or too quiet",
        DisplayNotFedTip = "The capture comes after the device's volume, so while the device is muted or its volume is nearly zero there is no sound to level-match, "
            + "and nothing is fed to the meters and analysers (they hold their last picture).";

    /// The setting that puts OmniControl's include line and file back when another program takes them out.
    public const string KeepInPlace = "Keep OmniControl's effects in place",
        KeepInPlaceTip = "When Equalizer APO's editor or another program writes its config without OmniControl's line, put it back at once.";

    /// What a row or card says when no cable is installed, when every installed cable is switched off, or when every allowed cable is in use.
    public const string NeedsCable = "Needs VB-CABLE", CablesOff = "Cables switched off", NeedsAnotherCable = "Needs another cable";

    /// Whether a row's status says it has no cable (the Wiring tab shows it in the row's Cable column).
    public static bool IsCableMissing(StatusKind kind) => kind is StatusKind.NeedsCable or StatusKind.CablesOff or StatusKind.NeedsAnotherCable or StatusKind.NoFreeCable;

    /// A whole cable pair is installed (allowed or switched off).
    public static bool CableInstalled(AudioEffectsSupport support) => support.Cables.Count > 0 || support.Found.Any(f => f.IsPair);

    /// Why no cable can be given out at all: <see cref="StatusKind.NeedsCable"/> (none installed) or <see cref="StatusKind.CablesOff"/>
    /// (every one switched off); <see cref="StatusKind.Other"/> when at least one cable is allowed.
    public static StatusKind NoCableKind(AudioEffectsSupport support) =>
        support.Cables.Count > 0 ? StatusKind.Other : CableInstalled(support) ? StatusKind.CablesOff : StatusKind.NeedsCable;

    /// Why no cable can run anything here (<see cref="NoCableKind"/> in words), or null when at least one cable is allowed.
    public static string? CablesMissing(AudioEffectsSupport support) => NoCableKind(support) switch
    {
        StatusKind.NeedsCable => NeedsCable,
        StatusKind.CablesOff => CablesOff,
        _ => null,
    };

    /// How many cable pairs OmniControl may use (VB-CABLE's two inputs are one pair): the count every tag shows.
    public static int AllowedPairs(AudioEffectsSupport support) => VirtualCables.Pairs(support.Cables).Count;

    /// The cable status line: how many virtual cables may be used, or what is missing.
    public static TagItem CableStatus(SoundFacts facts)
    {
        var count = AllowedPairs(facts.Support);
        var names = facts.Support.Cables.Where(c => c.RecordingId is not null).Select(c => c.Name); // one per pair: the input with its own recording side
        return CablesMissing(facts.Support) is { } missing
            ? new TagItem(missing, TagTone.Caution, missing == NeedsCable ? "Install VB-CABLE first." : "Switch a cable on under Virtual cables.")
            : new TagItem(count == 1 ? "Ready · 1 virtual cable" : $"Ready · {count} virtual cables", TagTone.Good,
                "Allowed: " + string.Join(", ", names) + ". Each combined device or processed microphone uses one; a processed program uses one when free, else it is recorded.");
    }
}
