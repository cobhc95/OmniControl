namespace OmniControl.App.Audio;

/// <summary>
/// Which effect kinds have presets and what their built-in ones are, read from the catalog entries (<see cref="FxKind.Eq"/> and
/// <see cref="FxKind.Presets"/>): the one list the preset drop-down and the preset manager both use. No UI.
/// </summary>
public static class PresetCatalog
{
    /// <summary>The kinds whose effect offers presets (the three equalisers and the compressor).</summary>
    public static IReadOnlyList<FxKind> Kinds => FxCatalog.All.Where(HasPresets).ToList();

    public static bool HasPresets(FxKind kind) => kind.Eq is not null || kind.Presets is { Count: > 0 };

    /// <summary>The built-in presets of a kind, in their order.</summary>
    public static IReadOnlyList<(string Name, PresetContent Content)> BuiltIns(FxKind kind)
    {
        if (kind.Eq is { } layout)
            return layout.BuiltInNames.Select(n => (n, new PresetContent { Bands = layout.FromPreset(n) })).ToList();
        return (kind.Presets ?? Array.Empty<FxPreset>()).Select(p => (p.Name, new PresetContent { Values = p.Values.ToList() })).ToList();
    }

    /// <summary>
    /// The content made fit for this kind (bands rebuilt from the kind's definition, values counted and held inside their ranges), or null
    /// when it cannot be a preset of it: a graphic equaliser's bands that are not its own count, or a value list of the wrong length.
    /// </summary>
    public static PresetContent? Fit(FxKind kind, PresetContent content)
    {
        if (kind.Eq is { } layout)
        {
            if (content.Bands is not { Count: > 0 } bands || bands.Count > Eq.MaxBands) return null;
            if (layout.Graphic is { } fixedBands && bands.Count != fixedBands.Centres.Count) return null;
            return new PresetContent { Bands = layout.Upgrade(bands.Select(b => b.Copy()).ToList()) };
        }
        if (content.Values is not { } values || values.Count != kind.Params.Count || values.Any(v => !double.IsFinite(v))) return null;
        return new PresetContent { Values = values.Select((v, i) => Math.Clamp(v, kind.Params[i].Min, kind.Params[i].Max)).ToList() };
    }
}
