namespace OmniControl.App.Audio;

/// <summary>
/// How one equaliser kind makes, names and loads its bands; it sits on the kind's catalog entry (<see cref="FxKind.Eq"/>), so the
/// engine, the APO writer and the editor treat every equaliser alike. The Parametric's is <see cref="Eq.Layout"/>; a graphic
/// equaliser's carries its fixed bands (<see cref="Graphic"/>, <see cref="GraphicEq"/>). No UI.
/// </summary>
public sealed class EqLayout
{
    /// <summary>The name shown when the bands match no preset.</summary>
    public const string CustomName = "Custom";

    private readonly Func<List<EqBand>> _default;
    private readonly Func<List<EqBand>?, List<EqBand>> _upgrade;
    private readonly IReadOnlyList<(string Name, Func<List<EqBand>> Bands)> _presets;

    /// <param name="defaults">A new, flat set of bands.</param>
    /// <param name="presets">The named sets, the first being the default.</param>
    /// <param name="upgrade">Bands saved by any version, made fit for today (never returns null).</param>
    /// <param name="graphic">The fixed bands of a graphic equaliser; null for the Parametric, whose bands are free.</param>
    public EqLayout(Func<List<EqBand>> defaults, IReadOnlyList<(string Name, Func<List<EqBand>> Bands)> presets,
        Func<List<EqBand>?, List<EqBand>> upgrade, GraphicBands? graphic = null)
    {
        _default = defaults;
        _presets = presets;
        _upgrade = upgrade;
        Graphic = graphic;
    }

    public GraphicBands? Graphic { get; }

    /// <summary>The preset drop-down's entries: every preset, then <see cref="CustomName"/>.</summary>
    public IReadOnlyList<string> PresetNames => _presets.Select(p => p.Name).Append(CustomName).ToList();

    public List<EqBand> Default() => _default();
    public List<EqBand> Upgrade(List<EqBand>? bands) => _upgrade(bands);
    public List<EqBand> FromPreset(string name) => _presets.First(p => p.Name == name).Bands();

    /// <summary>The preset these bands match exactly (ids aside), or <see cref="CustomName"/>.</summary>
    public string PresetNameFor(IReadOnlyList<EqBand> bands) => _presets.FirstOrDefault(p => SameSound(p.Bands(), bands)).Name ?? CustomName;

    private static bool SameSound(IReadOnlyList<EqBand> a, IReadOnlyList<EqBand> b) =>
        a.Count == b.Count && a.Zip(b).All(x => x.First.Type == x.Second.Type && x.First.Freq == x.Second.Freq && x.First.Q == x.Second.Q
            && x.First.Gain == x.Second.Gain && x.First.Enabled == x.Second.Enabled);
}
