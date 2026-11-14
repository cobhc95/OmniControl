using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.Core.Engine;
using Xunit.Abstractions;

namespace OmniControl.Tests;

/// <summary>
/// The sound a program's run makes from the owner's saved chain: the same <see cref="ChainBuilder"/> and the same
/// <see cref="ChainProcessor"/> (no row level, gate open) that <c>CableRoute</c> and <c>BusRun</c> build, fed a synthetic sine in
/// the pipe's format and blocks, the level at the tone measured with Goertzel.
/// </summary>
public class ProgramChainTests(ITestOutputHelper output)
{
    private const int Rate = PipeFormat.SampleRate, Channels = PipeFormat.Channels;
    private const int Block = 480; // 10 ms, like the pipe's playback reads
    private const double InputDb = -40; // a +24 dB boost stays below the limiter's ceiling

    private static (IPluginEffect?, string?) NoPlugins(FxSlot slot) => (null, "no plug-ins in this test");

    /// <summary>Gain in dB at <paramref name="hz"/> through a run's processor built from <paramref name="slots"/>.</summary>
    private static double GainDb(IEnumerable<FxSlot> slots, double hz)
    {
        var processor = new ChainProcessor(level: null, gateOpen: true); // exactly as CableRoute and BusRun make theirs
        processor.SetChain(ChainBuilder.Build(slots, NoPlugins));
        var amplitude = Math.Pow(10, InputDb / 20);
        var seconds = 1.0;
        var total = (int)(Rate * seconds);
        var measured = new List<float>();
        var buffer = new float[Block * Channels];
        for (var start = 0; start < total; start += Block)
        {
            for (var f = 0; f < Block; f++)
            {
                var v = (float)(amplitude * Math.Sin(2 * Math.PI * hz * (start + f) / Rate));
                for (var c = 0; c < Channels; c++) buffer[f * Channels + c] = v;
            }
            processor.Process(buffer, Channels, Rate);
            if (start >= total / 2) for (var f = 0; f < Block; f++) measured.Add(buffer[f * Channels]); // left, after every glide
        }
        return 20 * Math.Log10(Goertzel(measured, hz) / amplitude);
    }

    /// <summary>Amplitude of the tone at <paramref name="hz"/> in <paramref name="x"/>.</summary>
    private static double Goertzel(IReadOnlyList<float> x, double hz)
    {
        var w = 2 * Math.PI * hz / Rate;
        var coeff = 2 * Math.Cos(w);
        double s1 = 0, s2 = 0;
        foreach (var v in x)
        {
            var s0 = v + coeff * s1 - s2;
            s2 = s1;
            s1 = s0;
        }
        var power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
        return 2 * Math.Sqrt(Math.Max(power, 0)) / x.Count;
    }

    private static List<FxSlot> SpotifyChain() => OwnerPc.Settings().Apps.First(a => a.Key == OwnerPc.Spotify).Fx;

    [Fact]
    public void SpotifyChain_BoostsItsBandBy24Db()
    {
        var chain = SpotifyChain();
        var band = chain[0].Bands!.First(b => b.Gain == 24);
        var atBand = GainDb(chain, band.Freq);
        var at1k = GainDb(chain, 1000);
        var drawn1k = EqMath.CurveDb(chain[0].Bands!, FxCatalog.EqGainOf(chain[0]), new[] { 1000.0 })[0];
        output.WriteLine($"Spotify chain: {atBand:+0.00;-0.00} dB at {band.Freq:0} Hz, {at1k:+0.00;-0.00} dB at 1 kHz (the editor's curve says {drawn1k:+0.00;-0.00} dB)");
        Assert.InRange(atBand, 23.5, 24.5);
        Assert.InRange(at1k, drawn1k - 0.5, drawn1k + 0.5);
    }

    [Fact]
    public void MasterChain_MatchesItsCurve_ThroughTheSameProcessor()
    {
        var chain = OwnerPc.Settings().MasterFx;
        foreach (var hz in new[] { 100.0, 1000, 2492 })
        {
            var heard = GainDb(chain, hz);
            var drawn = EqMath.CurveDb(chain[0].Bands!, FxCatalog.EqGainOf(chain[0]), new[] { hz })[0];
            output.WriteLine($"master chain at {hz:0} Hz: {heard:+0.00;-0.00} dB (curve {drawn:+0.00;-0.00} dB)");
            Assert.InRange(heard, drawn - 0.5, drawn + 0.5);
        }
    }

    [Fact]
    public void EmptyOrSwitchedOffChain_PassesTheSoundUnchanged()
    {
        var empty = GainDb(Array.Empty<FxSlot>(), 1000);
        var off = SpotifyChain();
        off[0].Enabled = false;
        var disabled = GainDb(off, 1059);
        output.WriteLine($"empty chain {empty:+0.00;-0.00} dB, switched-off equaliser {disabled:+0.00;-0.00} dB");
        Assert.InRange(empty, -0.05, 0.05);
        Assert.InRange(disabled, -0.05, 0.05);
    }
}
