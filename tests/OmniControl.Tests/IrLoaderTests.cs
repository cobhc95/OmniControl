using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine.Dsp;

namespace OmniControl.Tests;

/// <summary>The IR Loader's convolver against direct convolution, and chains saved before its new settings existed.</summary>
public class IrLoaderTests
{
    [Fact]
    public void ConvolverMatchesDirectConvolution()
    {
        var random = new Random(7);
        var ir = Enumerable.Range(0, 5000).Select(_ => (float)(random.NextDouble() - 0.5) * 0.1f).ToArray();
        var input = Enumerable.Range(0, 12000).Select(_ => (float)(random.NextDouble() - 0.5)).ToArray();
        var convolver = new PartitionedConvolver(ir);
        var output = new float[input.Length];
        for (var at = 0; at < input.Length; at += 480) convolver.Process(input.AsSpan(at, 480), output.AsSpan(at, 480));
        foreach (var n in new[] { 0, 100, 3000, 7000, 11999 })
        {
            double expected = 0;
            for (var k = 0; k < ir.Length && k <= n; k++) expected += ir[k] * input[n - k];
            Assert.Equal(expected, output[n], 3);
        }
    }

    [Fact]
    public void OldChainIsPaddedWithTheNewSettings()
    {
        var slot = new FxSlot { Kind = FxCatalog.IrKind, Values = new List<double> { 100, 3 } };
        FxCatalog.Complete(slot);
        Assert.Equal(4, slot.Values.Count);
        Assert.True(IrParams.IsOn(slot.Values[IrParams.AutoLevel]));
        Assert.False(IrParams.IsOn(slot.Values[IrParams.TrimSilence]));
    }
}
