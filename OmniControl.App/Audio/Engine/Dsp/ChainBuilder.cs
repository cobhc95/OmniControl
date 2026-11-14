using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine.Dsp;

/// <summary>
/// Turns a row's stored chain (<see cref="FxSlot"/>s, the same list the FX window edits) into a <see cref="DspChain"/> for
/// OmniControl's own engine. Effects this engine does not run (<see cref="FxCatalog.EnginesOf"/>) are listed, not hidden. UI thread.
/// </summary>
internal static class ChainBuilder
{
    /// <param name="plugin">Gives the loaded plug-in of a plug-in slot (any format), or the reason it could not load.</param>
    public static DspChain Build(IEnumerable<FxSlot> slots, Func<FxSlot, (IPluginEffect? Effect, string? Error)> plugin)
    {
        var stages = new List<IDspStage>();
        var sources = new List<FxSlot>();
        var skipped = new List<string>();
        foreach (var slot in slots.Where(s => s.Enabled))
        {
            if (!FxCatalog.EnginesOf(slot).HasFlag(FxEngines.Program))
            {
                skipped.Add($"{slot.Name} (not built yet)");
                continue;
            }
            if (StageOf(slot, plugin, skipped) is not { } stage) continue;
            stages.Add(new WetStage(stage, Core.Models.Percent.ToScalar(slot.Wet), PipeFormat.MaxBlockSamples));
            sources.Add(slot);
        }
        return new DspChain(stages.ToArray(), sources.ToArray(), skipped);
    }

    private static IDspStage? StageOf(FxSlot slot, Func<FxSlot, (IPluginEffect? Effect, string? Error)> plugin, List<string> skipped)
    {
        if (FxCatalog.IsPlugin(slot)) return PluginStage(slot, plugin, skipped);
        // Every equaliser, parametric or graphic, is the same stage: a graphic one's bands are ordinary peaking bands.
        if (FxCatalog.EqLayoutOf(slot) is { } eq)
            return new EqStage(slot.Bands ?? eq.Default(), FxCatalog.EqGainOf(slot), PipeFormat.SampleRate, PipeFormat.Channels);
        return slot.Kind switch
        {
            FxCatalog.GainKind => new GainStage(slot.Values.FirstOrDefault()),
            FxCatalog.CompKind => new CompressorStage(Completed(slot), PipeFormat.SampleRate),
            FxCatalog.LimiterKind => new LimiterStage(Completed(slot), PipeFormat.SampleRate),
            _ => null,
        };
    }

    /// <summary>The slot's values with every setting present (a chain saved before a setting existed takes its default).</summary>
    private static IReadOnlyList<double> Completed(FxSlot slot)
    {
        FxCatalog.Complete(slot);
        return slot.Values;
    }

    private static IDspStage? PluginStage(FxSlot slot, Func<FxSlot, (IPluginEffect? Effect, string? Error)> plugin, List<string> skipped)
    {
        var (effect, error) = plugin(slot);
        if (effect is not null) return new PluginStage(effect);
        skipped.Add($"{slot.Name} ({error?.TrimEnd('.')})");
        return null;
    }
}
