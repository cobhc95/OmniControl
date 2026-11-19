using OmniControl.App.Features.Volume.Audio.Ir;

namespace OmniControl.App.Features.Volume.Audio.Engine.Dsp;

// Owns: the FFT of a real signal of one power-of-two size through a complex FFT of half that size (even samples as the real part,
//   odd ones as the imaginary part, then one pass to split them), so it costs about half the complex FFT the convolver used before.
// Does not own: the complex FFT itself (Fft).
// Threads: one caller at a time (the scratch is its own); no allocation after construction.

/// FFT of <c>size</c> real samples (a power of two, at least 4) to its <c>size / 2 + 1</c> bins (the rest is their mirror image), and back.
internal sealed class RealFft
{
    private readonly int _half;
    private readonly Fft _fft;
    private readonly float[] _wRe, _wIm; // e^(-2 pi i k / size) for k in [0, size / 2)
    private readonly float[] _zRe, _zIm; // the half-size complex signal

    public RealFft(int size)
    {
        _half = size / 2;
        _fft = new Fft(_half);
        _wRe = new float[_half]; _wIm = new float[_half];
        for (var k = 0; k < _half; k++)
        {
            var angle = -2.0 * Math.PI * k / size;
            _wRe[k] = (float)Math.Cos(angle);
            _wIm[k] = (float)Math.Sin(angle);
        }
        _zRe = new float[_half]; _zIm = new float[_half];
    }

    /// The spectrum of the signal <paramref name="first"/> followed by <paramref name="second"/> (each <c>size / 2</c> long) into
    /// <paramref name="re"/> and <paramref name="im"/> (<c>size / 2 + 1</c> bins each).
    public void Forward(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> re, Span<float> im)
    {
        var quarter = _half / 2;
        for (var n = 0; n < quarter; n++)
        {
            _zRe[n] = first[2 * n]; _zIm[n] = first[2 * n + 1];
            _zRe[quarter + n] = second[2 * n]; _zIm[quarter + n] = second[2 * n + 1];
        }
        _fft.Forward(_zRe, _zIm);
        re[0] = _zRe[0] + _zIm[0]; im[0] = 0;
        re[_half] = _zRe[0] - _zIm[0]; im[_half] = 0;
        for (var k = 1; k < _half; k++)
        {
            // Z[k] = E[k] + i O[k], where E and O are the spectra of the even and the odd samples; X[k] = E[k] + W^k O[k].
            float ar = _zRe[k], ai = _zIm[k], br = _zRe[_half - k], bi = -_zIm[_half - k];
            float er = 0.5f * (ar + br), ei = 0.5f * (ai + bi);
            float oRe = 0.5f * (ai - bi), oIm = -0.5f * (ar - br);
            float wr = _wRe[k], wi = _wIm[k];
            re[k] = er + wr * oRe - wi * oIm;
            im[k] = ei + wr * oIm + wi * oRe;
        }
    }

    /// The signal (<c>size</c> samples into <paramref name="output"/>) whose spectrum is <paramref name="re"/>, <paramref name="im"/>
    /// (<c>size / 2 + 1</c> bins); Forward then Inverse gives the signal back.
    public void Inverse(ReadOnlySpan<float> re, ReadOnlySpan<float> im, Span<float> output)
    {
        for (var k = 0; k < _half; k++)
        {
            // E[k] = (X[k] + conj X[H-k]) / 2, O[k] = (X[k] - conj X[H-k]) / (2 W^k), Z[k] = E[k] + i O[k].
            float ar = re[k], ai = im[k], br = re[_half - k], bi = -im[_half - k];
            float er = 0.5f * (ar + br), ei = 0.5f * (ai + bi);
            float dr = 0.5f * (ar - br), di = 0.5f * (ai - bi);
            float wr = _wRe[k], wi = _wIm[k];
            float oRe = dr * wr + di * wi, oIm = di * wr - dr * wi;
            _zRe[k] = er - oIm; _zIm[k] = ei + oRe;
        }
        _fft.Inverse(_zRe, _zIm);
        for (var n = 0; n < _half; n++)
        {
            output[2 * n] = _zRe[n];
            output[2 * n + 1] = _zIm[n];
        }
    }
}
