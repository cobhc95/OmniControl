using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Audio;

/// <summary>What this PC has, as the engine needs it: what Windows offers, and whether recording one program keeps working when it is muted.</summary>
public sealed record SoundFacts(AudioEffectsSupport Support, bool CaptureWorks);

/// <summary>
/// Whether a control works now: null reason = it does; otherwise why not. <see cref="Note"/> is the same fact as one short calm
/// sentence for a small pop-down (null = use the reason).
/// </summary>
public sealed record Gate(string? Reason, string? Note = null)
{
    public static readonly Gate Open = new((string?)null);
    public bool Allowed => Reason is null;
}

/// <summary>
/// How effects run for one kind of row: a few words ("through the cable"), its expected delay ("about 30-80 ms delay") and the engine
/// that processes the sound (what an effect needs to run there: <see cref="FxKind.Engines"/>).
/// </summary>
public sealed record ModeMethod(string How, string Delay, FxEngines Engine)
{
    public override string ToString() => $"{How} · {Delay}";
}

/// <summary>
/// The home of the effects engine's rules and words (AGENTS.md "One master list"): how each kind of row runs its effects, the checks
/// and wording for Equalizer APO and the virtual cables. Pure; the pages and the engine read it.
/// </summary>
public static partial class EffectsRules
{
    /// <summary>The delay of anything that is recorded and played again.</summary>
    private const string ReplayDelay = "30-80 ms";

    private static readonly ModeMethod ThroughCable = new("through the cable", $"about {ReplayDelay} delay", FxEngines.Program);
    private static readonly ModeMethod ProgramMethod = new("through a cable or by recording", $"about {ReplayDelay} delay", FxEngines.Program);

    /// <summary>
    /// How the effects of the row <paramref name="key"/> run: the master (null or <see cref="MixerService.MasterKey"/>) and a
    /// microphone (a recording device) through a cable, anything else (programs, combined devices) through a cable or by recording.
    /// </summary>
    public static ModeMethod MethodFor(string? key) =>
        key is null || key == MixerService.MasterKey || MixerService.IsInput(key) ? ThroughCable : ProgramMethod;

    /// <summary>
    /// A recorded program that is open but silent: its original is already turned down and the recording waits, so the effects apply from
    /// its first sound.
    /// </summary>
    public const string EffectsReady = "Effects ready for when it plays";

    /// <summary>How a row runs when its chain is in Equalizer APO: the master on the speakers (method Equalizer APO).</summary>
    public static ModeMethod ApoOnSpeakers { get; } = new("in Windows (Equalizer APO)", "no extra delay", FxEngines.Apo);

    /// <summary>A program on a cable with Equalizer APO on it (<see cref="RouteApoCable"/>): Equalizer APO runs the chain, the cable adds its own short delay.</summary>
    public static ModeMethod ApoOnCable { get; } = new("in Equalizer APO on its cable", "a cable's short delay", FxEngines.Apo);

    /// <summary>A combined microphone with none of its members plugged in.</summary>
    public const string NoMicrophonePluggedIn = "None of its microphones is plugged in";

    /// <summary>A processed or combined microphone's running words: what apps pick as their microphone (Windows' name, not ours).</summary>
    public static string AppsRecordFrom(string recordingName) => $"Apps record it from {recordingName}";

    /// <summary>Why a microphone waits for a cable while the master bus holds the only pair apps can record from.</summary>
    public static string BusHoldsCable(string cableName) => $"The master bus uses {cableName}. Set the master's method to Equalizer APO to free it.";

    /// <summary>The tag and its tooltip on a recording device that records what the PC plays (<see cref="LoopbackInputs"/>, "Stereo Mix").</summary>
    public const string RecordsPlaybackTag = "Records the PC's sound", RecordsPlaybackTip = "In calls others may hear themselves.";

    /// <summary>Why Capture and replay can't run here, or null when it can (or nobody has tested yet).</summary>
    public static string? CaptureMissing(SoundFacts facts) =>
        !facts.Support.ProcessLoopback ? "Needs Windows 10 version 2004 or later"
        : !facts.CaptureWorks ? "Recording stops when the program is turned down here"
        : null;

    /// <summary>
    /// Why master effects can't run in Equalizer APO on the default playback device, or null when they can (the user's own Equalizer
    /// APO first, else OmniControl APO). The checks and their order are Equalizer APO's own (its Device Selector).
    /// </summary>
    public static string? ApoMissing(ApoState apo) => ApoMissingOn(apo, apo.Output);

    /// <summary>
    /// Why a chain can't run in Equalizer APO on the playback device <paramref name="endpoint"/> (the real speakers while a cable may be
    /// the default, or a cable), or null when it can; null endpoint = not looked at, which counts as not on it.
    /// </summary>
    public static string? ApoMissingOn(ApoState apo, ApoEndpoint? endpoint) => EndpointMissing(apo, endpoint, "this device");

    private static string? EndpointMissing(ApoState apo, ApoEndpoint? endpoint, string where)
    {
        if (endpoint is not null && apo.FolderFor(endpoint) is not null) return null;
        if (apo.UpstreamInstalled)
            return !apo.UpstreamRegistered ? "Equalizer APO is not registered"
                : endpoint?.UpstreamOn != true ? $"Equalizer APO is not on {where}"
                : endpoint.EnhancementsOff ? $"Audio enhancements are off on {where}"
                : !apo.ProtectedAudioDisabled ? "Equalizer APO is not loaded by Windows"
                : "Equalizer APO's config folder is missing";
        if (!apo.AnyInstalled) return "Equalizer APO is not installed";
        return !apo.Registered || apo.ConfigFolder is null ? "OmniControl APO install is incomplete"
            : !apo.ProtectedAudioDisabled ? "OmniControl APO is not loaded by Windows"
            : endpoint?.OursOn != true ? $"OmniControl APO is not on {where}"
            : $"Audio enhancements are off on {where}";
    }

    /// <summary>Equalizer APO's Device Selector is what fixes a device it is missing on (registers it, puts it on the device, turns enhancements on).</summary>
    public static bool DeviceSelectorFixes(ApoState apo) => apo.UpstreamInstalled && apo.UpstreamConfigFolder is not null;

    /// <summary>
    /// A row's status once its chain is written to <paramref name="folder"/>: all OmniControl can know (AGENTS.md "Honest reporting")
    /// is that the chain was sent, not that Windows applied it.
    /// </summary>
    public static string ApoSent(ApoState apo, string folder) =>
        string.Equals(folder, apo.UpstreamConfigFolder, StringComparison.OrdinalIgnoreCase) ? "Chain sent to your Equalizer APO" : "Chain sent to OmniControl APO";

    /// <summary>Equalizer APO's config.txt no longer includes OmniControl's file (its editor or another program wrote it back without the line).</summary>
    public const string ApoDisplaced = "Equalizer APO's config no longer includes OmniControl's effects";

    /// <summary>An IR Loader's Auto level is measured before its chain is written for Equalizer APO.</summary>
    public const string ApoMeasuringIr = "Measuring the IR file's level before sending the chain";

    /// <summary>The tag beside a chain run by Equalizer APO whose config.txt also runs effects of its own, and the tag's tooltip.</summary>
    public const string ApoOwnEffects = "Equalizer APO's own effects also run",
        ApoOwnEffectsTip = "config.txt holds effects of its own besides OmniControl's line; Equalizer APO runs them as well.";

    /// <summary>The tag in the FX window while a plug-in's meters or an equaliser's spectrum are fed with a capture of what you hear, and its tooltip.</summary>
    public const string DisplayFed = "Display fed with what you hear",
        DisplayFedTip = "Equalizer APO runs this effect inside Windows, where OmniControl can't see its sound, so the meters and analysers here are fed with a capture of the device, after Equalizer APO. "
            + "The capture is level-matched to the full-volume signal (scaled back up by the device's volume). "
            + "It is used for the display only, never played or sent anywhere, and may differ from what Equalizer APO is doing: dynamics meters such as gain reduction read the already-processed sound.",
        DisplayNotFed = "Display not fed: device muted or too quiet",
        DisplayNotFedTip = "The capture comes after the device's volume, so while the device is muted or its volume is nearly zero there is no sound to level-match, "
            + "and nothing is fed to the meters and analysers (they hold their last picture).";

    /// <summary>The setting that puts OmniControl's include line and file back when another program takes them out.</summary>
    public const string KeepInPlace = "Keep OmniControl's effects in place",
        KeepInPlaceTip = "When Equalizer APO's editor or another program writes its config without OmniControl's line, put it back at once.";

    /// <summary>What a row or card says when no cable is installed, when every installed cable is switched off, or when every allowed cable is in use.</summary>
    public const string NeedsCable = "Needs VB-CABLE", CablesOff = "Cables switched off", NeedsAnotherCable = "Needs another cable";

    /// <summary>Whether a row's status says it has no cable (the Wiring tab shows it in the row's Cable column).</summary>
    public static bool IsCableMissing(StatusKind kind) => kind is StatusKind.NeedsCable or StatusKind.CablesOff or StatusKind.NeedsAnotherCable or StatusKind.NoFreeCable;

    /// <summary>A whole cable pair is installed (allowed or switched off).</summary>
    public static bool CableInstalled(AudioEffectsSupport support) => support.Cables.Count > 0 || support.Found.Any(f => f.IsPair);

    /// <summary>
    /// Why no cable can be given out at all: <see cref="StatusKind.NeedsCable"/> (none installed) or <see cref="StatusKind.CablesOff"/>
    /// (every one switched off); <see cref="StatusKind.Other"/> when at least one cable is allowed.
    /// </summary>
    public static StatusKind NoCableKind(AudioEffectsSupport support) =>
        support.Cables.Count > 0 ? StatusKind.Other : CableInstalled(support) ? StatusKind.CablesOff : StatusKind.NeedsCable;

    /// <summary>Why no cable can run anything here (<see cref="NoCableKind"/> in words), or null when at least one cable is allowed.</summary>
    public static string? CablesMissing(AudioEffectsSupport support) => NoCableKind(support) switch
    {
        StatusKind.NeedsCable => NeedsCable,
        StatusKind.CablesOff => CablesOff,
        _ => null,
    };

    /// <summary>How many cable pairs OmniControl may use (VB-CABLE's two inputs are one pair): the count every tag shows.</summary>
    public static int AllowedPairs(AudioEffectsSupport support) => VirtualCables.Pairs(support.Cables).Count;

    /// <summary>The cable status line: how many virtual cables may be used, or what is missing.</summary>
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
