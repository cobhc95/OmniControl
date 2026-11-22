namespace OmniControl.App.Features.Volume.Audio.Engine.Dsp;

/// Turns a row's stored chain (<see cref="FxSlot"/>s, the same list the FX window edits) into a <see cref="DspChain"/> for
/// OmniControl's own engine. Effects this engine does not run (<see cref="FxCatalog.EnginesOf"/>) are listed, not hidden. UI thread.
internal static class ChainBuilder
{
    /// <param name="plugin">Gives the loaded plug-in of a plug-in slot (any format), or the reason it could not load.</param>
    public static DspChain Build(IEnumerable<FxSlot> slots, Func<FxSlot, (IPluginEffect? Effect, string? Error)> plugin) =>
        BuildParts(new[] { new FxPart(null, slots.ToList()) }, plugin);

    /// <see cref="Build"/> for the master's parts (<see cref="FxFolders.PartsFor"/>): a device folder's effects are wrapped in its Mix,
    /// their result blended with the sound that entered the folder. <paramref name="loadHere"/>: files are read now, on this thread (a measurement).
    public static DspChain BuildParts(IEnumerable<FxPart> parts, Func<FxSlot, (IPluginEffect? Effect, string? Error)> plugin, bool loadHere = false)
    {
        var stages = new List<IDspStage>();
        var sources = new List<FxSlot>();
        var skipped = new List<string>();
        foreach (var part in parts)
        {
            var from = stages.Count;
            foreach (var slot in part.Effects.Where(s => s.Enabled))
            {
                if (!FxCatalog.EnginesOf(slot).HasFlag(FxEngines.Program))
                {
                    skipped.Add($"{slot.Name} (not built yet)");
                    continue;
                }
                if (StageOf(slot, plugin, skipped, loadHere) is not { } stage) continue;
                stages.Add(new WetStage(stage, Core.Models.Percent.ToScalar(slot.Wet), PipeFormat.MaxBlockSamples));
                sources.Add(slot);
            }
            if (part.Folder is not { } folder || stages.Count == from) continue;
            var tap = new MixTap();
            stages.Insert(from, new MixOpenStage(tap));
            sources.Insert(from, folder);
            stages.Add(new MixCloseStage(tap, Core.Models.Percent.ToScalar(folder.Wet)));
            sources.Add(folder);
        }
        return new DspChain(stages.ToArray(), sources.ToArray(), skipped);
    }

    private static IDspStage? StageOf(FxSlot slot, Func<FxSlot, (IPluginEffect? Effect, string? Error)> plugin, List<string> skipped, bool loadHere)
    {
        if (FxCatalog.IsPlugin(slot)) return PluginStage(slot, plugin, skipped);
        // Every equaliser, parametric or graphic, is the same stage: a graphic one's bands are ordinary peaking bands.
        if (FxCatalog.EqLayoutOf(slot) is { } eq)
            return new EqStage(slot.Bands ?? eq.Default(), FxCatalog.EqGainOf(slot), PipeFormat.SampleRate, PipeFormat.Channels);
        return slot.Kind switch
        {
            FxCatalog.GainKind => new GainStage(slot.Values.FirstOrDefault()),
            FxCatalog.IrKind => IrStageOf(slot, skipped, loadHere),
            FxCatalog.CompKind => new CompressorStage(Completed(slot), PipeFormat.SampleRate),
            FxCatalog.LimiterKind => new LimiterStage(Completed(slot), PipeFormat.SampleRate),
            _ => null,
        };
    }

    /// The IR Loader's stage, or none (said on the row) when its file is missing: the sound then passes unchanged, which must not read as running.
    private static IDspStage? IrStageOf(FxSlot slot, List<string> skipped, bool loadHere)
    {
        if (slot.Path.Length > 0 && Ir.IrFileStamp.Of(slot.Path) == 0)
        {
            skipped.Add($"{slot.Name} (its file is missing)");
            return null;
        }
        return new IrStage(slot.Path, Completed(slot), loadHere);
    }

    /// The slot's values with every setting present (a chain saved before a setting existed takes its default).
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
