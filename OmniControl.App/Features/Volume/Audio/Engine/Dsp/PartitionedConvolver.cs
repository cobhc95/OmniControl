using Vector = System.Numerics.Vector;
using System.Numerics;

namespace OmniControl.App.Features.Volume.Audio.Engine.Dsp;

// Owns: zero-latency convolution of one channel with one impulse response: the first HeadLength taps by direct FIR, the rest by overlap-save
//   FFT stages whose partitions grow along the response (128, 1024, 4096, 16384), each stage's latency hidden by where its taps start.
// Does not own: reading, resampling or levelling the IR (IrPrep), mixing or the swap of a new IR (IrStage).
// From TabForge (MIT, see NOTICE.txt); the growing partitions, the real FFT and the background tail are OmniControl's.
// Threads: built on any thread (all buffers allocated here); Process on the audio thread only, no allocation or lock. The tail stage's
//   block is computed on a thread-pool worker within its one-block slack (see Stage), so the channels and the tail use several cores.

/// One channel of convolution with no added latency, for any host block size. Taps [0, <see cref="HeadLength"/>) run as a direct FIR, the
/// rest in the stages of <see cref="Layout"/>. A stage of partition N whose taps start at a multiple of N (at least N) has its block ready
/// before its first output is due, so nothing is delayed; the tail stage starts at 2N, which leaves it one block of N to be computed in.
internal sealed partial class PartitionedConvolver
{
    public const int HeadLength = 128;

    /// The stages' partition and where their taps start; each ends where the next starts, the last at the end of the response, and the
    /// last runs on a worker. Every partition is a multiple of the first, so all blocks end on its boundaries.
    private static readonly (int Partition, int Start)[] Layout = { (128, 128), (1024, 2048), (4096, 8192), (16384, 32768) };
    private static int Boundary => Layout[0].Partition;

    private readonly float[] _headReversed;
    private readonly float[] _history;   // the last HeadLength inputs, written twice so one window is contiguous
    private int _historyPos;
    private readonly Stage[] _stages;
    private int _boundaryPos;            // samples into the current first-partition block

    public int Length { get; }

    public PartitionedConvolver(ReadOnlySpan<float> ir)
    {
        Length = ir.Length;
        var head = Math.Min(ir.Length, HeadLength);
        _headReversed = new float[HeadLength];
        for (var i = 0; i < head; i++) _headReversed[HeadLength - 1 - i] = ir[i];
        _history = new float[2 * HeadLength];
        var stages = new List<Stage>();
        for (var s = 0; s < Layout.Length && ir.Length > Layout[s].Start; s++)
        {
            var last = s == Layout.Length - 1;
            var end = last ? ir.Length : Math.Min(ir.Length, Layout[s + 1].Start);
            stages.Add(new Stage(ir, Layout[s].Partition, Layout[s].Start, end, background: last));
        }
        _stages = stages.ToArray();
    }

    /// Audio thread: writes the convolution of <paramref name="input"/> to <paramref name="output"/> (the same length; may be the same memory).
    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        var frames = Math.Min(input.Length, output.Length);
        var done = 0;
        while (done < frames)
        {
            var run = Math.Min(frames - done, Boundary - _boundaryPos);
            var x = input.Slice(done, run);
            var y = output.Slice(done, run);
            foreach (var stage in _stages) stage.Take(x); // before y is written: they may be the same memory
            for (var i = 0; i < run; i++) y[i] = Direct(x[i]);
            foreach (var stage in _stages) stage.AddTo(y);
            done += run;
            _boundaryPos += run;
            if (_boundaryPos < Boundary) continue;
            _boundaryPos = 0;
            foreach (var stage in _stages) stage.BlockIfFull();
        }
    }

    private float Direct(float x)
    {
        _historyPos = (_historyPos + 1) % HeadLength;
        _history[_historyPos] = x;
        _history[_historyPos + HeadLength] = x;
        // Window [pos+1, pos+HeadLength] is the last HeadLength inputs oldest first; the newest meets tap 0 (stored last).
        return Dot(new ReadOnlySpan<float>(_history, _historyPos + 1, HeadLength), _headReversed);
    }

    private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var sum = Vector<float>.Zero;
        var width = Vector<float>.Count;
        var i = 0;
        for (; i <= a.Length - width; i += width) sum += new Vector<float>(a[i..]) * new Vector<float>(b[i..]);
        var total = Vector.Dot(sum, Vector<float>.One);
        for (; i < a.Length; i++) total += a[i] * b[i];
        return total;
    }
}
