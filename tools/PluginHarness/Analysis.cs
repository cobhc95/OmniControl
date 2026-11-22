namespace PluginHarness;

/// The test signal and the measurements (levels, null test, 1/3-octave spectrum).
internal static class Analysis
{
    public const int Rate = 48000, Block = 512, Seconds = 2;
    public const int Frames = Rate * Seconds;
    private const int FftSize = 65536, FftStart = 16384;
    private static readonly double[] BandCentres = Enumerable.Range(0, 28).Select(i => 25 * Math.Pow(2, i / 3.0)).Where(c => c <= 16000.5).ToArray();

    /// Pink noise at -18 dBFS RMS (fixed seed) plus a 0.5 s 1 kHz burst at -12 dBFS peak from 1.0 s; one channel.
    public static float[] TestSignal()
    {
        var rng = new Random(12345);
        var pink = new float[Frames];
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        for (var i = 0; i < Frames; i++)
        {
            var white = rng.NextDouble() * 2 - 1;
            b0 = 0.99886 * b0 + white * 0.0555179; b1 = 0.99332 * b1 + white * 0.0750759; b2 = 0.96900 * b2 + white * 0.1538520;
            b3 = 0.86650 * b3 + white * 0.3104856; b4 = 0.55000 * b4 + white * 0.5329522; b5 = -0.7616 * b5 - white * 0.0168980;
            pink[i] = (float)(b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362);
            b6 = white * 0.115926;
        }
        var scale = Math.Pow(10, -18 / 20.0) / Rms(pink, 0);
        var signal = new float[Frames];
        for (var i = 0; i < Frames; i++)
        {
            var burst = i >= Rate && i < Rate + Rate / 2 ? Math.Pow(10, -12 / 20.0) * Math.Sin(2 * Math.PI * 1000 * i / Rate) : 0;
            signal[i] = (float)(pink[i] * scale + burst);
        }
        return signal;
    }

    public static double Rms(float[] x, int shift)
    {
        double sum = 0;
        var n = 0;
        for (var i = Math.Max(0, shift); i < x.Length; i++) { sum += (double)x[i] * x[i]; n++; }
        return n == 0 ? 0 : Math.Sqrt(sum / n);
    }

    public static double Db(double ratio) => ratio <= 1e-12 ? -240 : 20 * Math.Log10(ratio);

    public static int NonFinite(float[] x) => x.Count(v => !float.IsFinite(v));

    /// Level of (output - input) against the input, with the output shifted by 0 or the reported latency, whichever matches better.
    public static double NullDb(float[] input, float[] output, int latency)
    {
        var reference = Rms(input, 0);
        return Db(Math.Min(Diff(input, output, 0), Diff(input, output, latency)) / reference);
    }

    private static double Diff(float[] input, float[] output, int shift)
    {
        double sum = 0;
        var n = 0;
        for (var i = 0; i + shift < output.Length; i++)
        {
            var d = (double)output[i + shift] - input[i];
            sum += d * d;
            n++;
        }
        return Math.Sqrt(sum / Math.Max(1, n));
    }

    /// Energy per 1/3-octave band (dB) of a Hann-windowed block of the signal.
    public static double[] Bands(float[] x)
    {
        var re = new double[FftSize];
        var im = new double[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            var s = FftStart + i < x.Length ? x[FftStart + i] : 0f;
            re[i] = float.IsFinite(s) ? s * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FftSize)) : 0;
        }
        Fft(re, im);
        var bands = new double[BandCentres.Length];
        var hz = (double)Rate / FftSize;
        for (var b = 0; b < bands.Length; b++)
        {
            double energy = 0;
            var lo = BandCentres[b] / Math.Pow(2, 1 / 6.0);
            var hi = BandCentres[b] * Math.Pow(2, 1 / 6.0);
            for (var k = (int)Math.Ceiling(lo / hz); k < hi / hz && k < FftSize / 2; k++) energy += re[k] * re[k] + im[k] * im[k];
            bands[b] = energy <= 1e-30 ? -300 : 10 * Math.Log10(energy);
        }
        return bands;
    }

    /// Largest |change| in any band between two spectra (dB).
    public static double MaxBandChange(double[] a, double[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Max();

    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            double wr = Math.Cos(angle), wi = Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var a = i + k;
                    var c = a + len / 2;
                    var tr = re[c] * cr - im[c] * ci;
                    var ti = re[c] * ci + im[c] * cr;
                    re[c] = re[a] - tr; im[c] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }
    }
}
