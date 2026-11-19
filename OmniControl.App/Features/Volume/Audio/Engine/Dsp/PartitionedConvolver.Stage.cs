using System.Numerics;

namespace OmniControl.App.Features.Volume.Audio.Engine.Dsp;

// Owns: one uniformly-partitioned overlap-save stage of the convolver (partition N, real FFT 2N, a frequency-domain delay line of K spectra),
//   computed at once on the audio thread or, for the tail, on a thread-pool worker; split from PartitionedConvolver.cs.
// Does not own: the direct head, the layout or the order the stages run in (PartitionedConvolver).
// From TabForge (MIT, see NOTICE.txt).
internal sealed partial class PartitionedConvolver
{
    /// Taps [start, end) in partitions of N; start is a multiple of N and at least N, so each output block is ready before it is due.
    /// A background stage starts at 2N or later: the block whose input is complete is handed to a worker and collected one block later,
    /// when it is due. If the worker has not started it by then, the audio thread computes it itself (a late block costs a spike, never a
    /// gap); if the worker is part-way, the audio thread spins until it is done (no lock, no sleep). Either way the output is the same.
    private sealed class Stage : IThreadPoolWorkItem
    {
        private const int Idle = 0, Posted = 1, Running = 2, Done = 3; // the background block's state
        private const int Blocks = 3; // input blocks kept: the two the posted block's transform reads and the one being filled meanwhile

        private readonly int _n, _bins, _count;
        private readonly bool _background;
        private readonly RealFft _fft;
        private readonly float[] _hRe, _hIm;          // K spectra of N+1 bins (the rest is the mirror image)
        private readonly float[] _xRe, _xIm;          // the delay line: the last K input spectra
        private readonly float[] _accRe, _accIm, _time;
        private readonly float[] _input;              // Blocks input blocks of N, used in turn
        private readonly float[] _ring;               // the output not yet due: start / N blocks (start / N - 1 in the background)
        private int _fill, _block, _line, _ringPos;
        private int _job, _jobBlock;                  // the background block's state and which input block it transforms

        public Stage(ReadOnlySpan<float> ir, int n, int start, int end, bool background)
        {
            _n = n;
            _bins = n + 1;
            _count = (end - start + n - 1) / n;
            _background = background;
            _fft = new RealFft(2 * n);
            _hRe = new float[_count * _bins]; _hIm = new float[_count * _bins];
            _xRe = new float[_count * _bins]; _xIm = new float[_count * _bins];
            _accRe = new float[_bins]; _accIm = new float[_bins];
            _time = new float[2 * n];
            _input = new float[Blocks * n];
            _ring = new float[background ? start - n : start];
            var taps = new float[n];
            var zeros = new float[n];
            for (var k = 0; k < _count; k++)
            {
                var from = start + k * n;
                Array.Clear(taps);
                ir.Slice(from, Math.Min(n, end - from)).CopyTo(taps);
                _fft.Forward(taps, zeros, _hRe.AsSpan(k * _bins, _bins), _hIm.AsSpan(k * _bins, _bins));
            }
        }

        /// Takes a run of input (never past the end of the block being filled).
        public void Take(ReadOnlySpan<float> x)
        {
            x.CopyTo(_input.AsSpan(_block * _n + _fill));
            _fill += x.Length;
        }

        /// Adds this stage's output for the same run to <paramref name="y"/> (a run never wraps the ring: its length is a multiple of every run's boundary).
        public void AddTo(Span<float> y)
        {
            var due = _ring.AsSpan(_ringPos, y.Length);
            var width = Vector<float>.Count;
            var i = 0;
            for (; i <= y.Length - width; i += width) (new Vector<float>(y[i..]) + new Vector<float>(due[i..])).CopyTo(y[i..]);
            for (; i < y.Length; i++) y[i] += due[i];
            _ringPos += y.Length;
            if (_ringPos == _ring.Length) _ringPos = 0;
        }

        /// At a first-partition boundary: when this stage's input block is full, computes (or hands to a worker) the output block it makes.
        public void BlockIfFull()
        {
            if (_fill < _n) return;
            _fill = 0;
            if (_background)
            {
                if (Volatile.Read(ref _job) != Idle) Collect();
                _jobBlock = _block;
                Volatile.Write(ref _job, Posted);
                ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
            }
            else
            {
                Transform(_block);
                Deliver();
            }
            _block = (_block + 1) % Blocks;
        }

        /// Worker: computes the posted block unless the audio thread already took it.
        public void Execute()
        {
            if (Interlocked.CompareExchange(ref _job, Running, Posted) == Posted) Run();
        }

        /// Audio thread: the posted block is due now.
        private void Collect()
        {
            if (Interlocked.CompareExchange(ref _job, Running, Posted) == Posted) Run(); // the worker never started it: done here, still on time
            else
            {
                var spin = new SpinWait();
                while (Volatile.Read(ref _job) != Done) spin.SpinOnce(sleep1Threshold: -1);
            }
            Deliver();
            _job = Idle;
        }

        /// Computes the posted block on whichever thread took it. A fault (a bug) is logged and the block is silent, so the audio thread never waits for ever.
        private void Run()
        {
            try
            {
                Transform(_jobBlock);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException)
            {
                Array.Clear(_time);
                Guard.Log("Effects: the IR Loader's tail convolution", ex);
            }
            finally
            {
                Volatile.Write(ref _job, Done);
            }
        }

        /// Overlap-save of the input block <paramref name="block"/> after the one before it: its spectrum joins the delay line, and the
        /// sum of every partition times its delayed input spectrum goes back to time in <see cref="_time"/> (the last N samples are the output).
        private void Transform(int block)
        {
            var before = (block + Blocks - 1) % Blocks;
            _fft.Forward(_input.AsSpan(before * _n, _n), _input.AsSpan(block * _n, _n), _xRe.AsSpan(_line * _bins, _bins), _xIm.AsSpan(_line * _bins, _bins));
            Array.Clear(_accRe); Array.Clear(_accIm);
            for (var k = 0; k < _count; k++) MultiplyAdd((_line - k + _count) % _count * _bins, k * _bins);
            _line = (_line + 1) % _count;
            _fft.Inverse(_accRe, _accIm, _time);
        }

        /// The block just read out is the oldest in the ring: its positions take the new block (due when they come round again).
        private void Deliver()
        {
            var write = (_ringPos - _n + _ring.Length) % _ring.Length;
            _time.AsSpan(_n, _n).CopyTo(_ring.AsSpan(write));
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
