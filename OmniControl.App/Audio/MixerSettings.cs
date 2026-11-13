using System.Text.RegularExpressions;
using OmniControl.App.Services;

namespace OmniControl.App.Audio;

/// <summary>What OmniControl remembers about one program's sound, kept even while the program is closed.</summary>
public sealed class AppAudioEntry
{
    /// <summary>Lower-case path of the program's .exe (its identity).</summary>
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public int Volume { get; set; } = 100;
    /// <summary>Left/right balance, -100..100.</summary>
    public int Pan { get; set; }
    public bool Muted { get; set; }

    /// <summary>
    /// Where the program plays: endpoint ids and combined devices (<see cref="CombinedDevice.KeyPrefix"/>); empty = it follows the
    /// master (Windows' default device, or the master bus in Full control).
    /// </summary>
    public List<string> PlaysOn { get; set; } = new();

    /// <summary>The effects are switched on.</summary>
    public bool FxOn { get; set; }
    public List<FxSlot> Fx { get; set; } = new();

    // Read only to convert older files (MixerMigration); written back empty.
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? FxMode { get; set; }

    // Read only to convert files written before FX chains (a single equaliser); never written back.
    public bool? EqEnabled { get; set; }
    public List<EqBand>? EqBands { get; set; }
    public int[]? EqGains { get; set; }

    // Store apps live in a folder named after their version (...\WindowsApps\Publisher.App_1.2.3.0_x64__id\), which changes on every update.
    private static readonly Regex PackageVersion = new(@"(\\windowsapps\\[^\\_]+)_[^\\_]+(_[^\\_]*__)", RegexOptions.Compiled);

    /// <summary>The identity of a program: its lower-case .exe path, without the version folder of a Store app so an update stays the same program.</summary>
    public static string KeyFor(string path) => PackageVersion.Replace(path.ToLowerInvariant(), "$1$2");

    /// <summary>How much the user has set on this program; decides which of two duplicates is kept.</summary>
    internal int Customisation => Fx.Count + (Pan != 0 ? 1 : 0) + (Volume != 100 ? 1 : 0) + (Muted ? 1 : 0) + PlaysOn.Count
        + (string.IsNullOrEmpty(DeviceId) ? 0 : 1) + (string.IsNullOrEmpty(FxMode) ? 0 : 1);
}

/// <summary>Mixer preferences, stored as JSON in %APPDATA%\OmniControl\mixer.json.</summary>
public sealed class MixerSettings
{
    public List<AppAudioEntry> Apps { get; set; } = new();
    public bool MasterFxOn { get; set; }
    public List<FxSlot> MasterFx { get; set; } = new();

    /// <summary>The sound mode (<see cref="SoundModes"/> id); empty in files written before modes were global (MixerMigration fills it).</summary>
    public string SoundMode { get; set; } = string.Empty;

    /// <summary>The master's devices when it plays on more than one (Full control); otherwise it plays on Windows' default device.</summary>
    public List<string> MasterPlaysOn { get; set; } = new();

    /// <summary>Last known names of devices by endpoint id, so a "plays on" list can name a device that is unplugged now.</summary>
    public Dictionary<string, string> DeviceNames { get; set; } = new();

    /// <summary>Devices the user combined (Full control).</summary>
    public List<CombinedDevice> Combined { get; set; } = new();

    // Read only to convert older files (MixerMigration); written back empty.
    public string? MasterFxMode { get; set; }

    /// <summary>
    /// What the last "Test effects on this program" found about Capture and replay on this PC (<see cref="Engine.CaptureTestResult"/>):
    /// empty = never tested (the engine tries muting and falls back if the capture goes silent).
    /// </summary>
    public string CaptureSilence { get; set; } = string.Empty;

    /// <summary>FX chains of recording devices (keyed by endpoint id), kept even while the device is unplugged.</summary>
    public List<InputFxEntry> Inputs { get; set; } = new();

    // Legacy (see AppAudioEntry).
    public bool? MasterEqEnabled { get; set; }
    public List<EqBand>? MasterEqBands { get; set; }
    public int[]? MasterEqGains { get; set; }

    private const string FileName = "mixer.json";

    public static MixerSettings Load()
    {
        var s = AppDataStore.Load<MixerSettings>(FileName) ?? new();
        if (s.MasterFx.Count == 0 && ConvertLegacy(s.MasterEqBands, s.MasterEqGains) is { } master)
        {
            s.MasterFx.Add(master);
            s.MasterFxOn = s.MasterEqEnabled ?? false;
        }
        s.MasterEqBands = null; s.MasterEqGains = null; s.MasterEqEnabled = null;
        s.Apps = MergeDuplicates(s.Apps);
        foreach (var a in s.Apps)
        {
            if (a.Fx.Count == 0 && ConvertLegacy(a.EqBands, a.EqGains) is { } slot)
            {
                a.Fx.Add(slot);
                a.FxOn = a.EqEnabled ?? false;
            }
            a.EqBands = null; a.EqGains = null; a.EqEnabled = null;
        }
        UpgradeBuiltIns(s.MasterFx.Concat(s.Apps.SelectMany(a => a.Fx)).Concat(s.Inputs.SelectMany(i => i.Fx)).Concat(s.Combined.SelectMany(c => c.Fx)));
        if (MixerMigration.Run(s)) s.Save();
        return s;
    }

    /// <summary>
    /// Built-in effects are known by their kind, never by their name: each takes today's name from its catalog entry (an "Equaliser"
    /// saved before there were three reads "Equaliser - Parametric"), and an equaliser's bands keep their sound and gain what today's
    /// band model needs (<see cref="EqLayout.Upgrade"/>).
    /// </summary>
    private static void UpgradeBuiltIns(IEnumerable<FxSlot> slots)
    {
        foreach (var slot in slots)
        {
            if (FxCatalog.KindOf(slot) is not { } kind) continue;
            slot.Name = kind.Name;
            if (kind.Eq is { } eq) slot.Bands = eq.Upgrade(slot.Bands);
        }
    }

    /// <summary>One entry per program: files written before updates were recognised hold one entry per app version. The most customised entry wins (the newest on a tie) and takes the newest path.</summary>
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

    /// <summary>An equaliser slot from an old single-EQ file, or null when that EQ was never changed.</summary>
    private static FxSlot? ConvertLegacy(List<EqBand>? bands, int[]? gains)
    {
        List<EqBand>? converted = bands is { Count: Eq.LegacyBandCount } && bands.Any(b => b.Gain != 0) ? bands
            : gains is { Length: > 0 } && gains.Any(g => g != 0) ? Eq.FromLegacy(gains)
            : null;
        if (converted is null) return null;
        var slot = FxCatalog.Create(FxCatalog.EqKind);
        slot.Bands = converted;
        return slot;
    }

    public void Save() => AppDataStore.Save(FileName, this);
}

/// <summary>What OmniControl remembers about one recording device's FX chain.</summary>
public sealed class InputFxEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool FxOn { get; set; }
    public List<FxSlot> Fx { get; set; } = new();

    /// <summary>The cable its processed sound last went to (Full control), so apps that picked that cable keep finding it; empty = none yet.</summary>
    public string Cable { get; set; } = string.Empty;
}
