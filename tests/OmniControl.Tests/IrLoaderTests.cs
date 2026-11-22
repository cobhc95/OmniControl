using OmniControl.App.Features.Volume.Audio.Engine.Dsp;

namespace OmniControl.Tests;

/// The IR Loader's convolver against direct convolution, and chains saved before its new settings existed.
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

    [Fact]
    public void RapidEditsWithTheSameFileShareOneLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-ir-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, IrWaveFileTests.Wav(1, 16, 1, 48000, 100));
        try
        {
            var first = new IrStage(path, new List<double> { 100, 0, 1, 0 }, loadHere: true);
            var stage = first;
            for (var gain = 1; gain <= 20; gain++) stage = new IrStage(path, new List<double> { 50, gain, 1, 0 }, stage, loadHere: true);
            Assert.Same(first.Loading, stage.Loading);
            Assert.NotNull(stage.Loading.Kernel?.Left);
            var trimmed = new IrStage(path, new List<double> { 50, 0, 1, 1 }, stage, loadHere: true);
            Assert.NotSame(stage.Loading, trimmed.Loading);
            Assert.Same(trimmed.Loading, new IrStage(path, new List<double> { 80, 3, 1, 1 }, trimmed, loadHere: true).Loading);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AChangedFileLoadsOnceAndStopsTheSupersededLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-ir-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, IrWaveFileTests.Wav(1, 16, 1, 48000, 100));
        try
        {
            var values = new List<double> { 100, 0, 1, 0 };
            var before = new IrStage(path, values, loadHere: true);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            var after = new IrStage(path, values, before, loadHere: true);
            Assert.NotSame(before.Loading, after.Loading);
            Assert.Same(after.Loading, new IrStage(path, values, after, loadHere: true).Loading);
            var newer = new IrStage("", values, after);
            Assert.NotSame(after.Loading, newer.Loading);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
