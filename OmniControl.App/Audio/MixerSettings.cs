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
    /// <summary>Endpoint id of the chosen playback device; empty = the default device.</summary>
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    /// <summary>The FX chain is switched on (green power button).</summary>
    public bool FxOn { get; set; }
    public List<FxSlot> Fx { get; set; } = new();

    // Read only to convert files written before FX chains (a single equaliser); never written back.
    public bool? EqEnabled { get; set; }
    public List<EqBand>? EqBands { get; set; }
    public int[]? EqGains { get; set; }
}

/// <summary>Mixer preferences, stored as JSON in %APPDATA%\OmniControl\mixer.json.</summary>
public sealed class MixerSettings
{
    public List<AppAudioEntry> Apps { get; set; } = new();
    public bool MasterFxOn { get; set; }
    public List<FxSlot> MasterFx { get; set; } = new();

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
        foreach (var a in s.Apps)
        {
            if (a.Fx.Count == 0 && ConvertLegacy(a.EqBands, a.EqGains) is { } slot)
            {
                a.Fx.Add(slot);
                a.FxOn = a.EqEnabled ?? false;
            }
            a.EqBands = null; a.EqGains = null; a.EqEnabled = null;
        }
        return s;
    }

    /// <summary>An equaliser slot from an old single-EQ file, or null when that EQ was never changed.</summary>
    private static FxSlot? ConvertLegacy(List<EqBand>? bands, int[]? gains)
    {
        List<EqBand>? converted = bands is { Count: Eq.Bands } && bands.Any(b => b.Gain != 0) ? bands
            : gains is { Length: > 0 } && gains.Any(g => g != 0) ? Eq.FromLegacy(gains)
            : null;
        if (converted is null) return null;
        var slot = FxCatalog.Create("eq");
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
}
