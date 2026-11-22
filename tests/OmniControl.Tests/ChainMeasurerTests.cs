using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.App.Features.Volume.Audio.Engine.Dsp;

namespace OmniControl.Tests;

/// The FX window's whole-chain measurement against what the chain is known to do: an equaliser's own curve, gains, an impulse
/// response read from a WAV file (its delay and its ring-out), switched-off effects, and each effect played once.
public class ChainMeasurerTests
{
    private static readonly double[] Freqs = EqMath.LogGrid(200, Eq.MinFreq, Eq.MaxFreq);

    private static (IPluginEffect?, string?) NoPlugins(FxSlot slot) => (null, "no plug-ins in this test");

    private static (double[] Db, int? LatencyFrames, double TailSeconds) Measure(params FxSlot[] slots) =>
        ChainMeasurer.Measure(ChainBuilder.BuildParts(new[] { new FxPart(null, slots) }, NoPlugins, loadHere: true), Freqs, CancellationToken.None);

    private static FxSlot Gain(double db, bool enabled = true) =>
        new() { Kind = FxCatalog.GainKind, Name = "Gain", Enabled = enabled, Values = new List<double> { db } };

    private static double At(double[] db, double hz) => db[Array.FindIndex(Freqs, f => f >= hz)];

    [Fact]
    public void EmptyChainIsFlatWithNoDelayOrTail()
    {
        var (db, latency, tail) = Measure();
        Assert.All(db, d => Assert.InRange(d, -0.01, 0.01));
        Assert.Equal(0, latency);
        Assert.InRange(tail, 0, 0.05);
    }

    [Fact]
    public void EqualiserMatchesItsOwnCurve()
    {
        var eq = FxCatalog.Create(FxCatalog.EqKind);
        eq.Bands = new List<EqBand>
        {
            new() { Id = 1, Type = EqType.Peak, Freq = 1000, Gain = 6, Q = 1 },
            new() { Id = 2, Type = EqType.LowShelf, Freq = 100, Gain = -4, Q = Eq.ShelfQ },
        };
        var expected = EqMath.CurveDb(eq.Bands, 0, Freqs);
        var (db, _, _) = Measure(eq);
        for (var i = 0; i < Freqs.Length; i++)
            if (Freqs[i] is >= 30 and <= 18000) Assert.InRange(db[i] - expected[i], -0.5, 0.5);
        Assert.InRange(At(db, 1000), 5.5, 6.5);
    }

    [Fact]
    public void SwitchedOffEffectIsSkipped()
    {
        var (db, _, _) = Measure(Gain(-24, enabled: false), Gain(-6));
        Assert.All(db, d => Assert.InRange(d, -6.1, -5.9));
    }

    [Fact]
    public void EachEffectIsPlayedOnce()
    {
        var (db, _, _) = Measure(Gain(3), Gain(3));
        Assert.All(db, d => Assert.InRange(d, 5.9, 6.1));
    }

    [Fact]
    public void ImpulseResponseGivesItsLevelAndDelay()
    {
        var ir = new float[4800];
        ir[480] = 0.5f; // 10 ms late, -6 dB
        var (db, latency, _) = WithIr(ir);
        foreach (var hz in new[] { 50.0, 1000, 10000 }) Assert.InRange(At(db, hz), -6.3, -5.7);
        Assert.Equal(480, latency);
    }

    [Fact]
    public void ReverbIrRingsOut()
    {
        var random = new Random(3);
        var length = 48000;
        var ir = new float[length];
        ir[0] = 1;
        for (var n = 1; n < length; n++) ir[n] = (float)((random.NextDouble() - 0.5) * 0.2 * Math.Exp(-n / (0.1 * 48000)));
        var (_, _, tail) = WithIr(ir);
        Assert.InRange(tail, 0.4, 1.1);
    }

    /// The chain with one IR Loader (no auto level, no trim) reading <paramref name="ir"/> from a temporary 48 kHz WAV file.
    private static (double[] Db, int? LatencyFrames, double TailSeconds) WithIr(float[] ir)
    {
        var path = Path.Combine(Path.GetTempPath(), $"omnicontrol-test-ir-{Guid.NewGuid():N}.wav");
        try
        {
            WriteFloatWav(path, ir, 48000);
            var slot = new FxSlot { Kind = FxCatalog.IrKind, Name = "IR", Path = path, Values = new List<double> { 100, 0, 0, 0 } };
            return Measure(slot);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteFloatWav(string path, float[] samples, int rate)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + samples.Length * 4); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)3); w.Write((short)1); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)32);
        w.Write("data"u8); w.Write(samples.Length * 4);
        foreach (var s in samples) w.Write(s);
    }
}
