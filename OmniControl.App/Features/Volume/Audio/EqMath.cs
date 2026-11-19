using System.Numerics;

namespace OmniControl.App.Features.Volume.Audio;

/// The normalised coefficients of one biquad (a0 = 1): y = b0 x + b1 x1 + b2 x2 - a1 y1 - a2 y2.
public readonly record struct BiquadCoefficients(double B0, double B1, double B2, double A1, double A2)
{
    /// Passes sound through unchanged; a filter running these keeps no state.
    public static BiquadCoefficients Identity { get; } = new(1, 0, 0, 0, 0);
}

/// Filter maths (the RBJ Audio EQ Cookbook biquads): the one formula, used by the effects engine, for the master APO's headroom and
/// to draw every equaliser's curve (<see cref="CurveDb"/> over a <see cref="LogGrid"/>). Q is the cookbook's Q for every type
/// (Equalizer APO reads it the same way).
public static class EqMath
{
    /// The rate the graph is drawn at.
    public const double GraphSampleRate = 48000;

    /// How many frequencies <see cref="PeakDb"/> looks at, spread evenly on a log scale from MinFreq to MaxFreq.
    private const int PeakScanPoints = 256;

    /// A notch's exact zero would be minus infinity dB; responses are held above this.
    private const double FloorDb = -240;

    /// Highest filter frequency as a share of the sample rate (just under Nyquist, where the maths breaks down); also the highest a curve is drawn to.
    public const double MaxFreqShare = 0.49;

    /// Types whose Gain changes the sound; the others (pass, notch, band-pass, all-pass) ignore it, as Equalizer APO does.
    public static bool UsesGain(EqType type) => type is EqType.Peak or EqType.LowShelf or EqType.HighShelf;

    /// A band that passes sound unchanged: switched off, or a gain type at 0 dB.
    /// Types whose width is a bandwidth around the frequency (a bell, notch, band-pass or all-pass); shelves and the pass filters use the setting as a slope or resonance.
    public static bool UsesBandwidth(EqType type) => type is EqType.Peak or EqType.Notch or EqType.BandPass or EqType.AllPass;

    public static bool IsFlat(EqBand band) => !band.Enabled || (UsesGain(band.Type) && band.Gain == 0);

    /// Bandwidth in octaves for a Q (the cookbook's relation: 1/Q = 2 sinh(ln 2 / 2 * BW)).
    public static double BandwidthOf(double q) => 2 / Math.Log(2) * Math.Asinh(1 / (2 * q));

    /// The Q for a bandwidth in octaves (inverse of <see cref="BandwidthOf"/>).
    public static double QOf(double octaves) => 1 / (2 * Math.Sinh(Math.Log(2) / 2 * octaves));

    /// The coefficients of one band at <paramref name="sampleRate"/>; a flat band (<see cref="IsFlat"/>) is exactly <see cref="BiquadCoefficients.Identity"/>.
    public static BiquadCoefficients Coefficients(EqBand band, double sampleRate)
    {
        if (IsFlat(band)) return BiquadCoefficients.Identity;
        var w0 = 2 * Math.PI * Math.Min(band.Freq, sampleRate * MaxFreqShare) / sampleRate;
        var cos = Math.Cos(w0);
        var a = Math.Pow(10, band.Gain / 40);
        var alpha = Math.Sin(w0) / (2 * band.Q);
        var sq = 2 * Math.Sqrt(a) * alpha;
        double b0, b1, b2, a0, a1, a2;
        switch (band.Type)
        {
            case EqType.Peak:
                b0 = 1 + alpha * a; b1 = -2 * cos; b2 = 1 - alpha * a;
                a0 = 1 + alpha / a; a1 = -2 * cos; a2 = 1 - alpha / a;
                break;
            case EqType.LowShelf:
                b0 = a * ((a + 1) - (a - 1) * cos + sq); b1 = 2 * a * ((a - 1) - (a + 1) * cos); b2 = a * ((a + 1) - (a - 1) * cos - sq);
                a0 = (a + 1) + (a - 1) * cos + sq; a1 = -2 * ((a - 1) + (a + 1) * cos); a2 = (a + 1) + (a - 1) * cos - sq;
                break;
            case EqType.HighShelf:
                b0 = a * ((a + 1) + (a - 1) * cos + sq); b1 = -2 * a * ((a - 1) + (a + 1) * cos); b2 = a * ((a + 1) + (a - 1) * cos - sq);
                a0 = (a + 1) - (a - 1) * cos + sq; a1 = 2 * ((a - 1) - (a + 1) * cos); a2 = (a + 1) - (a - 1) * cos - sq;
                break;
            case EqType.LowPass:
                b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = (1 - cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case EqType.HighPass:
                b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = (1 + cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case EqType.BandPass: // constant 0 dB peak
                b0 = alpha; b1 = 0; b2 = -alpha;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case EqType.Notch:
                b0 = 1; b1 = -2 * cos; b2 = 1;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case EqType.AllPass:
                b0 = 1 - alpha; b1 = -2 * cos; b2 = 1 + alpha;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            default:
                return BiquadCoefficients.Identity;
        }
        return new BiquadCoefficients(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// The complex response of all bands together at <paramref name="freq"/> (graph rate).
    public static Complex Response(IEnumerable<EqBand> bands, double freq) => Response(SoundingCoefficients(bands), freq);

    /// The graph-rate coefficients of the bands that change the sound (flat ones pass unchanged).
    private static List<BiquadCoefficients> SoundingCoefficients(IEnumerable<EqBand> bands) =>
        bands.Where(b => !IsFlat(b)).Select(b => Coefficients(b, GraphSampleRate)).ToList();

    private static Complex Response(IReadOnlyList<BiquadCoefficients> filters, double freq)
    {
        var w = 2 * Math.PI * freq / GraphSampleRate;
        var z1 = Complex.FromPolarCoordinates(1, -w);
        var z2 = z1 * z1;
        var total = Complex.One;
        foreach (var c in filters) total *= (c.B0 + c.B1 * z1 + c.B2 * z2) / (1 + c.A1 * z1 + c.A2 * z2);
        return total;
    }

    /// <paramref name="points"/> frequencies (at least 2) spread evenly on a log scale from <paramref name="min"/> to <paramref name="max"/>.
    public static double[] LogGrid(int points, double min, double max) =>
        Enumerable.Range(0, points).Select(i => min * Math.Pow(max / min, i / (double)(points - 1))).ToArray();

    /// The total response in dB of <paramref name="bands"/> plus <paramref name="gainDb"/> at each of <paramref name="freqs"/>: the curve an
    /// equaliser draws (each band's coefficients are worked out once). The response of a chain is the sum of its effects' curves
    /// (<see cref="ChainResponse"/>). Below a <paramref name="wet"/> of 1 the dry sound is mixed back as the engine's wet mix does it,
    /// (1 - wet) + wet x the effect, which is not a plain shift of the curve.
    public static double[] CurveDb(IEnumerable<EqBand> bands, double gainDb, IReadOnlyList<double> freqs, double wet = 1)
    {
        var filters = SoundingCoefficients(bands);
        var curve = new double[freqs.Count];
        if (wet >= 1)
        {
            for (var i = 0; i < curve.Length; i++) curve[i] = Db(Response(filters, freqs[i])) + gainDb;
            return curve;
        }
        var gain = Math.Pow(10, gainDb / 20);
        for (var i = 0; i < curve.Length; i++) curve[i] = Db((1 - wet) + wet * gain * Response(filters, freqs[i]));
        return curve;
    }

    /// Gain in dB of a response (held above <see cref="FloorDb"/>).
    public static double Db(Complex response) => Math.Max(FloorDb, 20 * Math.Log10(response.Magnitude));

    /// Phase in degrees of a response, -180 to 180.
    public static double PhaseDeg(Complex response) => response.Phase * 180 / Math.PI;

    /// The highest gain of all bands together (dB), looked for on a log grid and at each band's own frequency.
    public static double PeakDb(IReadOnlyList<EqBand> bands)
    {
        var freqs = LogGrid(PeakScanPoints, Eq.MinFreq, Eq.MaxFreq).Concat(bands.Select(b => b.Freq)).ToList();
        return CurveDb(bands, 0, freqs).Max();
    }
}
