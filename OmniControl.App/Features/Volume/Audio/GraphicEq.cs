namespace OmniControl.App.Features.Volume.Audio;

/// A graphic equaliser's fixed bands: one peaking band per centre, as wide as the spacing between centres, only the gain free. Its
/// bands are ordinary <see cref="EqBand"/>s (id = place from 1), so the engine, the APO writer and the response maths treat it like
/// any equaliser. No UI.
public sealed class GraphicBands
{
    /// Preset gains are rounded to this (dB), so a sampled shape reads cleanly on the sliders.
    private const double PresetStepDb = 0.5;

    public GraphicBands(IReadOnlyList<double> centres, double octavesApart)
    {
        Centres = centres;
        OctavesApart = octavesApart;
    }

    /// The band frequencies (Hz), low to high.
    public IReadOnlyList<double> Centres { get; }

    /// The spacing between neighbouring centres, in octaves.
    public double OctavesApart { get; }

    /// Every band's Q: a bandwidth of exactly the spacing (<see cref="EqMath.QOf"/>), so neighbouring bands meet at their half-gain
    /// points, as on a constant-Q graphic equaliser. The one formula for every graphic equaliser.
    public double Q => EqMath.QOf(OctavesApart);

    /// The bands at these gains (one per centre, in order).
    public List<EqBand> Bands(IReadOnlyList<double> gains) => Centres.Select((freq, i) => new EqBand
    {
        Id = i + 1,
        Type = EqType.Peak,
        Freq = freq,
        Q = Q,
        Gain = Math.Clamp(gains[i], Eq.MinGain, Eq.MaxGain),
    }).ToList();

    /// Every band at 0 dB (a new equaliser and the Flat preset).
    public List<EqBand> Flat() => Bands(new double[Centres.Count]);

    /// One of <see cref="Eq.PresetShapes"/> at these centres.
    public List<EqBand> Shaped(IReadOnlyList<double> shape) =>
        Bands(Centres.Select(f => Math.Round(Eq.ShapeAt(shape, f) / PresetStepDb) * PresetStepDb).ToList());

    /// Saved bands made fit for today: rebuilt from this definition (so frequency, type and Q always follow it), each keeping the gain
    /// of the saved band with its id. Never returns null.
    public List<EqBand> Upgrade(List<EqBand>? saved)
    {
        var gains = new double[Centres.Count];
        foreach (var band in saved ?? new List<EqBand>())
            if (band.Id >= 1 && band.Id <= gains.Length && double.IsFinite(band.Gain)) gains[band.Id - 1] = band.Gain;
        return Bands(gains);
    }
}

/// The graphic equalisers' band sets and their layouts (the catalog entries' <see cref="FxKind.Eq"/>), each defined once.
public static class GraphicEq
{
    /// The ISO 266 preferred third-octave centres from 16 Hz to 20 kHz (32). 20 Hz to 20 kHz alone is exactly 31 centres; 16 Hz is the
    /// standard centre below it, so the spacing stays even (25 kHz, the one above, is more than 48 kHz sound can carry).
    private static readonly double[] ThirdOctaveCentres =
    {
        16, 20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800,
        1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000, 20000,
    };

    private const double ThirdOctave = 1.0 / 3;

    /// 32 bands a third of an octave apart.
    private static readonly GraphicBands ThirtyTwoBands = new(ThirdOctaveCentres, ThirdOctave);

    /// 16 bands two thirds of an octave apart: every second centre of the same list from 20 Hz (20, 31.5, 50 ... 12.5k, 20k).
    private static readonly GraphicBands SixteenBands = new(ThirdOctaveCentres.Where((_, i) => i % 2 == 1).ToList(), 2 * ThirdOctave);

    public static readonly EqLayout ThirtyTwo = LayoutOf(ThirtyTwoBands);
    public static readonly EqLayout Sixteen = LayoutOf(SixteenBands);

    /// Flat, then every shared preset shape sampled at the set's centres.
    private static EqLayout LayoutOf(GraphicBands set) => new(
        set.Flat,
        Eq.PresetShapes.Select(s => (s.Name, (Func<List<EqBand>>)(() => set.Shaped(s.Gains)))).Prepend((Eq.FlatName, set.Flat)).ToList(),
        set.Upgrade,
        set);
}
