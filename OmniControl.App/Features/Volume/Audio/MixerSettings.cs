using System.Text.RegularExpressions;

namespace OmniControl.App.Features.Volume.Audio;

/// What OmniControl remembers about one program's sound, kept even while the program is closed.
public sealed class AppAudioEntry
{
    /// Lower-case path of the program's .exe (its identity).
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public int Volume { get; set; } = 100;
    /// Left/right balance, -100..100.
    public int Pan { get; set; }
    public bool Muted { get; set; }

    /// The user added this program by hand ("Add program"): its row stays until they remove it, even with nothing set.
    public bool Added { get; set; }

    /// Where the program plays: endpoint ids and combined devices (<see cref="CombinedDevice.KeyPrefix"/>); empty = it follows the
    /// master (Windows' default device, or the master bus).
    public List<string> PlaysOn { get; set; } = new();

    /// The effects are switched on.
    public bool FxOn { get; set; }
    /// The user switched the effects off themselves, so adding an effect does not switch them back on (<see cref="MixerService.EffectsAdded"/>).
    public bool FxUserOff { get; set; }
    public List<FxSlot> Fx { get; set; } = new();

    /// How the effects run (<see cref="EffectsRules.ProgramRoutes"/> id): empty = Off (the default: nothing routed), "auto" = Automatic
    /// (a free cable, else recording), "cable", "capture" or "apo-cable".
    public string FxRoute { get; set; } = string.Empty;

    // Store apps live in a folder named after their version (...\WindowsApps\Publisher.App_1.2.3.0_x64__id\), which changes on every update.
    private static readonly Regex PackageVersion = new(@"(\\windowsapps\\[^\\_]+)_[^\\_]+(_[^\\_]*__)", RegexOptions.Compiled);

    /// The identity of a program: its lower-case .exe path, without the version folder of a Store app so an update stays the same program.
    public static string KeyFor(string path) => PackageVersion.Replace(path.ToLowerInvariant(), "$1$2");

    /// True when the program is worth a row while it is closed: the user added it, or set something on it (effects, route, level, balance, mute).
    internal bool KeepsRow => Added || Customisation > 0;

    /// How much the user has set on this program; decides which of two duplicates is kept.
    internal int Customisation => Fx.Count + (Added ? 1 : 0) + (Pan != 0 ? 1 : 0) + (Volume != 100 ? 1 : 0) + (Muted ? 1 : 0) + PlaysOn.Count
        + (FxRoute.Length > 0 ? 1 : 0);
}

/// Mixer preferences, stored as JSON in %APPDATA%\OmniControl\mixer.json.
public sealed class MixerSettings
{
    public List<AppAudioEntry> Apps { get; set; } = new();
    public bool MasterFxOn { get; set; }
    /// The user switched the effects off themselves, so adding an effect does not switch them back on (<see cref="MixerService.EffectsAdded"/>).
    public bool MasterFxUserOff { get; set; }
    public List<FxSlot> MasterFx { get; set; } = new();

    /// The master's devices when it plays on more than one; otherwise it plays on Windows' default device.
    public List<string> MasterPlaysOn { get; set; } = new();

    /// The real playback device (endpoint id) the master plays on, where Windows' default device is the master bus's
    /// cable; it is the one Windows' default was when the master bus started, or the one chosen since. Empty = none remembered yet.
    public string RealMasterDevice { get; set; } = string.Empty;

    /// The virtual cable (endpoint id) OmniControl made Windows' default playback device for the master bus, to be put back to
    /// <see cref="RealMasterDevice"/> when the master bus ends; empty = OmniControl changed nothing. Kept in the file so a crash is
    /// repaired at the next start.
    public string BusCable { get; set; } = string.Empty;

    /// How the master runs (<see cref="EffectsRules.MasterRoutes"/> id): empty = Off (the default: nothing routed), "auto" = Automatic,
    /// "bus" = the cable bus, "apo" = Equalizer APO on the speakers.
    public string MasterMethod { get; set; } = string.Empty;

    /// Last known names of devices by endpoint id, so a "plays on" list can name a device that is unplugged now.
    public Dictionary<string, string> DeviceNames { get; set; } = new();

    /// Devices the user combined.
    public List<CombinedDevice> Combined { get; set; } = new();

    /// What the last "Test effects on this program" found about Capture and replay on this PC (<see cref="Engine.CaptureTestResult"/>):
    /// empty = never tested (the engine tries muting and falls back if the capture goes silent).
    public string CaptureSilence { get; set; } = string.Empty;

    /// "Keep OmniControl's effects in place": when Equalizer APO's editor or another program writes its config without OmniControl's line,
    /// it is put back at once (<see cref="Engine.ApoKeeper"/>). Off: it is left out and the rows say so. Files without it load as on.
    public bool KeepFxInPlace { get; set; } = true;

    /// FX chains of recording devices (keyed by endpoint id), kept even while the device is unplugged.
    public List<InputFxEntry> Inputs { get; set; } = new();

    /// Virtual cables the user switched on (<see cref="Core.Engine.FoundCable.Id"/>): the only ones OmniControl gives out. A cable already on
    /// the PC is often another program's (a mixer on VB-CABLE), so none is taken until switched on. Null only in a file written before
    /// (<see cref="Load"/>, <see cref="MixerService.KeepEarlierCables"/>).
    public List<string>? AllowedCables { get; set; }

    /// Read only from a file written before cables were switched on by hand, when every cable but these was used; never written again.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ForbiddenCables { get; set; }

    /// Device pairs the user marked as cables (Wiring > Virtual cables > "This device is a cable..."); files without it load as none.
    public List<Core.Engine.CableMark> MarkedCables { get; set; } = new();

    /// "Use microphones" (the Microphones group's switch): off, OmniControl opens no recording device (no meter, no effects, no Listen).
    /// Null only in a file written before the switch existed (and in presets taken then): <see cref="Load"/> turns it on when a microphone already had effects.
    public bool? UseMicrophones { get; set; }

    internal bool MicrophonesOn => UseMicrophones == true;

    /// Something set here may run through a cable: an effect on any row, a combined device, or the master or a program on several devices.
    private bool MayNeedCables =>
        MasterFx.Count > 0 || Apps.Any(a => a.Fx.Count > 0 || a.PlaysOn.Count > 1) || Inputs.Any(i => i.Fx.Count > 0) || Combined.Count > 0 || MasterPlaysOn.Count > 1;

    private static readonly DataStore Store = DataStores.Mixer;

    public static MixerSettings Load()
    {
        var s = AppDataStore.Load<MixerSettings>(Store) ?? new();
        s.UseMicrophones ??= s.Inputs.Any(i => FxSlot.AnyRuns(i.FxOn, i.Fx)) || s.Combined.Any(c => c.Kind == CombinedKind.Microphones);
        if (s.AllowedCables is null)
        {
            // A file from before: methods move to Off's ids; with nothing set that needs a cable, no cable is kept, otherwise the cables
            // it used stay on (KeepEarlierCables).
            UpgradeMethods(s);
            if (!s.MayNeedCables) (s.AllowedCables, s.ForbiddenCables) = (new(), null);
        }
        s.Apps = MergeDuplicates(s.Apps);
        UpgradeBuiltIns(s.MasterFx.Concat(s.Apps.SelectMany(a => a.Fx)).Concat(s.Inputs.SelectMany(i => i.Fx)).Concat(s.Combined.SelectMany(c => c.Fx)));
        return s;
    }

    /// Settings written before Off (its id is the empty one Automatic had; <see cref="AllowedCables"/> null marks them, also an older preset or
    /// restore copy): a row on Automatic that has effects, or plays on several devices, stays Automatic under its new id; one with nothing
    /// to run becomes Off, so nothing is routed for it. Chosen methods stay.
    internal static void UpgradeMethods(MixerSettings s)
    {
        static string Upgraded(string id, bool uses) => id.Length == 0 && uses ? EffectsRules.AutomaticId : id;
        s.MasterMethod = Upgraded(s.MasterMethod, s.MasterFx.Count > 0 || s.MasterPlaysOn.Count > 1 || s.MasterPlaysOn.Any(CombinedDevice.IsKey));
        foreach (var app in s.Apps) app.FxRoute = Upgraded(app.FxRoute, app.Fx.Count > 0 || app.PlaysOn.Count > 1);
        foreach (var input in s.Inputs) input.Method = Upgraded(input.Method, input.Fx.Count > 0);
    }

    /// Built-in effects are known by their kind, never by their name: each takes today's name from its catalog entry (an "Equaliser"
    /// saved before there were three reads "Equaliser - Parametric"), and an equaliser's bands keep their sound and gain what today's
    /// band model needs (<see cref="EqLayout.Upgrade"/>).
    private static void UpgradeBuiltIns(IEnumerable<FxSlot> slots)
    {
        foreach (var slot in slots)
        {
            if (FxCatalog.KindOf(slot) is not { } kind) continue;
            slot.Name = kind.Name;
            if (kind.Eq is { } eq) slot.Bands = eq.Upgrade(slot.Bands);
        }
    }

    /// One entry per program: files written before updates were recognised hold one entry per app version. The most customised entry wins (the newest on a tie) and takes the newest path.
    private static List<AppAudioEntry> MergeDuplicates(List<AppAudioEntry> apps)
    {
        var merged = new List<AppAudioEntry>();
        foreach (var group in apps.GroupBy(a => AppAudioEntry.KeyFor(a.ExePath.Length > 0 ? a.ExePath : a.Key)))
        {
            var keep = group.Reverse().MaxBy(a => a.Customisation)!;
            keep.Key = group.Key;
            keep.ExePath = group.Last().ExePath;
            merged.Add(keep);
        }
        return merged;
    }

    /// Saved a moment later off the UI thread (every volume, pan and effect change saves; a drag writes the file once).
    public void Save() => AppDataStore.SaveSoon(Store, this);
}

/// What OmniControl remembers about one recording device's FX chain.
public sealed class InputFxEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool FxOn { get; set; }
    /// The user switched the effects off themselves, so adding an effect does not switch them back on (<see cref="MixerService.EffectsAdded"/>).
    public bool FxUserOff { get; set; }
    public List<FxSlot> Fx { get; set; } = new();

    /// How its effects run (<see cref="EffectsRules.MicRoutes"/> id): empty = Off (the default), "auto" = Automatic, "bus" = its cable, "apo" = Equalizer APO on it.
    public string Method { get; set; } = string.Empty;

    /// The cable its processed sound last went to so apps that picked that cable keep finding it; empty = none yet.
    public string Cable { get; set; } = string.Empty;
}
