namespace OmniControl.App.Features.Volume.Audio.Engine;

/// What an FX window's measurement plays its test signal through (<see cref="ChainMeasurer"/>).
public sealed partial class EffectsEngine
{
    /// UI thread: copies of what the row's engine runs now, in order (the master's for the device it plays on, with its device
    /// folders), each plug-in with its current settings; null while the row's effects are off or nothing is switched on.
    internal ChainMeasureJob? MeasureJobOf(string? key)
    {
        var chain = _mixer.FxChain(key);
        if (!FxSlot.AnyRuns(_mixer.IsFxOn(key), chain)) return null;
        var engine = MethodFor(key).Engine;
        var blends = ApoBlends;
        var parts = key is null ? FxFolders.PartsFor(chain, _bus?.Real.Id ?? _masterApoDevice) : new List<FxPart> { new(null, chain) };
        var notRun = new List<string>();
        var levelDependent = false;
        var copies = new List<FxPart>();
        foreach (var part in parts)
        {
            var effects = new List<FxSlot>();
            foreach (var slot in part.Effects.Where(s => s.Enabled))
            {
                if (ChainResponse.NotRunReason(slot, engine, blends) is { } reason)
                {
                    notRun.Add($"{slot.Name} ({reason})");
                    continue;
                }
                var copy = slot.Copy();
                if (FxCatalog.IsPlugin(slot)) copy.State = _plugins.StateOf(slot);
                levelDependent |= FxCatalog.IsPlugin(slot) || slot.Kind is FxCatalog.CompKind or FxCatalog.LimiterKind;
                effects.Add(copy);
            }
            copies.Add(new FxPart(part.Folder?.Copy(), effects));
        }
        return new ChainMeasureJob(copies, notRun, levelDependent, engine == FxEngines.Apo && ApoOwnEffects);
    }
}
