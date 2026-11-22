using OmniControl.App.Features.Volume.Audio.Engine;

namespace OmniControl.Tests;

/// Work saved on repeated saves: a chain's measurement digest, a plug-in's unchanged settings, and SaveSoon's one text per burst.
public class SettingsChurnTests
{
    private static ChainMeasureJob Job(string state) =>
        new(new[] { new FxPart(null, new List<FxSlot> { new() { Kind = FxCatalog.GainKind, Enabled = true, Values = new() { 3 }, State = state } }) },
            Array.Empty<string>(), false, false);

    [Fact]
    public void A_measurement_digest_is_short_and_follows_every_setting()
    {
        Assert.Equal(Job(new string('A', 100_000)).Signature, Job(new string('A', 100_000)).Signature);
        Assert.NotEqual(Job("AAAA").Signature, Job("AAAB").Signature);
        Assert.Equal(64, Job(new string('A', 100_000)).Signature.Length);
    }

    private sealed class FakePlugin : IPluginEffect
    {
        public byte[] State = { 1, 2, 3 };
        public string Name => "Fake";
        public int MaxBlockFrames => 512;
        public int InputChannels => 2;
        public int OutputChannels => 2;
        public int LatencyFrames => 0;
        public void Process(ReadOnlySpan<float> input, Span<float> output, int frames) { }
        public byte[] SaveState() => (byte[])State.Clone();
        public bool TakeChanged() => true;
        public void LoadState(byte[] state) => State = state;
        public IPluginEditor? CreateEditor() => null;
        public IReadOnlyList<PluginParam> Parameters => Array.Empty<PluginParam>();
        public double ValueOf(uint id) => 0;
        public void Set(uint id, double value) { }
        public string TextOf(uint id, double value) => "";
        public double PlainOf(uint id) => 0;
        public void SetPlain(uint id, double plain) { }
        public void Dispose() { }
    }

    [Fact]
    public void Unchanged_plugin_settings_are_not_saved_again()
    {
        var saves = 0;
        var plugin = new FakePlugin();
        var slot = new FxSlot();
        using var host = new PluginChainHost(() => saves++, _ => (plugin, null));
        host.Get(slot);
        host.SaveStates();
        host.SaveStates();
        Assert.Equal(1, saves);
        Assert.Equal(Convert.ToBase64String(plugin.State), slot.State);
        plugin.State = new byte[] { 4 };
        host.SaveStates();
        Assert.Equal(2, saves);
        Assert.Equal("BA==", slot.State);
    }

    private sealed class Counted
    {
        public int Reads;
        public int Value => ++Reads;
    }

    /// An owner whose posts never run, so the due timer can't make the text before the test reads it.
    private sealed class Held : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { }
    }

    [Fact]
    public void SaveSoon_on_an_owning_thread_makes_one_text_per_burst()
    {
        var store = new DataStore("churn-test.json", "Test", StoreKind.Saved);
        var value = new Counted();
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new Held());
        try
        {
            for (var i = 0; i < 5; i++) AppDataStore.SaveSoon(store, value);
            Assert.Equal(0, value.Reads);
            Assert.Equal("{\"Value\":1}", AppDataStore.LoadText(store));
            Assert.Equal(1, value.Reads);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
            AppDataStore.Delete(store);
        }
    }
}
