using OmniControl.App.Features.Volume.Audio.Engine.Dsp;
using OmniControl.App.Features.Volume.Audio.Ir;
using Xunit.Abstractions;

namespace OmniControl.Tests;

/// The non-uniform, multi-core convolver against the one it replaced (<see cref="ReferenceConvolver"/>): the same output within float
/// tolerance for short and long impulse responses, any host block size and several channels running at once.
public class PartitionedConvolverTests(ITestOutputHelper output)
{
    private const int Rate = 48000;

    [Fact]
    public void RealFftMatchesTheComplexFft()
    {
        const int size = 256;
        var random = new Random(3);
        var signal = Enumerable.Range(0, size).Select(_ => (float)(random.NextDouble() - 0.5)).ToArray();
        float[] re = signal.ToArray(), im = new float[size];
        new Fft(size).Forward(re, im);
        float[] realRe = new float[size / 2 + 1], realIm = new float[size / 2 + 1], back = new float[size];
        var fft = new RealFft(size);
        fft.Forward(signal.AsSpan(0, size / 2), signal.AsSpan(size / 2), realRe, realIm);
        for (var k = 0; k <= size / 2; k++)
        {
            Assert.Equal(re[k], realRe[k], 1e-4);
            Assert.Equal(im[k], realIm[k], 1e-4);
        }
        fft.Inverse(realRe, realIm, back);
        for (var n = 0; n < size; n++) Assert.Equal(signal[n], back[n], 1e-5);
    }

    [Theory]
    [InlineData(100, 1, new[] { 1 })]
    [InlineData(1500, 2, new[] { 480 })]
    [InlineData(5000, 1, new[] { 4096 })]
    [InlineData(20000, 2, new[] { 37, 480, 1, 4096, 128 })]
    [InlineData(Rate, 2, new[] { 480 })]                   // 1 s
    [InlineData(Rate, 6, new[] { 441, 4096, 7 })]          // 1 s, 5.1
    [InlineData(5 * Rate, 2, new[] { 480, 1024, 333 })]    // 5 s
    [InlineData(20 * Rate, 2, new[] { 480 })]              // 20 s
    public void MatchesTheReference(int irLength, int channels, int[] blocks)
    {
        var refs = new ReferenceConvolver[channels];
        var news = new PartitionedConvolver[channels];
        var inputs = new float[channels][];
        var length = irLength + 3 * 16384 + 1000;
        var noise = Math.Min(length, Rate); // a second of sound, then silence: the whole tail rings out
        for (var c = 0; c < channels; c++)
        {
            var ir = DecayingNoise(irLength, seed: 11 + c);
            refs[c] = new ReferenceConvolver(ir);
            news[c] = new PartitionedConvolver(ir);
            var random = new Random(101 + c);
            inputs[c] = Enumerable.Range(0, length).Select(i => i < noise ? (float)(random.NextDouble() - 0.5) : 0f).ToArray();
        }
        var expected = inputs.Select(_ => new float[length]).ToArray();
        var actual = inputs.Select(_ => new float[length]).ToArray();
        for (int at = 0, b = 0; at < length; b++)
        {
            var run = Math.Min(blocks[b % blocks.Length], length - at);
            for (var c = 0; c < channels; c++)
            {
                refs[c].Process(inputs[c].AsSpan(at, run), expected[c].AsSpan(at, run));
                news[c].Process(inputs[c].AsSpan(at, run), actual[c].AsSpan(at, run));
            }
            at += run;
        }

        for (var c = 0; c < channels; c++)
        {
            double worst = 0, peak = 0;
            for (var i = 0; i < length; i++)
            {
                worst = Math.Max(worst, Math.Abs(expected[c][i] - actual[c][i]));
                peak = Math.Max(peak, Math.Abs(expected[c][i]));
            }
            output.WriteLine($"IR {irLength}, channel {c}: peak {peak:F3}, largest difference {worst:E2}");
            Assert.True(worst <= 1e-4 * Math.Max(1, peak), $"channel {c}: difference {worst:E2} at peak {peak:F3}");
        }
    }

    [Fact]
    public void LongTailMatchesDirectConvolutionInPlace()
    {
        var ir = DecayingNoise(3 * Rate, seed: 5);
        var random = new Random(9);
        var input = Enumerable.Range(0, 4 * Rate).Select(i => i < Rate ? (float)(random.NextDouble() - 0.5) : 0f).ToArray();
        var samples = input.ToArray();
        var convolver = new PartitionedConvolver(ir);
        for (var at = 0; at < samples.Length; at += 480) convolver.Process(samples.AsSpan(at, 480), samples.AsSpan(at, 480));
        foreach (var n in new[] { 0, 127, 2047, 8191, 32767, 32768, 50000, 100000, 3 * Rate + 1000, 4 * Rate - 1 })
        {
            double direct = 0;
            for (var k = 0; k < ir.Length && k <= n; k++) direct += ir[k] * (double)input[n - k];
            Assert.Equal(direct, samples[n], 1e-3);
        }
    }

    /// A reverb-like response: noise that falls by 60 dB over its length, scaled to about unit energy.
    private static float[] DecayingNoise(int length, int seed)
    {
        var random = new Random(seed);
        var ir = new float[length];
        for (var i = 0; i < length; i++) ir[i] = (float)((random.NextDouble() - 0.5) * Math.Exp(-6.9 * i / length));
        var scale = 1 / Math.Sqrt(ir.Sum(v => (double)v * v));
        for (var i = 0; i < length; i++) ir[i] = (float)(ir[i] * scale);
        return ir;
    }
}
