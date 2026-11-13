using System.Runtime.InteropServices;
using NAudio.Dsp;
using NAudio.Wave;

namespace OmniControl.Core.Engine;

/// <summary>
/// An extra playback device of an effects pipe. The pipe's first device is its clock: its playback thread mixes, runs the effects and
/// hands a copy of every processed block to the queue of each extra device. An extra device runs on its own clock, a little faster or
/// slower than the first, so it reads its queue through a resampler whose rate is nudged from how full that queue is (at most
/// <see cref="CoreTiming.PipeDriftMaxShift"/>): the queue stays near <see cref="CoreTiming.PipeDriftTargetMs"/> without a click.
/// A queue that still grows past <see cref="CoreTiming.PipeMaxQueueMs"/> is trimmed; one that runs dry plays silence until it has
/// gathered <see cref="CoreTiming.PipePrefillMs"/> again. Runs on that device's playback thread: no locks; the resampler's buffers
/// grow during the first blocks and are reused from then on.
/// </summary>
internal sealed class ExtraOutput : IWaveProvider
{
    private const int Channels = CoreTiming.PipeChannels;
    private const int SamplesPerMs = CoreTiming.PipeSamplesPerMs;

    private readonly WdlResampler _resampler = new();
    private readonly float[] _block = new float[CoreTiming.PipeMaxBlockFrames * Channels];
    private double _fillMs = CoreTiming.PipeDriftTargetMs;
    private bool _primed;

    public ExtraOutput()
    {
        _resampler.SetMode(true, 2, false, 64, 32); // linear interpolation with a light low-pass, NAudio's own resampler default
        _resampler.SetFilterParms(0.693f, 0.707f);
        _resampler.SetFeedMode(false); // asked for output frames; it says how many input frames it needs
        _resampler.SetRates(CoreTiming.PipeSampleRate, CoreTiming.PipeSampleRate);
    }

    /// <summary>The processed sound waiting for this device (the first device's thread writes, this device's thread reads).</summary>
    public AudioRing Queue { get; } = new(CoreTiming.PipeRingSamples);

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(CoreTiming.PipeSampleRate, Channels);

    public int QueuedMs => Queue.Count / SamplesPerMs;

    public int Read(Span<byte> buffer)
    {
        var output = MemoryMarshal.Cast<byte, float>(buffer);
        var done = 0;
        while (output.Length - done >= Channels)
        {
            var frames = Math.Min(_block.Length, output.Length - done) / Channels;
            var block = _block.AsSpan(0, frames * Channels);
            Produce(block, frames);
            block.CopyTo(output[done..]);
            done += block.Length;
        }
        output[done..].Clear();
        return buffer.Length;
    }

    private void Produce(Span<float> block, int frames)
    {
        var queued = Queue.Count;
        if (queued > CoreTiming.PipeMaxQueueMs * SamplesPerMs)
        {
            Queue.DropTo(CoreTiming.PipeDriftTargetMs * SamplesPerMs); // far behind (the first device stalled): start again from the target
            _fillMs = CoreTiming.PipeDriftTargetMs;
        }
        if (!_primed && Queue.Count < CoreTiming.PipePrefillMs * SamplesPerMs)
        {
            block.Clear();
            return;
        }
        _primed = true;
        _fillMs += ((double)Queue.Count / SamplesPerMs - _fillMs) * CoreTiming.PipeDriftSmoothing;
        var shift = Math.Clamp((_fillMs - CoreTiming.PipeDriftTargetMs) * CoreTiming.PipeDriftShiftPerMs,
            -CoreTiming.PipeDriftMaxShift, CoreTiming.PipeDriftMaxShift);
        _resampler.SetRates(CoreTiming.PipeSampleRate * (1 + shift), CoreTiming.PipeSampleRate); // a fuller queue is read a little faster

        var needed = _resampler.ResamplePrepare(frames, Channels, out var input);
        var wanted = input[..(needed * Channels)];
        wanted.Clear();
        if (Queue.MixInto(wanted) < wanted.Length) _primed = false; // ran dry: the gap is silence, then gather the prefill again
        var made = _resampler.ResampleOut(block, needed, frames, Channels);
        if (made < frames) block[(made * Channels)..].Clear();
    }
}
