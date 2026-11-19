namespace OmniControl.App.Features.Input.Mouse;

/// One layer of one family as it was: its name (null = none given) and its mappings (stored keys and texts).
public sealed record LayerSnapshot(int Layer, string? Name, IReadOnlyDictionary<string, string> Bindings, IReadOnlyDictionary<string, List<string>> Extras);

/// One input family's layers as stored in <see cref="MouseSettings"/>: their names, the "only list used layers" choice and the mappings on
/// each layer. A view over the settings; nothing is kept here.
public sealed class LayerSet
{
    private readonly MouseSettings _settings;

    public InputFamily Family { get; }

    internal LayerSet(MouseSettings settings, InputFamily family)
    {
        _settings = settings;
        Family = family;
    }

    private Dictionary<int, string> Names => Family == InputFamily.Trackpad ? _settings.TrackpadLayerNames ??= new() : _settings.LayerNames;

    /// True: the layer drop-down lists only layer 1, named layers and layers with mappings.
    public bool OnlyUsed
    {
        get => Family == InputFamily.Trackpad ? _settings.TrackpadOnlyUsedLayers : _settings.OnlyUsedLayers;
        set
        {
            if (Family == InputFamily.Trackpad) _settings.TrackpadOnlyUsedLayers = value;
            else _settings.OnlyUsedLayers = value;
        }
    }

    /// The name the user gave <paramref name="layer"/>, or null.
    public string? NameOf(int layer) => Names.GetValueOrDefault(layer);

    /// The trackpad's first layers, always listed and named from the start; clearing a name brings the default back.
    private static readonly string[] TrackpadPresets = { "Trackpad 1", "Trackpad 2", "Trackpad 3" };

    /// The name a layer has until the user gives it one.
    public string DefaultNameOf(int layer) =>
        Family == InputFamily.Trackpad && layer >= 1 && layer <= TrackpadPresets.Length ? TrackpadPresets[layer - 1] : $"Layer {layer}";

    /// True: the layer is one the family lists even when unused (it can be cleared, never taken off the list).
    public bool IsAlwaysListed(int layer) => layer == 1 || (Family == InputFamily.Trackpad && layer <= TrackpadPresets.Length);

    /// Every layer's name as shown, each different from the others (a repeated name gets its layer number added).
    public IReadOnlyDictionary<int, string> Labels()
    {
        var own = Enumerable.Range(1, MouseSettings.Layers)
            .ToDictionary(n => n, n => NameOf(n) is { } name && name.Trim().Length > 0 ? name.Trim() : DefaultNameOf(n));
        return own.ToDictionary(p => p.Key, p => own.Count(o => o.Value == p.Value) > 1 ? $"{p.Value} ({p.Key})" : p.Value);
    }

    /// How many inputs of this family have an action on <paramref name="layer"/>.
    public int MappingCount(int layer) => KeysOn(layer).Count();

    /// The layers the drop-down offers: all of them, or (with <see cref="OnlyUsed"/>) layer 1, named and mapped ones, and any layer in <paramref name="alwaysShown"/>.
    public IReadOnlyList<int> Listed(params int[] alwaysShown) =>
        Enumerable.Range(1, MouseSettings.Layers)
            .Where(n => !OnlyUsed || IsAlwaysListed(n) || Names.ContainsKey(n) || MappingCount(n) > 0 || alwaysShown.Contains(n))
            .ToList();

    public void SetName(int layer, string name)
    {
        if (name.Trim().Length == 0) Names.Remove(layer);
        else Names[layer] = name.Trim();
    }

    /// Removes this family's mappings on <paramref name="layer"/>; the other family's stay.
    public void Clear(int layer)
    {
        foreach (var key in KeysOn(layer).ToList())
        {
            _settings.Bindings.Remove(key);
            _settings.ExtraActions.Remove(key);
        }
    }

    /// The layer's name and this family's mappings on it, as they are now (what an undone clear puts back).
    public LayerSnapshot Snapshot(int layer) =>
        new(layer, NameOf(layer),
            KeysOn(layer).ToDictionary(k => k, k => _settings.Bindings[k]),
            KeysOn(layer).Where(_settings.ExtraActions.ContainsKey).ToDictionary(k => k, k => _settings.ExtraActions[k].ToList()));

    /// Puts a layer back as <paramref name="snapshot"/> had it: its name and exactly those mappings.
    public void Restore(LayerSnapshot snapshot)
    {
        Clear(snapshot.Layer);
        SetName(snapshot.Layer, snapshot.Name ?? string.Empty);
        foreach (var (key, spec) in snapshot.Bindings) _settings.Bindings[key] = spec;
        foreach (var (key, extras) in snapshot.Extras) _settings.ExtraActions[key] = extras.ToList();
    }

    private IEnumerable<string> KeysOn(int layer) =>
        _settings.Bindings.Keys.Where(key => MouseSettings.ParseKey(key) is { } parsed && parsed.Layer == layer && parsed.Button.FamilyOf() == Family);
}
