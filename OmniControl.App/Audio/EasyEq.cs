namespace OmniControl.App.Audio;

/// <summary>One of the Easy page's friendly presets: the words on its chip and its five gains in dB (<see cref="EasyEq.Parts"/> order).</summary>
public sealed record EasyPreset(string Name, int[] Gains);

/// <summary>
/// The Easy mode's five-slider equaliser (Bass, Low mid, Mid, High mid, Treble) as a fixed layout of a Parametric equaliser's bands:
/// one definition, so the Easy sliders and the Advanced equaliser edit the same effect. The mapping goes both ways: gains to bands
/// (<see cref="Bands"/>) and bands back to gains (<see cref="GainsOf"/>, exact for this layout, the response at each part's frequency
/// for any other). The friendly presets are the shared <see cref="Eq.PresetShapes"/> sampled at the five frequencies. No UI.
/// </summary>
public static class EasyEq
{
    /// <summary>The reach of a slider, both ways, in dB; one slider step is one dB.</summary>
    public const int MaxGainDb = 12;

    /// <summary>One slider: its name, and the band it drives.</summary>
    public sealed record Part(string Name, EqType Type, double Freq, double Q);

    /// <summary>The five parts, low to high.</summary>
    public static readonly IReadOnlyList<Part> Parts = new Part[]
    {
        new("Bass", EqType.LowShelf, 100, Eq.ShelfQ),
        new("Low mid", EqType.Peak, 300, Eq.DefaultQ),
        new("Mid", EqType.Peak, 1000, Eq.DefaultQ),
        new("High mid", EqType.Peak, 3000, Eq.DefaultQ),
        new("Treble", EqType.HighShelf, 8000, Eq.ShelfQ),
    };

    public static int[] Flat() => new int[Parts.Count];

    public static bool IsFlat(IReadOnlyList<int> gains) => gains.All(g => g == 0);

    /// <summary>The bands of the layout at these gains (held to ±<see cref="MaxGainDb"/>); ids are the place from 1.</summary>
    public static List<EqBand> Bands(IReadOnlyList<int> gains) => Parts.Select((p, i) => new EqBand
    {
        Id = i + 1, Type = p.Type, Freq = p.Freq, Q = p.Q, Gain = Math.Clamp(gains[i], -MaxGainDb, MaxGainDb),
    }).ToList();

    /// <summary>Whether these bands are exactly this layout (any gains), so the five sliders show them as they are.</summary>
    public static bool IsLayout(IReadOnlyList<EqBand>? bands) =>
        bands is not null && bands.Count == Parts.Count
        && bands.Zip(Parts).All(x => x.First.Type == x.Second.Type && x.First.Freq == x.Second.Freq && x.First.Q == x.Second.Q);

    /// <summary>
    /// The five gains of these bands: the bands' own gains (rounded to a dB) when they are this layout; otherwise what the bands do at each
    /// part's frequency, so the sliders show roughly the sound an Advanced equaliser makes. Always whole dB inside the slider's reach.
    /// </summary>
    public static int[] GainsOf(IReadOnlyList<EqBand>? bands)
    {
        if (bands is null || bands.Count == 0) return Flat();
        var db = IsLayout(bands)
            ? bands.Select(b => b.Enabled ? b.Gain : 0)
            : EqMath.CurveDb(bands, 0, Parts.Select(p => p.Freq).ToList());
        return db.Select(g => (int)Math.Clamp(Math.Round(g), -MaxGainDb, MaxGainDb)).ToArray();
    }

    /// <summary>The words of a chip that means "no change", and each chip with the shared shape it is made from (null = flat), in the order of the chips.</summary>
    private static readonly (string Label, string? Shape)[] Chips =
    {
        ("Normal", null), ("Bass boost", "Bass boost"), ("Vocal", "Vocal"), ("Night", "Night"), ("Loud", "Loudness"),
    };

    /// <summary>The preset chips: Normal (flat), then <see cref="Eq.PresetShapes"/> sampled at the five frequencies, in whole dB.</summary>
    public static readonly IReadOnlyList<EasyPreset> Presets = Chips
        .Select(c => new EasyPreset(c.Label, c.Shape is null ? Flat() : Sample(Eq.PresetShapes.First(s => s.Name == c.Shape).Gains)))
        .ToList();

    private static int[] Sample(IReadOnlyList<double> shape) =>
        Parts.Select(p => (int)Math.Clamp(Math.Round(Eq.ShapeAt(shape, p.Freq)), -MaxGainDb, MaxGainDb)).ToArray();

    /// <summary>
    /// The looks for a voice (Easy's "Improve my microphone"), as gains of the same five parts: an equaliser cleanup, no noise removal.
    /// "Normal" leaves the microphone as it is.
    /// </summary>
    public static readonly IReadOnlyList<EasyPreset> MicPresets = new EasyPreset[]
    {
        new("Normal", new[] { 0, 0, 0, 0, 0 }),
        new("Clear voice", new[] { -3, -2, 0, 3, 2 }),
        new("Warm", new[] { 2, 3, 0, -2, -3 }),
        new("Reduce rumble", new[] { -9, -3, 0, 0, 0 }),
    };

    /// <summary>The preset these gains are, or null (the sound is the user's own).</summary>
    public static EasyPreset? PresetOf(IReadOnlyList<int> gains) => Presets.FirstOrDefault(p => p.Gains.SequenceEqual(gains));

    // ---- the effect in a chain ----

    /// <summary>The equaliser the Easy sliders edit: the chain's first Parametric equaliser; null when it has none.</summary>
    public static FxSlot? Find(IReadOnlyList<FxSlot> chain) => chain.FirstOrDefault(s => s.Kind == FxCatalog.EqKind);

    /// <summary>The five gains the chain makes now: flat when it has no equaliser or its equaliser is switched off.</summary>
    public static int[] Read(IReadOnlyList<FxSlot> chain) =>
        Find(chain) is { Enabled: true } slot ? GainsOf(slot.Bands) : Flat();

    /// <summary>True when the chain has an equaliser the five sliders cannot show exactly (bands made in the Advanced editor): moving a slider replaces them.</summary>
    public static bool HasOtherBands(IReadOnlyList<FxSlot> chain) =>
        Find(chain) is { Enabled: true } slot && !IsLayout(slot.Bands) && !(slot.Bands ?? new List<EqBand>()).All(EqMath.IsFlat);

    /// <summary>Puts these gains in the chain's equaliser (made first in the chain when there is none, and switched on) and returns it.</summary>
    public static FxSlot Write(List<FxSlot> chain, IReadOnlyList<int> gains)
    {
        var slot = Find(chain);
        if (slot is null)
        {
            slot = FxCatalog.Create(FxCatalog.EqKind);
            chain.Insert(0, slot);
        }
        slot.Bands = Bands(gains);
        slot.Enabled = true;
        return slot;
    }

    /// <summary>The five gains stored in the chain's equaliser even while it is switched off (what the user chose); flat when it has none.</summary>
    public static int[] StoredGains(IReadOnlyList<FxSlot> chain) => GainsOf(Find(chain)?.Bands);

    /// <summary>The chain has an equaliser that is switched on and its effects are on: what "its own sound is on" means.</summary>
    public static bool OwnEqOn(IReadOnlyList<FxSlot> chain, bool effectsOn) => effectsOn && Find(chain) is { Enabled: true };

    /// <summary>Whether a row's effects should be on after these gains were written: a flat sound with nothing else in the chain switches them off, any other sound on.</summary>
    public static bool EffectsOnFor(IReadOnlyList<FxSlot> chain, IReadOnlyList<int> gains, bool effectsOn) =>
        !IsFlat(gains) || (!OnlyEasyRuns(chain) && effectsOn);

    /// <summary>True when nothing in the chain but the Easy equaliser can change the sound (so a flat sound needs no processing at all).</summary>
    public static bool OnlyEasyRuns(IReadOnlyList<FxSlot> chain) =>
        Find(chain) is { } own && chain.All(s => ReferenceEquals(s, own) || !s.Enabled);
}
