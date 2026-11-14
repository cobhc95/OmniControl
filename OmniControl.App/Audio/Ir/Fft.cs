namespace OmniControl.App.Audio.Ir;

/// <summary>An in-place radix-2 complex FFT of one power-of-two size on separate real and imaginary arrays (for the impulse response's frequency-response curve). From TabForge (MIT, see NOTICE.txt).</summary>
internal sealed class Fft
{
    private readonly float[] _cos, _sin;
    private readonly int[] _reverse;

    private int Size { get; }

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(size), "The FFT size must be a power of two.");
        Size = size;
        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (var i = 0; i < size / 2; i++)
        {
            var angle = -2.0 * Math.PI * i / size;
            _cos[i] = (float)Math.Cos(angle);
            _sin[i] = (float)Math.Sin(angle);
        }
        _reverse = new int[size];
        var bits = System.Numerics.BitOperations.Log2((uint)size);
        for (var i = 0; i < size; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
            _reverse[i] = r;
        }
    }

    public void Forward(float[] re, float[] im) => Transform(re, im);

    private void Transform(float[] re, float[] im)
    {
        var n = Size;
        for (var i = 0; i < n; i++)
        {
            var j = _reverse[i];
            if (j <= i) continue;
            (re[i], re[j]) = (re[j], re[i]);
            (im[i], im[j]) = (im[j], im[i]);
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var half = len >> 1;
            var step = n / len;
            for (var start = 0; start < n; start += len)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = _cos[k * step];
                    var wi = _sin[k * step];
                    var a = start + k;
                    var b = a + half;
                    var tr = re[b] * wr - im[b] * wi;
                    var ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }
}
