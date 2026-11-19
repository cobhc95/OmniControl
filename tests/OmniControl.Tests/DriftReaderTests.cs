using OmniControl.Core;
using Xunit.Abstractions;

namespace OmniControl.Tests;

/// The drift reader shared by a pipe's captures and extra devices, on a simulated clock: a writer faster or slower than the reader for
/// ten minutes of 10 ms blocks. Pure arithmetic, no device is opened. An extra device (40 ms target) holds 0.5 % either way; a capture
/// (20 ms target, the old prefill, so no added delay) holds 0.5 % fast and 0.2 % slow, twenty times a real crystal's drift (~0.01 %):
/// 0.5 % slow would need the queue 25 ms below its target.
public class DriftReaderTests(ITestOutputHelper output)
{
    private const int Channels = CoreTiming.PipeChannels;
    private const int BlockFrames = CoreTiming.PipeSampleRate / 100; // 10 ms
    private const int Blocks = 10 * 60 * 100; // ten minutes
    private const int SettleBlocks = 500; // five seconds to settle

    [Theory]
    [InlineData(CoreTiming.PipeCaptureDriftTargetMs, 1.005)]
    [InlineData(CoreTiming.PipeCaptureDriftTargetMs, 1.002)]
    [InlineData(CoreTiming.PipeCaptureDriftTargetMs, 0.998)]
    [InlineData(CoreTiming.PipeDriftTargetMs, 1.005)]
    [InlineData(CoreTiming.PipeDriftTargetMs, 0.995)]
    public void WriterFastOrSlow_QueueStaysInBand_NoTrimNoGap(int targetMs, double writerRate)
    {
        var ring = new AudioRing(CoreTiming.PipeRingSamples);
        var reader = new DriftReader(ring, targetMs);
        var block = new float[BlockFrames * Channels];
        var written = 0.0;
        var writtenFrames = 0L;
        int low = int.MaxValue, high = 0;
        var quietest = float.MaxValue;
        var chunk = new float[CoreTiming.PipeMaxQueueMs * CoreTiming.PipeSamplesPerMs];
        Array.Fill(chunk, 0.5f); // a constant level: any gap or trim shows as a sample far from it

        // Start with the target already waiting, as a running capture has.
        ring.Write(chunk.AsSpan(0, targetMs * CoreTiming.PipeSamplesPerMs));
        for (var b = 0; b < Blocks; b++)
        {
            written += BlockFrames * writerRate;
            var frames = (int)(written - writtenFrames);
            writtenFrames += frames;
            ring.Write(chunk.AsSpan(0, frames * Channels));

            var queued = reader.QueuedMs; // what the reader finds when its turn comes
            reader.Read(block);
            if (b < SettleBlocks) continue;
            low = Math.Min(low, queued);
            high = Math.Max(high, queued);
            foreach (var x in block) quietest = Math.Min(quietest, x);
        }
        output.WriteLine($"target {targetMs} ms, writer x{writerRate}: queue {low}-{high} ms before each read, quietest sample {quietest}");
        Assert.True(low > 0, "the queue ran dry");
        Assert.True(high < CoreTiming.PipeMaxQueueMs, "the queue reached the trim point");
        Assert.True(quietest > 0.45f, "a gap of silence or a trim was heard");
    }
}
