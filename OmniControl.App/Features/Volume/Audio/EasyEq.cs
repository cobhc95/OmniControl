namespace OmniControl.App.Features.Volume.Audio;

/// One of the Easy page's friendly presets: the words on its chip and its five gains in dB (<see cref="EasyEq.Parts"/> order).
public sealed record EasyPreset(string Name, int[] Gains);

/// The Easy mode's five-slider equaliser (Bass, Low mid, Mid, High mid, Treble) as a fixed layout of a Parametric equaliser's bands:
/// one definition, so the Easy sliders and the Advanced equaliser edit the same effect. The mapping goes both ways: gains to bands
/// (<see cref="Bands"/>) and bands back to gains (<see cref="GainsOf(FxSlot)"/>, exact for this layout, the response at each part's frequency
/// for any other). The friendly presets are the shared <see cref="Eq.PresetShapes"/> sampled at the five frequencies. No UI.
public static class EasyEq
{
    /// The reach of a slider, both ways, in dB; one slider step is one dB.
    public const int MaxGainDb = 12;

    /// One slider: its name, and the band it drives.
    public sealed record Part(string Name, EqType Type, double Freq, double Q);

    /// The five parts, low to high.
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

    /// The bands of the layout at these gains (held to ±<see cref="MaxGainDb"/>); ids are the place from 1.
    public static List<EqBand> Bands(IReadOnlyList<int> gains) => Parts.Select((p, i) => new EqBand
    {
        Id = i + 1, Type = p.Type, Freq = p.Freq, Q = p.Q, Gain = Math.Clamp(gains[i], -MaxGainDb, MaxGainDb),
    }).ToList();

    /// How far a band's frequency may be from its part's (a fraction) and its Q (absolute) and still be this layout: fine moves in the FX window are not a new shape.
    private const double FreqTolerance = 0.01, QTolerance = 0.01;

    /// Whether these bands still have the Easy layout, so the five sliders can show and edit them exactly: the same band count, types,
    /// frequencies and Qs (within a small tolerance), every band switched on and every gain inside the slider's reach.
    public static bool Matches(IReadOnlyList<EqBand>? bands) =>
        bands is not null && bands.Count == Parts.Count
        && bands.Zip(Parts).All(x => x.First.Enabled && x.First.Type == x.Second.Type
            && Math.Abs(x.First.Freq / x.Second.Freq - 1) <= FreqTolerance && Math.Abs(x.First.Q - x.Second.Q) <= QTolerance
            && Math.Abs(x.First.Gain) <= MaxGainDb);

    /// Whether the effect is exactly what the five sliders can describe: the Easy layout, no overall gain, fully wet.
    public static bool Describes(FxSlot slot) => Matches(slot.Bands) && FxCatalog.EqGainOf(slot) == 0 && slot.Wet == FxSlot.DefaultWet;

    /// The effect has a shape (a band that does something, or an overall gain) the five sliders cannot describe: it was made in the full effects window.
    public static bool IsCustom(FxSlot slot) =>
        !Describes(slot) && (FxCatalog.EqGainOf(slot) != 0 || (slot.Bands ?? new List<EqBand>()).Any(b => !EqMath.IsFlat(b)));

    /// The five gains of these bands: the bands' own gains (rounded to a dB) when they are this layout; otherwise what the bands do at each
    /// part's frequency, so the sliders show roughly the sound an Advanced equaliser makes. Always whole dB inside the slider's reach.
    public static int[] GainsOf(IReadOnlyList<EqBand>? bands)
    {
        if (bands is null || bands.Count == 0) return Flat();
        return Matches(bands) ? bands.Select(b => (int)Math.Round(b.Gain)).ToArray() : Sample(bands, 0, 1);
    }

    /// The five gains of this equaliser effect: exact when it is the Easy layout, otherwise its whole response (bands, overall gain and wet mix) sampled at the five frequencies.
    public static int[] GainsOf(FxSlot? slot) => slot is null ? Flat() : Describes(slot) ? GainsOf(slot.Bands) : BestFit(slot);

    /// The five-slider sound closest to this equaliser effect: its response (bands, overall gain and wet mix, the same maths as the FX
    /// window's graph) read at the five frequencies, in whole dB inside the slider's reach. What "Back to simple sliders" puts in.
    public static int[] BestFit(FxSlot slot) => Sample(slot.Bands ?? new List<EqBand>(), FxCatalog.EqGainOf(slot), Core.Models.Percent.ToScalar(slot.Wet));

    private static int[] Sample(IReadOnlyList<EqBand> bands, double gainDb, double wet) =>
        EqMath.CurveDb(bands, gainDb, Parts.Select(p => p.Freq).ToList(), wet).Select(g => (int)Math.Clamp(Math.Round(g), -MaxGainDb, MaxGainDb)).ToArray();

    /// The words of a chip that means "no change", and each chip with the shared shape it is made from (null = flat), in the order of the chips.
    private static readonly (string Label, string? Shape)[] Chips =
    {
        ("Normal", null), ("Bass boost", "Bass boost"), ("Vocal", "Vocal"), ("Night", "Night"), ("Loud", "Loudness"),
    };

    /// The preset chips: Normal (flat), then <see cref="Eq.PresetShapes"/> sampled at the five frequencies, in whole dB.
    public static readonly IReadOnlyList<EasyPreset> Presets = Chips
        .Select(c => new EasyPreset(c.Label, c.Shape is null ? Flat() : Sample(Eq.PresetShapes.First(s => s.Name == c.Shape).Gains)))
        .ToList();

    private static int[] Sample(IReadOnlyList<double> shape) =>
        Parts.Select(p => (int)Math.Clamp(Math.Round(Eq.ShapeAt(shape, p.Freq)), -MaxGainDb, MaxGainDb)).ToArray();

    /// The looks for a voice (Easy's "Improve my microphone"), as gains of the same five parts: an equaliser cleanup, no noise removal.
    /// "Normal" leaves the microphone as it is.
    public static readonly IReadOnlyList<EasyPreset> MicPresets = new EasyPreset[]
    {
        new("Normal", new[] { 0, 0, 0, 0, 0 }),
        new("Clear voice", new[] { -3, -2, 0, 3, 2 }),
        new("Warm", new[] { 2, 3, 0, -2, -3 }),
        new("Reduce rumble", new[] { -9, -3, 0, 0, 0 }),
    };

    /// The preset these gains are, or null (the sound is the user's own).
    public static EasyPreset? PresetOf(IReadOnlyList<int> gains) => Presets.FirstOrDefault(p => p.Gains.SequenceEqual(gains));

    // ---- the effect in a chain ----

    /// The equaliser the Easy sliders edit: the chain's first Parametric equaliser; null when it has none.
    public static FxSlot? Find(IReadOnlyList<FxSlot> chain) => chain.FirstOrDefault(s => s.Kind == FxCatalog.EqKind);

    /// The five gains the chain makes now: flat when it has no equaliser or its equaliser is switched off.
    public static int[] Read(IReadOnlyList<FxSlot> chain) =>
        Find(chain) is { Enabled: true } slot ? GainsOf(slot) : Flat();

    /// True when the chain has an equaliser with a shape the five sliders cannot describe (<see cref="IsCustom"/>): writing the five replaces it, so ask first.
    public static bool HasCustom(IReadOnlyList<FxSlot> chain) => Find(chain) is { } slot && IsCustom(slot);

    /// Puts these gains in the chain's equaliser as the Easy layout (made first in the chain when there is none, switched on, no overall gain, fully wet) and returns it. It replaces any shape made elsewhere: the caller asks first (<see cref="HasCustom"/>).
    public static FxSlot Write(List<FxSlot> chain, IReadOnlyList<int> gains)
    {
        var slot = Find(chain);
        if (slot is null)
        {
            slot = FxCatalog.Create(FxCatalog.EqKind);
            chain.Insert(0, slot);
        }
        slot.Bands = Bands(gains);
        if (slot.Values.Count > 0) slot.Values[0] = 0; // the overall gain is part of the shape the five sliders replace
        slot.Wet = FxSlot.DefaultWet;
        slot.Enabled = true;
        return slot;
    }

    /// The five gains stored in the chain's equaliser even while it is switched off (what the user chose); flat when it has none.
    public static int[] StoredGains(IReadOnlyList<FxSlot> chain) => GainsOf(Find(chain));

    /// The chain has an equaliser that is switched on and its effects are on: what "its own sound is on" means.
    public static bool OwnEqOn(IReadOnlyList<FxSlot> chain, bool effectsOn) => effectsOn && Find(chain) is { Enabled: true };

    /// Whether a row's effects should be on after these gains were written: a flat sound with nothing else in the chain switches them off, any other sound on.
    public static bool EffectsOnFor(IReadOnlyList<FxSlot> chain, IReadOnlyList<int> gains, bool effectsOn) =>
        !IsFlat(gains) || (!OnlyEasyRuns(chain) && effectsOn);

    /// True when nothing in the chain but the Easy equaliser can change the sound (so a flat sound needs no processing at all).
    public static bool OnlyEasyRuns(IReadOnlyList<FxSlot> chain) =>
        Find(chain) is { } own && chain.All(s => ReferenceEquals(s, own) || !s.Enabled);
}
