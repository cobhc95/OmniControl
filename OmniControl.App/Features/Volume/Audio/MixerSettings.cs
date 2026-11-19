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

    /// How the effects run (<see cref="EffectsRules.ProgramRoutes"/> id): empty = Automatic (a free cable, else recording;
    /// files written before the choice existed load as Automatic), "cable" or "capture".
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

    /// How the master runs (<see cref="EffectsRules.MasterRoutes"/> id): empty = Automatic (files written before the choice
    /// existed load as Automatic), "bus" = the cable bus, "apo" = Equalizer APO on the speakers.
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

    /// Virtual cables the user switched off (<see cref="Core.Engine.FoundCable.Id"/>): OmniControl never gives them out. Empty = every cable allowed.
    public List<string> ForbiddenCables { get; set; } = new();

    /// Device pairs the user marked as cables (Wiring > Virtual cables > "This device is a cable..."); files without it load as none.
    public List<Core.Engine.CableMark> MarkedCables { get; set; } = new();

    private static readonly DataStore Store = DataStores.Mixer;

    public static MixerSettings Load()
    {
        var s = AppDataStore.Load<MixerSettings>(Store) ?? new();
        s.Apps = MergeDuplicates(s.Apps);
        UpgradeBuiltIns(s.MasterFx.Concat(s.Apps.SelectMany(a => a.Fx)).Concat(s.Inputs.SelectMany(i => i.Fx)).Concat(s.Combined.SelectMany(c => c.Fx)));
        return s;
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

    /// The cable its processed sound last went to so apps that picked that cable keep finding it; empty = none yet.
    public string Cable { get; set; } = string.Empty;
}
