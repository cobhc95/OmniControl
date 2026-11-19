namespace OmniControl.App.Features.Volume.Audio;

/// How one equaliser kind makes, names and loads its bands; it sits on the kind's catalog entry (<see cref="FxKind.Eq"/>), so the
/// engine, the APO writer and the editor treat every equaliser alike. The Parametric's is <see cref="Eq.Layout"/>; a graphic
/// equaliser's carries its fixed bands (<see cref="Graphic"/>, <see cref="GraphicEq"/>). No UI.
public sealed class EqLayout
{
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

    /// The names of the built-in presets, the first being the default (the user's own presets are in <see cref="UserPresetStore"/>).
    public IReadOnlyList<string> BuiltInNames => _presets.Select(p => p.Name).ToList();

    public List<EqBand> Default() => _default();
    public List<EqBand> Upgrade(List<EqBand>? bands) => _upgrade(bands);
    public List<EqBand> FromPreset(string name) => _presets.First(p => p.Name == name).Bands();
}
