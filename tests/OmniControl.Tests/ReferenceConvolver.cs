using System.Numerics;
using OmniControl.App.Features.Volume.Audio.Ir;

namespace OmniControl.Tests;

/// The convolver as it was before the non-uniform, multi-core one (commit 2a6f49c), kept here only as the reference the new one is compared
/// with: a 128-tap direct head, then uniform partitions of 128 up to 2048 and of 1024 for the rest, complex FFTs, all on the calling thread.
internal sealed class ReferenceConvolver
{
    private const int HeadLength = 128, SmallBlock = 128, SmallEnd = 2048, LargeBlock = 1024;

    private readonly float[] _headReversed;
    private readonly float[] _history;
    private int _historyPos;
    private readonly Stage? _small, _large;
    private int _smallPos;

    public ReferenceConvolver(ReadOnlySpan<float> ir)
    {
        var head = Math.Min(ir.Length, HeadLength);
        _headReversed = new float[HeadLength];
        for (var i = 0; i < head; i++) _headReversed[HeadLength - 1 - i] = ir[i];
        _history = new float[2 * HeadLength];
        if (ir.Length > HeadLength) _small = new Stage(ir, SmallBlock, HeadLength, Math.Min(ir.Length, SmallEnd));
        if (ir.Length > SmallEnd) _large = new Stage(ir, LargeBlock, SmallEnd, ir.Length);
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        var frames = Math.Min(input.Length, output.Length);
        var done = 0;
        while (done < frames)
        {
            var run = Math.Min(frames - done, SmallBlock - _smallPos);
            for (var i = 0; i < run; i++)
            {
                var x = input[done + i];
                var y = Direct(x);
                if (_small is { } s) y += s.Push(x);
                if (_large is { } l) y += l.Push(x);
                output[done + i] = y;
            }
            done += run;
            _smallPos += run;
            if (_smallPos < SmallBlock) continue;
            _smallPos = 0;
            _small?.BlockIfFull();
            _large?.BlockIfFull();
        }
    }

    private float Direct(float x)
    {
        _historyPos = (_historyPos + 1) % HeadLength;
        _history[_historyPos] = x;
        _history[_historyPos + HeadLength] = x;
        var a = new ReadOnlySpan<float>(_history, _historyPos + 1, HeadLength);
        var sum = Vector<float>.Zero;
        var width = Vector<float>.Count;
        var i = 0;
        for (; i <= a.Length - width; i += width) sum += new Vector<float>(a[i..]) * new Vector<float>(_headReversed.AsSpan(i));
        var total = Vector.Dot(sum, Vector<float>.One);
        for (; i < a.Length; i++) total += a[i] * _headReversed[i];
        return total;
    }

    private sealed class Stage
    {
        private readonly int _n, _bins, _count;
        private readonly Fft _fft;
        private readonly float[] _hRe, _hIm, _xRe, _xIm, _accRe, _accIm, _re, _im, _prev, _cur, _ring;
        private int _curPos, _line, _ringPos;

        public Stage(ReadOnlySpan<float> ir, int n, int start, int end)
        {
            _n = n;
            _bins = n + 1;
            _count = (end - start + n - 1) / n;
            _fft = new Fft(2 * n);
            _hRe = new float[_count * _bins]; _hIm = new float[_count * _bins];
            _xRe = new float[_count * _bins]; _xIm = new float[_count * _bins];
            _accRe = new float[_bins]; _accIm = new float[_bins];
            _re = new float[2 * n]; _im = new float[2 * n];
            _prev = new float[n]; _cur = new float[n];
            _ring = new float[start / n * n];
            for (var k = 0; k < _count; k++)
            {
                Array.Clear(_re); Array.Clear(_im);
                var from = start + k * n;
                for (var i = 0; i < n && from + i < end; i++) _re[i] = ir[from + i];
                _fft.Forward(_re, _im);
                Array.Copy(_re, 0, _hRe, k * _bins, _bins);
                Array.Copy(_im, 0, _hIm, k * _bins, _bins);
            }
        }

        public float Push(float x)
        {
            _cur[_curPos++] = x;
            var y = _ring[_ringPos];
            _ringPos = (_ringPos + 1) % _ring.Length;
            return y;
        }

        public void BlockIfFull()
        {
            if (_curPos < _n) return;
            _curPos = 0;
            Array.Copy(_prev, 0, _re, 0, _n);
            Array.Copy(_cur, 0, _re, _n, _n);
            Array.Clear(_im);
            _fft.Forward(_re, _im);
            Array.Copy(_re, 0, _xRe, _line * _bins, _bins);
            Array.Copy(_im, 0, _xIm, _line * _bins, _bins);
            Array.Copy(_cur, _prev, _n);
            Array.Clear(_accRe); Array.Clear(_accIm);
            for (var k = 0; k < _count; k++) MultiplyAdd((_line - k + _count) % _count * _bins, k * _bins);
            _line = (_line + 1) % _count;
            for (var j = 0; j <= _n; j++) { _re[j] = _accRe[j]; _im[j] = _accIm[j]; }
            for (var j = 1; j < _n; j++) { _re[2 * _n - j] = _accRe[j]; _im[2 * _n - j] = -_accIm[j]; }
            _fft.Inverse(_re, _im);
            var write = (_ringPos - _n + _ring.Length) % _ring.Length;
            Array.Copy(_re, _n, _ring, write, _n);
        }

        private void MultiplyAdd(int x, int h)
        {
            var width = Vector<float>.Count;
            var j = 0;
            for (; j <= _bins - width; j += width)
            {
                var xr = new Vector<float>(_xRe, x + j); var xi = new Vector<float>(_xIm, x + j);
                var hr = new Vector<float>(_hRe, h + j); var hi = new Vector<float>(_hIm, h + j);
                (new Vector<float>(_accRe, j) + xr * hr - xi * hi).CopyTo(_accRe, j);
                (new Vector<float>(_accIm, j) + xr * hi + xi * hr).CopyTo(_accIm, j);
            }
            for (; j < _bins; j++)
            {
                float xr = _xRe[x + j], xi = _xIm[x + j], hr = _hRe[h + j], hi = _hIm[h + j];
                _accRe[j] += xr * hr - xi * hi;
                _accIm[j] += xr * hi + xi * hr;
            }
        }
    }
}
