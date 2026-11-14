using System.Runtime.InteropServices;
using NAudio.Wave;

namespace OmniControl.Core.Engine;

/// <summary>
/// An extra playback device of an effects pipe. The pipe's first device is its clock: its playback thread mixes, runs the effects and
/// hands a copy of every processed block to the queue of each extra device. An extra device runs on its own clock, a little faster or
/// slower than the first, so it reads its queue through a <see cref="DriftReader"/> that keeps it near
/// <see cref="CoreTiming.PipeDriftTargetMs"/> without a click.
/// </summary>
internal sealed class ExtraOutput : IWaveProvider
{
    private const int Channels = CoreTiming.PipeChannels;

    private readonly DriftReader _reader = new(new AudioRing(CoreTiming.PipeRingSamples), CoreTiming.PipeDriftTargetMs);
    private readonly float[] _block = new float[CoreTiming.PipeMaxBlockFrames * Channels];

    /// <summary>The processed sound waiting for this device (the first device's thread writes, this device's thread reads).</summary>
    public AudioRing Queue => _reader.Ring;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(CoreTiming.PipeSampleRate, Channels);

    public int QueuedMs => _reader.QueuedMs;

    public int Read(Span<byte> buffer)
    {
        var output = MemoryMarshal.Cast<byte, float>(buffer);
        var done = 0;
        while (output.Length - done >= Channels)
        {
            var frames = Math.Min(_block.Length, output.Length - done) / Channels;
            var block = _block.AsSpan(0, frames * Channels);
            _reader.Read(block);
            block.CopyTo(output[done..]);
            done += block.Length;
        }
        output[done..].Clear();
        return buffer.Length;
    }
}
