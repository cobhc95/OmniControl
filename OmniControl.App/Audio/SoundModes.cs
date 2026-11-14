using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Audio;

/// <summary>
/// The colour of a mode's tag: green for a plain advantage, no colour for a neutral fact, yellow for the one thing to keep in mind.
/// Red is kept for real failures and is never a tag.
/// </summary>
public enum TagTone { Good, Info, Caution }

/// <summary>One short fact about a sound mode ("Lowest delay"), its full wording for the tooltip, and its colour.</summary>
public sealed record ModeTag(string Text, string Tip, TagTone Tone)
{
    public bool IsGood => Tone == TagTone.Good;
    public bool IsCaution => Tone == TagTone.Caution;
}

/// <summary>What a mode's card offers to install or fix (<see cref="Engine.EngineInstaller"/> carries it out).</summary>
public enum ModeSetup
{
    /// <summary>Equalizer APO: the user's own installation is used as it is; its installer, Device Selector or download page otherwise.</summary>
    EqualizerApo,
    /// <summary>VB-CABLE: its official installer when it was placed next to the app, else its download page.</summary>
    VirtualCable,
}

/// <summary>
/// The Volume controls whose use depends on the sound mode (the logic matrix in HANDOVER.md, "Volume page Design D"). Effects on the
/// master and on programs are not here: both modes run them, and the row's status says what really happens.
/// </summary>
public enum SoundFeature
{
    /// <summary>The master plays on more than one device, or on a combined device.</summary>
    MasterSeveralOutputs,
    /// <summary>A program plays on more than one device.</summary>
    ProgramSeveralOutputs,
    /// <summary>A program or the master plays on a combined device.</summary>
    CombinedOutputs,
    /// <summary>Combining speakers into one (and its row).</summary>
    CombineSpeakers,
    /// <summary>Combining microphones into one (and its row, level and effects): needs only a cable apps can record from, in either mode.</summary>
    CombineMicrophones,
    /// <summary>Effects on a microphone (one recording device; a combined microphone is <see cref="CombineMicrophones"/>).</summary>
    MicEffects,
}

/// <summary>What this PC has, as the modes need it: what Windows offers, and whether recording one program keeps working when it is muted.</summary>
public sealed record SoundFacts(AudioEffectsSupport Support, bool CaptureWorks, bool CaptureTested);

/// <summary>One line of a mode's plain status ("Ready", "Needs VB-CABLE"); the tone colours its tag; the tip is the detail.</summary>
public sealed record ModeStatus(string Text, TagTone Tone, string Tip)
{
    public bool IsGood => Tone == TagTone.Good;
    public bool IsCaution => Tone == TagTone.Caution;
}

/// <summary>
/// Whether a control works in the current mode: null reason = it does; otherwise why not and the mode that would allow it.
/// <see cref="Note"/> is the same fact as one short calm sentence for a small pop-down (null = use the reason).
/// </summary>
public sealed record Gate(string? Reason, SoundMode? SwitchTo, string? Note = null)
{
    public static readonly Gate Open = new(null, null);
    public bool Allowed => Reason is null;
}

/// <summary>
/// How a mode runs effects for one kind of row: a few words ("by recording"), its expected delay ("about 30-80 ms delay") and the
/// engine that processes the sound (what an effect needs to run there: <see cref="FxKind.Engines"/>).
/// </summary>
public sealed record ModeMethod(string How, string Delay, FxEngines Engine)
{
    public override string ToString() => $"{How} · {Delay}";
}

/// <summary>
/// One sound mode (an entry of the <see cref="SoundModes"/> master list): its stored id, name, one plain line for its card,
/// <see cref="Details"/> (the technical wording, for the tooltip), tags, how it runs the master's, a microphone's and a program's
/// effects, what its card can set up, and its status on this PC.
/// </summary>
public sealed record SoundMode(
    string Id,
    string Name,
    string Description,
    string Details,
    IReadOnlyList<ModeTag> Tags,
    ModeMethod Master,
    ModeMethod Microphone,
    ModeMethod Program,
    ModeSetup Setup,
    Func<SoundFacts, IReadOnlyList<ModeStatus>> Status)
{
    /// <summary>
    /// How this mode runs the effects of the row <paramref name="key"/>: the master (null or <see cref="MixerService.MasterKey"/>), a
    /// microphone (a recording device), or anything else (programs, combined devices).
    /// </summary>
    public ModeMethod MethodFor(string? key) =>
        key is null || key == MixerService.MasterKey ? Master : MixerService.IsInput(key) ? Microphone : Program;

    /// <summary>One line for the mode's card: each method once, with the rows that use it ("Master and microphones: in Windows ...").</summary>
    public string MethodSummary
    {
        get
        {
            var rows = new[] { (Name: "Master", Method: Master), (Name: "microphones", Method: Microphone), (Name: "programs", Method: Program) };
            var groups = rows.GroupBy(r => r.Method).ToList();
            if (groups.Count == 1) return $"Effects {Master}";
            return string.Join(" ", groups.Select(g =>
            {
                var names = string.Join(" and ", g.Select(r => r.Name));
                return $"{char.ToUpperInvariant(names[0])}{names[1..]}: {g.Key}.";
            }));
        }
    }
}

/// <summary>
/// The master list of sound modes (AGENTS.md "One master list"): the Sound mode panel, the search card, the "Switch sound mode"
/// hotkey, the effects engine and the greyed-out controls all read it. One global mode applies to every row. <see cref="Check"/> is
/// the logic matrix: which controls work in which mode. Wording stays short and calm; technical names only in <see cref="SoundMode.Details"/>.
/// </summary>
public static partial class SoundModes
{
    /// <summary>The delay of anything that is recorded and played again (a program in Fast mode, everything in Full control).</summary>
    private const string ReplayDelay = "30-80 ms";

    public static readonly SoundMode Fast = new(
        "fast", "Fast",
        "Lowest delay: effects in Windows, programs recorded.",
        $"Master and microphone effects run inside Windows' audio engine through your Equalizer APO (an APO), with no added delay of its own: the master on the default playback device, a microphone when Equalizer APO is on it (ticked on the Capture tab of its Device Selector). Equalizer APO runs the equaliser, gain, IR loader and VST2 plug-ins (with their settings saved in OmniControl); VST3 plug-ins are kept but not run there. A program's effects record the program (Windows' process loopback capture), silence the original (muted, or turned down to near silence where the recording follows its volume) and play the processed copy, about {ReplayDelay} later. The master plays on one device; combining speakers needs Full control. Combined microphones are mixed by OmniControl into a virtual cable, which apps record from.",
        new[]
        {
            new ModeTag("Lowest delay", "Master effects add no noticeable delay.", TagTone.Good),
            new ModeTag("Some protected apps may not play", "Equalizer APO switches off Windows' protected audio path.", TagTone.Caution),
        },
        InWindows(),
        InWindows(),
        new ModeMethod("by recording", $"about {ReplayDelay} delay", FxEngines.Program),
        ModeSetup.EqualizerApo,
        FastStatus);

    public static readonly SoundMode Full = new(
        "full", "Full control",
        "Through a virtual cable: any routing, combining and effects.",
        $"Sound is sent to a virtual sound device (VB-CABLE) and OmniControl's engine processes it and plays it where you choose: several devices at once, combined speakers or microphones, effects on the master, programs and microphones. Each combined device and processed microphone needs its own cable; a processed program uses a free cable, or is recorded (Windows' process loopback capture, its own sound turned down to near silence) when none is free or when you choose recording for it. On the master's default method (the cable bus) the master's own cable becomes Windows' default playback device, so every program and system sound goes through OmniControl, which plays it on your real speakers; when Full control ends the default goes back to them. With the master's method set to Equalizer APO the master's effects run in Windows on the speakers instead, which stay the default, and the cable is free for a program, a combined microphone or combined speakers. Adds a little delay (about {ReplayDelay}).",
        new[]
        {
            new ModeTag($"{ReplayDelay} delay", "Fine for music, noticeable in games and calls.", TagTone.Info),
            new ModeTag("Uses VB-CABLE", "Needs the free VB-CABLE driver (vb-cable.com); one cable per combined device or processed microphone. A processed program uses a free cable, or is recorded when none is free.", TagTone.Info),
        },
        ThroughCable(),
        ThroughCable(),
        new ModeMethod("through a cable or by recording", $"about {ReplayDelay} delay", FxEngines.Program),
        ModeSetup.VirtualCable,
        FullStatus);

    private static ModeMethod ThroughCable() => new("through the cable", $"about {ReplayDelay} delay", FxEngines.Program);

    /// <summary>
    /// A recorded program that is open but silent: its original is already turned down and the recording waits, so the effects apply from
    /// its first sound.
    /// </summary>
    public const string EffectsReady = "Effects ready for when it plays";
    private static ModeMethod InWindows() => new("in Windows (Equalizer APO)", "no extra delay", FxEngines.Apo);

    /// <summary>How a row runs when its chain is in Equalizer APO outside Fast mode: the master on the speakers (Full control, method Equalizer APO).</summary>
    public static ModeMethod ApoOnSpeakers { get; } = InWindows();

    /// <summary>A program on a cable with Equalizer APO on it (<see cref="RouteApoCable"/>): Equalizer APO runs the chain, the cable adds its own short delay.</summary>
    public static ModeMethod ApoOnCable { get; } = new("in Equalizer APO on its cable", "a cable's short delay", FxEngines.Apo);

    /// <summary>Every mode, in the order the panel lists them.</summary>
    public static IReadOnlyList<SoundMode> All { get; } = new[] { Fast, Full };

    /// <summary>The mode before the user picks one (and for a stored id that no longer exists).</summary>
    public static SoundMode Default => Fast;

    public static SoundMode Find(string? id) => All.FirstOrDefault(m => m.Id == id) ?? Default;

    /// <summary>The mode after <paramref name="current"/>, wrapping round.</summary>
    public static SoundMode Next(SoundMode current) => All[Services.Wrap.Index(IndexOf(current) + 1, All.Count)];

    private static int IndexOf(SoundMode mode) => All.ToList().IndexOf(mode);

    private const string NeedsFull = "Needs Full control mode";

    /// <summary>The short caution in the Wiring Cable column of a combined speaker while Fast mode cannot run it.</summary>
    public const string NeedsFullTag = "Needs Full control";

    /// <summary>
    /// Why combined speakers (and anything playing on them) need Full control: the sound sent to them goes through a virtual cable, and
    /// Fast mode does not route the master or programs through one.
    /// </summary>
    public const string CombinedNeedsFull = NeedsFull + ": combined speakers run through a virtual cable";

    /// <summary>A combined microphone with none of its members plugged in.</summary>
    public const string NoMicrophonePluggedIn = "None of its microphones is plugged in";

    /// <summary>A processed or combined microphone's running words: what apps pick as their microphone (Windows' name, not ours).</summary>
    public static string AppsRecordFrom(string recordingName) => $"Apps record it from {recordingName}";

    /// <summary>Why a microphone waits for a cable in Full control while the master bus holds the only pair apps can record from.</summary>
    public static string BusHoldsCable(string cableName) => $"The master bus uses {cableName}. In Fast mode it is free.";

    /// <summary>The tag and its tooltip on a recording device that records what the PC plays (<see cref="LoopbackInputs"/>, "Stereo Mix").</summary>
    public const string RecordsPlaybackTag = "Records the PC's sound", RecordsPlaybackTip = "In calls others may hear themselves.";

    /// <summary>
    /// The logic matrix: whether <paramref name="feature"/> works in <paramref name="mode"/> on this PC. Full control allows every
    /// feature (each row's status says when a cable is missing); Fast keeps the master on one device and does not route sound through
    /// a cable, so it can't combine speakers; combined microphones work in both (the row's status says when a cable is missing); a
    /// program on several devices in Fast needs its sound to be recordable; microphone effects in Fast need Equalizer APO (whether it
    /// is on that microphone is the row's status, with Choose devices).
    /// </summary>
    public static Gate Check(SoundFeature feature, SoundMode mode, SoundFacts? facts)
    {
        if (mode == Full) return Gate.Open;
        return feature switch
        {
            SoundFeature.MasterSeveralOutputs => new Gate($"{NeedsFull}: in Fast mode the master plays on one device", Full, "Fast mode plays on one device."),
            SoundFeature.ProgramSeveralOutputs => facts is null || facts.CaptureWorks ? Gate.Open
                : new Gate($"{NeedsFull}: recording a program doesn't work on this PC", Full, "Recording a program doesn't work on this PC."),
            SoundFeature.MicEffects => facts is null || MicApoPossible(facts.Support.Apo) ? Gate.Open
                : new Gate("Fast mode runs microphone effects in Equalizer APO, which is not installed", Full, "Needs Equalizer APO."),
            SoundFeature.CombineMicrophones => Gate.Open,
            _ => new Gate(CombinedNeedsFull, Full),
        };
    }

    /// <summary>Why Fast mode can't run program effects here, or null when it can (or nobody has tested yet).</summary>
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

    /// <summary>Why a microphone's effects can't run in Equalizer APO on that recording device, or null when they can (the same checks).</summary>
    public static string? MicApoMissing(ApoState apo, string endpointId) => EndpointMissing(apo, apo.MicrophoneOf(endpointId), "this microphone");

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

    /// <summary>
    /// Fast mode can process microphones at all: Equalizer APO is installed (its Device Selector can put it on a microphone), or an APO
    /// already runs on one. Only when neither is true are microphone effects greyed.
    /// </summary>
    private static bool MicApoPossible(ApoState apo) => apo.UpstreamInstalled || apo.Microphones.Any(m => apo.FolderFor(m) is not null);

    /// <summary>Equalizer APO's Device Selector is what fixes a device it is missing on (registers it, puts it on the device, turns enhancements on).</summary>
    public static bool DeviceSelectorFixes(ApoState apo) => apo.UpstreamInstalled && apo.UpstreamConfigFolder is not null;

    /// <summary>What the master uses in Fast mode when it can run ("Using your Equalizer APO"), else null.</summary>
    public static string? ApoInUse(ApoState apo) => apo.UsesUpstream ? "Using your Equalizer APO" : apo.Ready ? "Using OmniControl APO" : null;

    /// <summary>
    /// A row's status once its chain is written to <paramref name="folder"/>: all OmniControl can know (AGENTS.md "Honest reporting")
    /// is that the chain was sent, not that Windows applied it.
    /// </summary>
    public static string ApoSent(ApoState apo, string folder) =>
        string.Equals(folder, apo.UpstreamConfigFolder, StringComparison.OrdinalIgnoreCase) ? "Chain sent to your Equalizer APO" : "Chain sent to OmniControl APO";

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

    /// <summary>Why Full control can't run anything here (<see cref="NoCableKind"/> in words), or null when at least one cable is allowed.</summary>
    public static string? CablesMissing(AudioEffectsSupport support) => NoCableKind(support) switch
    {
        StatusKind.NeedsCable => NeedsCable,
        StatusKind.CablesOff => CablesOff,
        _ => null,
    };

    /// <summary>How many cable pairs OmniControl may use (VB-CABLE's two inputs are one pair): the count every tag shows.</summary>
    public static int AllowedPairs(AudioEffectsSupport support) => VirtualCables.Pairs(support.Cables).Count;

    private static IReadOnlyList<ModeStatus> FastStatus(SoundFacts facts)
    {
        var apo = facts.Support.Apo;
        var master = ApoInUse(apo) is { } inUse
            ? new ModeStatus($"Master: {inUse}", TagTone.Good, "Master effects run in Equalizer APO on the default playback device.")
            : new ModeStatus($"Master: {ApoMissing(apo)}", TagTone.Caution, "Needs Equalizer APO on the default playback device.");
        var programs = CaptureMissing(facts) is { } missing
            ? new ModeStatus($"Programs: {missing}", TagTone.Caution, "Fast mode records a program and silences the original.")
            : facts.CaptureTested
                ? new ModeStatus("Programs: ready", TagTone.Good, "The last Test found that a program can be recorded while its own sound is silenced.")
                : new ModeStatus("Programs: not tested yet", TagTone.Info, "Press Test on a playing program's row to check.");
        return new[] { master, programs, MicrophonesStatus(apo) };
    }

    /// <summary>Fast mode's microphones: on how many Equalizer APO runs, or what it needs. Never a caution: most PCs need no microphone effects.</summary>
    private static ModeStatus MicrophonesStatus(ApoState apo)
    {
        var count = apo.Microphones.Count(m => apo.FolderFor(m) is not null);
        if (count > 0)
            return new ModeStatus($"Microphones: Equalizer APO on {count}", TagTone.Good,
                "Microphone effects run in Equalizer APO on the microphones ticked on the Capture tab of its Device Selector.");
        return apo.UpstreamInstalled
            ? new ModeStatus("Microphones: Equalizer APO is on none", TagTone.Info,
                "Tick it on the Capture tab of Equalizer APO's Device Selector.")
            : new ModeStatus("Microphones: need Equalizer APO", TagTone.Info, "Fast mode runs microphone effects in Equalizer APO.");
    }

    private static IReadOnlyList<ModeStatus> FullStatus(SoundFacts facts)
    {
        var count = AllowedPairs(facts.Support);
        var names = facts.Support.Cables.Where(c => c.RecordingId is not null).Select(c => c.Name); // one per pair: the input with its own recording side
        return new[]
        {
            CablesMissing(facts.Support) is { } missing
                ? new ModeStatus(missing, TagTone.Caution, missing == NeedsCable ? "Install VB-CABLE first." : "Switch a cable on under Virtual cables.")
                : new ModeStatus(count == 1 ? "Ready · 1 virtual cable" : $"Ready · {count} virtual cables", TagTone.Good,
                    "Allowed: " + string.Join(", ", names) + ". Each combined device or processed microphone uses one; a processed program uses one when free, else it is recorded."),
        };
    }
}
