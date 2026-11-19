namespace OmniControl.App.Features.Volume.Audio;

/// A band's filter type. The numbers are stored in mixer.json: never renumber them (0-2 are from the first equaliser).
public enum EqType
{
    LowShelf = 0,
    /// The bell ("Band" in the UI).
    Peak = 1,
    HighShelf = 2,
    LowPass = 3,
    HighPass = 4,
    AllPass = 5,
    Notch = 6,
    BandPass = 7,
}

/// One parametric band. Plain data, saved in mixer.json.
public sealed class EqBand
{
    /// Unique within its equaliser, so the engine keeps each band's filter state with that band when others are added or removed.
    public int Id { get; set; }
    public bool Enabled { get; set; } = true;
    public double Freq { get; set; }
    public double Gain { get; set; }
    public double Q { get; set; } = Eq.DefaultQ;
    public EqType Type { get; set; } = EqType.Peak;

    public EqBand Copy() => (EqBand)MemberwiseClone();
}

/// Band limits, the default layout, presets and the loading of older settings. No UI and no Windows calls.
public static class Eq
{
    public const double MinFreq = 20, MaxFreq = 20000, MinGain = -24, MaxGain = 24;

    /// The Q range; the bandwidth range in octaves follows from it (<see cref="EqMath.BandwidthOf"/>), so the two always agree.
    public const double MinQ = 0.1, MaxQ = 150;
    public static readonly double MinBandwidth = EqMath.BandwidthOf(MaxQ), MaxBandwidth = EqMath.BandwidthOf(MinQ);

    /// Q of a new bell band (about 1.4 octaves) and of a shelf or pass band (Butterworth).
    public const double DefaultQ = 1, ShelfQ = 0.707;

    /// The most bands one equaliser holds. Only a guard: each band costs one biquad per channel, so even this many is light work;
    /// it keeps the band tabs usable and the engine's fixed-size arrays bounded.
    public const int MaxBands = 64;

    /// Where a new band goes when the user adds one and the bands leave no gap to fill.
    public const double NewBandFreq = 1000;

    /// A new equaliser: low shelf, two bells, high shelf, all flat.
    private static readonly IReadOnlyList<(EqType Type, double Freq, double Q)> DefaultLayout = new[]
    {
        (EqType.LowShelf, 100.0, ShelfQ), (EqType.Peak, 400.0, DefaultQ), (EqType.Peak, 2500.0, DefaultQ), (EqType.HighShelf, 8000.0, ShelfQ),
    };

    /// The 10-band layout of the first equaliser; its presets and settings saved with it keep this layout.
    private static readonly IReadOnlyList<double> ClassicFreqs = new double[] { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    /// The band a slider is reset to: the default layout's band at this place, or a new flat bell for bands added later.
    public static EqBand DefaultFor(int index)
    {
        if (index >= DefaultLayout.Count) return new EqBand { Id = index + 1, Freq = NewBandFreq, Q = DefaultQ, Type = EqType.Peak };
        var (type, freq, q) = DefaultLayout[index];
        return new EqBand { Id = index + 1, Freq = freq, Q = q, Type = type };
    }

    /// A new, flat equaliser.
    public static List<EqBand> Default() => Enumerable.Range(0, DefaultLayout.Count).Select(DefaultFor).ToList();

    private static EqBand ClassicBand(int i, double gain) => new()
    {
        Id = i + 1,
        Freq = ClassicFreqs[i],
        Gain = gain,
        Q = i is 0 || i == ClassicFreqs.Count - 1 ? 0.7 : 1.0,
        Type = i == 0 ? EqType.LowShelf : i == ClassicFreqs.Count - 1 ? EqType.HighShelf : EqType.Peak,
    };

    private static List<EqBand> Classic(params double[] gains) => gains.Select((gain, i) => ClassicBand(i, gain)).ToList();

    /// The name of the preset with every band at 0 dB, the first of every equaliser's presets.
    public const string FlatName = "Flat";

    /// The named sound shapes of the presets, as gains at the classic 10-band points (31 Hz to 16 kHz). Defined once for every
    /// equaliser: the Parametric's presets are these 10 bands (the sound they always had); a graphic equaliser samples them at its own
    /// centres (<see cref="ShapeAt"/>).
    public static readonly IReadOnlyList<(string Name, double[] Gains)> PresetShapes = new (string, double[])[]
    {
        ("Bass boost", new double[] { 6, 5, 3, 1, 0, 0, 0, 0, 0, 0 }),
        ("Treble boost", new double[] { 0, 0, 0, 0, 0, 0, 1, 3, 5, 6 }),
        ("Vocal", new double[] { -3, -2, -1, 0, 2, 3, 3, 1, -1, -2 }),
        ("V shape", new double[] { 5, 4, 2, 0, -2, -2, 0, 2, 4, 5 }),
        ("Loudness", new double[] { 5, 4, 1, 0, 0, 0, 0, 1, 4, 5 }),
        ("Night", new double[] { -4, -3, -1, 0, 1, 2, 2, 0, -2, -4 }),
    };

    /// A shape's gain at <paramref name="freq"/>: straight between its points on a log scale, held beyond the first and last.
    public static double ShapeAt(IReadOnlyList<double> gains, double freq)
    {
        if (freq <= ClassicFreqs[0]) return gains[0];
        for (var i = 1; i < ClassicFreqs.Count; i++)
        {
            if (freq > ClassicFreqs[i]) continue;
            var share = Math.Log(freq / ClassicFreqs[i - 1]) / Math.Log(ClassicFreqs[i] / ClassicFreqs[i - 1]);
            return gains[i - 1] + share * (gains[i] - gains[i - 1]);
        }
        return gains[^1];
    }

    /// The Parametric's named equalisers. Flat is the default layout; the others keep the 10-band shapes they were made on, so they sound as before.
    private static readonly IReadOnlyList<(string Name, Func<List<EqBand>> Bands)> Presets = PresetShapes
        .Select(s => (s.Name, (Func<List<EqBand>>)(() => Classic(s.Gains))))
        .Prepend((FlatName, Default))
        .ToList();

    /// The Parametric equaliser's layout (its catalog entry's <see cref="FxKind.Eq"/>): free bands, the presets above, <see cref="Upgrade"/>.
    public static readonly EqLayout Layout = new(Default, Presets, Upgrade);

    /// A flat bell for "Add band": in the middle of the widest gap between the bands (on a log scale), with the next free id.
    public static EqBand NewBand(IReadOnlyList<EqBand> bands)
    {
        var edges = bands.Select(b => b.Freq).Append(MinFreq).Append(MaxFreq).OrderBy(f => f).ToList();
        var (low, high) = edges.Zip(edges.Skip(1)).MaxBy(g => Math.Log(g.Second / g.First));
        var freq = high / low > 1.5 ? Math.Round(Math.Sqrt(low * high)) : NewBandFreq;
        return new EqBand { Id = NextId(bands), Freq = freq, Q = DefaultQ, Type = EqType.Peak };
    }

    public static int NextId(IReadOnlyList<EqBand> bands) => bands.Count == 0 ? 1 : bands.Max(b => b.Id) + 1;

    /// Settings saved by older versions load unchanged (same types, frequencies, gains and Q); they only gain unique ids, and gains or
    /// Q outside today's ranges are held inside them. Never returns null.
    public static List<EqBand> Upgrade(List<EqBand>? bands)
    {
        if (bands is null) return Default();
        if (bands.Count > MaxBands) bands = bands.Take(MaxBands).ToList();
        if (bands.Select(b => b.Id).Distinct().Count() != bands.Count || bands.Any(b => b.Id <= 0))
            for (var i = 0; i < bands.Count; i++) bands[i].Id = i + 1;
        foreach (var b in bands)
        {
            b.Freq = Math.Clamp(b.Freq, MinFreq, MaxFreq);
            b.Gain = Math.Clamp(b.Gain, MinGain, MaxGain);
            b.Q = Math.Clamp(b.Q, MinQ, MaxQ);
            if (!Enum.IsDefined(b.Type)) b.Type = EqType.Peak;
        }
        return bands;
    }
}
