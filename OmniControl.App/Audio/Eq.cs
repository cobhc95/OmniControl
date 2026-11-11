namespace OmniControl.App.Audio;

public enum EqType { LowShelf, Peak, HighShelf }

/// <summary>One parametric band. Plain data, saved in mixer.json.</summary>
public sealed class EqBand
{
    public double Freq { get; set; }
    public double Gain { get; set; }
    public double Q { get; set; } = 1;
    public EqType Type { get; set; } = EqType.Peak;

    public EqBand Clone() => (EqBand)MemberwiseClone();
}

/// <summary>The 10-band layout, limits, and presets. No UI and no Windows calls.</summary>
public static class Eq
{
    public const int Bands = 10;
    public const double MinFreq = 20, MaxFreq = 20000, MinGain = -12, MaxGain = 12, MinQ = 0.3, MaxQ = 10;

    public static readonly IReadOnlyList<double> DefaultFreqs = new double[] { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    public static EqBand DefaultBand(int i) => new()
    {
        Freq = DefaultFreqs[i],
        Gain = 0,
        Q = i is 0 or Bands - 1 ? 0.7 : 1.0,
        Type = i == 0 ? EqType.LowShelf : i == Bands - 1 ? EqType.HighShelf : EqType.Peak,
    };

    public static List<EqBand> Flat() => Enumerable.Range(0, Bands).Select(DefaultBand).ToList();

    /// <summary>Named gain shapes (dB per band) on the default layout.</summary>
    public static readonly IReadOnlyList<(string Name, double[] Gains)> Presets = new (string, double[])[]
    {
        ("Flat", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
        ("Bass boost", new double[] { 6, 5, 3, 1, 0, 0, 0, 0, 0, 0 }),
        ("Treble boost", new double[] { 0, 0, 0, 0, 0, 0, 1, 3, 5, 6 }),
        ("Vocal", new double[] { -3, -2, -1, 0, 2, 3, 3, 1, -1, -2 }),
        ("V shape", new double[] { 5, 4, 2, 0, -2, -2, 0, 2, 4, 5 }),
        ("Loudness", new double[] { 5, 4, 1, 0, 0, 0, 0, 1, 4, 5 }),
        ("Night", new double[] { -4, -3, -1, 0, 1, 2, 2, 0, -2, -4 }),
    };

    public static List<EqBand> FromPreset(string name) =>
        Presets.First(p => p.Name == name).Gains.Select((g, i) => { var b = DefaultBand(i); b.Gain = g; return b; }).ToList();

    /// <summary>The preset these bands match exactly, or "Custom".</summary>
    public static string PresetNameFor(IReadOnlyList<EqBand> bands) =>
        Presets.FirstOrDefault(p => bands.Count == Bands && bands.Select((b, i) => b.Type == DefaultBand(i).Type
            && b.Freq == DefaultBand(i).Freq && b.Q == DefaultBand(i).Q && b.Gain == p.Gains[i]).All(x => x)).Name ?? "Custom";

    /// <summary>Converts the old 6-band gains (60, 230, 910, 3600, 7000, 14000 Hz) onto the nearest bands of the 10-band layout.</summary>
    public static List<EqBand> FromLegacy(int[] gains)
    {
        var bands = Flat();
        var map = new[] { 1, 3, 5, 7, 8, 9 };
        for (var i = 0; i < Math.Min(gains.Length, map.Length); i++) bands[map[i]].Gain = gains[i];
        return bands;
    }
}

/// <summary>Filter response maths (RBJ biquads at 48 kHz), used to draw the graph and later by a sound engine.</summary>
public static class EqMath
{
    private const double SampleRate = 48000;

    /// <summary>Gain in dB of one band at <paramref name="freq"/>.</summary>
    public static double ResponseDb(EqBand band, double freq)
    {
        var w0 = 2 * Math.PI * band.Freq / SampleRate;
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
            default:
                b0 = a * ((a + 1) + (a - 1) * cos + sq); b1 = -2 * a * ((a - 1) + (a + 1) * cos); b2 = a * ((a + 1) + (a - 1) * cos - sq);
                a0 = (a + 1) - (a - 1) * cos + sq; a1 = 2 * ((a - 1) - (a + 1) * cos); a2 = (a + 1) - (a - 1) * cos - sq;
                break;
        }
        var w = 2 * Math.PI * freq / SampleRate;
        double c1 = Math.Cos(w), s1 = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
        double nr = b0 + b1 * c1 + b2 * c2, ni = -(b1 * s1 + b2 * s2);
        double dr = a0 + a1 * c1 + a2 * c2, di = -(a1 * s1 + a2 * s2);
        return 10 * Math.Log10((nr * nr + ni * ni) / (dr * dr + di * di));
    }

    public static double TotalDb(IEnumerable<EqBand> bands, double freq) => bands.Sum(b => ResponseDb(b, freq));
}
