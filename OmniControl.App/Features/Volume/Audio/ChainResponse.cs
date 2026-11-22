namespace OmniControl.App.Features.Volume.Audio;

/// A chain's total response (what the FX window's header draws): <paramref name="Db"/> at each frequency asked for, its highest point
/// (<paramref name="PeakDb"/> at <paramref name="PeakHz"/>), and the enabled effects that are not part of it, each with its reason.
/// <paramref name="RunsPlugins"/>: the engine runs at least one plug-in of the chain (its delay is the plug-in's own business).
public sealed record ChainCurve(double[] Db, double PeakDb, double PeakHz, IReadOnlyList<string> NotDrawn, bool RunsPlugins);

/// The response of a whole chain as the engine that runs the row really plays it, from the stored settings (no UI): the curves of
/// the effects that engine runs and whose response is fixed (every equaliser and Gain, with their wet mix) added in dB, since effects
/// in series multiply. Everything else that is switched on is listed, never drawn as if it were flat by choice: level-dependent
/// effects (compressor, limiter, gate), effects with no fixed curve (delay, reverb, IR Loader), plug-ins (unknown), and what the
/// engine does not run. The per-effect maths is <see cref="EqMath.CurveDb"/>, the one the engine and the editors use.
public static class ChainResponse
{
    /// <param name="engine">The engine that runs the row (<see cref="ModeMethod.Engine"/>).</param>
    /// <param name="apoBlends">Equalizer APO blends a wet mix below 100 % (<see cref="EffectsRules.ApoBlends"/>); when not, it leaves such effects out.</param>
    /// <param name="freqs">Where the curve is wanted (a <see cref="EqMath.LogGrid"/>); the peak is also looked for at every band's own frequency inside that range.</param>
    public static ChainCurve Of(IReadOnlyList<FxSlot> slots, FxEngines engine, bool apoBlends, IReadOnlyList<double> freqs)
    {
        var drawn = new List<(IReadOnlyList<EqBand> Bands, double GainDb, double Wet)>();
        var notDrawn = new List<string>();
        var runsPlugins = false;
        foreach (var slot in slots.Where(s => s.Enabled))
        {
            runsPlugins |= FxCatalog.IsPlugin(slot) && FxCatalog.EnginesOf(slot).HasFlag(engine) && (engine != FxEngines.Apo || FxCatalog.ApoRuns(slot, apoBlends));
            if (NotDrawnReason(slot, engine, apoBlends) is { } reason) notDrawn.Add($"{slot.Name} ({reason})");
            else drawn.Add(Linear(slot));
        }

        var (low, high) = (freqs.Min(), freqs.Max());
        var all = freqs.Concat(drawn.SelectMany(d => d.Bands).Where(b => !EqMath.IsFlat(b)).Select(b => b.Freq)
            .Where(f => f >= low && f <= high).Distinct()).ToList();
        var total = new double[all.Count];
        foreach (var (bands, gainDb, wet) in drawn)
        {
            var curve = EqMath.CurveDb(bands, gainDb, all, wet);
            for (var i = 0; i < total.Length; i++) total[i] += curve[i];
        }

        var peak = 0;
        for (var i = 1; i < total.Length; i++) if (total[i] > total[peak]) peak = i;
        return new ChainCurve(total[..freqs.Count], total[peak], all[peak], notDrawn, runsPlugins);
    }

    /// An equaliser's bands and overall gain, or a Gain effect as a gain alone, with the slot's wet mix (0-1).
    private static (IReadOnlyList<EqBand> Bands, double GainDb, double Wet) Linear(FxSlot slot)
    {
        var wet = Core.Models.Percent.ToScalar(slot.Wet);
        return FxCatalog.EqLayoutOf(slot) is { } eq
            ? (slot.Bands ?? eq.Default(), FxCatalog.EqGainOf(slot), wet)
            : (Array.Empty<EqBand>(), slot.Values.FirstOrDefault(), wet);
    }

    /// Why an enabled effect is not part of the drawn total; null when it is (an equaliser or Gain that the engine runs).
    private static string? NotDrawnReason(FxSlot slot, FxEngines engine, bool apoBlends)
    {
        if (FxCatalog.IsPlugin(slot) && NotRunReason(slot, engine, apoBlends) is null) return "plug-in: its response is not known";
        if (FxCatalog.EqLayoutOf(slot) is null && slot.Kind != FxCatalog.GainKind && FxCatalog.EnginesOf(slot).HasFlag(engine)) return "its response is not drawn";
        return NotRunReason(slot, engine, apoBlends);
    }

    /// Why the engine that runs the row does not run this effect (it passes the sound unchanged there); null when it runs it.
    public static string? NotRunReason(FxSlot slot, FxEngines engine, bool apoBlends)
    {
        var engines = FxCatalog.EnginesOf(slot);
        if (engines == FxEngines.None) return "saved only, it does not process sound yet";
        if (!engines.HasFlag(engine)) return engine == FxEngines.Apo ? "not run by Equalizer APO" : "runs only in Equalizer APO";
        if (engine == FxEngines.Apo && !FxCatalog.ApoRuns(slot, apoBlends)) return $"not run: {EffectsRules.ApoNoBlendReason}";
        return null;
    }
}
