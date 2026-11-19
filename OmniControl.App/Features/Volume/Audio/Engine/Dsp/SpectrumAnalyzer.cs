using OmniControl.App.Features.Volume.Audio.Ir;

namespace OmniControl.App.Features.Volume.Audio.Engine.Dsp;

/// Turns the newest sound of a <see cref="SpectrumTap"/> into a level (dB) per log-spaced band across the equaliser's range: a Hann
/// window, one FFT, the loudest bin of each band, and a fall that is slower than the rise so the picture is calm. Pure maths, run off
/// the UI thread by <see cref="SpectrumSession"/>; one instance per session (it keeps its own smoothing).
internal sealed class SpectrumAnalyzer
{
    /// FFT size in frames, and the number of bands drawn.
    public const int Window = 4096, Bands = 96;

    /// The quietest level drawn (the bottom of the picture) and how far a band may fall per picture.
    public const double FloorDb = -90, FallDb = 3;

    private readonly Fft _fft = new(Window);
    private readonly float[] _re = new float[Window], _im = new float[Window], _hann = new float[Window];
    private readonly float[] _shown = new float[Bands];
    private long _lastWritten = -1;

    public SpectrumAnalyzer()
    {
        for (var i = 0; i < Window; i++) _hann[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Window - 1)));
        Array.Fill(_shown, (float)FloorDb);
    }

    /// The bands' levels now (a new array), or null while the tap has not heard enough sound yet.
    public float[]? Compute(SpectrumTap tap)
    {
        var silent = tap.Written == _lastWritten; // no new sound since the last picture: the bands fall
        _lastWritten = tap.Written;
        if (silent) Array.Clear(_re);
        else if (!tap.CopyLatest(_re)) return null;
        else for (var i = 0; i < Window; i++) _re[i] *= _hann[i];
        Array.Clear(_im);
        _fft.Forward(_re, _im);

        var binHz = (double)PipeFormat.SampleRate / Window;
        var ratio = Eq.MaxFreq / Eq.MinFreq;
        for (var band = 0; band < Bands; band++)
        {
            var low = Eq.MinFreq * Math.Pow(ratio, (double)band / Bands) / binHz;
            var high = Eq.MinFreq * Math.Pow(ratio, (double)(band + 1) / Bands) / binHz;
            var level = (int)high <= (int)low ? DbAt((low + high) / 2) : MaxDb((int)low, (int)high);
            _shown[band] = (float)Math.Max(level, Math.Max(FloorDb, _shown[band] - FallDb));
        }
        return (float[])_shown.Clone();
    }

    /// The level at a fractional bin, between its two neighbours (bands narrower than a bin).
    private double DbAt(double bin)
    {
        var lower = (int)bin;
        var part = bin - lower;
        return Db(lower) * (1 - part) + Db(Math.Min(lower + 1, Window / 2)) * part;
    }

    private double MaxDb(int from, int to)
    {
        var best = FloorDb;
        for (var bin = from; bin <= Math.Min(to, Window / 2); bin++) best = Math.Max(best, Db(bin));
        return best;
    }

    /// A bin's level in dB, 0 dB being a full-scale sine (the Hann window passes a quarter of the FFT size).
    private double Db(int bin)
    {
        var magnitude = Math.Sqrt((double)_re[bin] * _re[bin] + (double)_im[bin] * _im[bin]) / (Window / 4.0);
        return magnitude <= 0 ? FloorDb : Math.Max(FloorDb, 20 * Math.Log10(magnitude));
    }
}
