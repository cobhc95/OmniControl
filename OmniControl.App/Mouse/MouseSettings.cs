using System.Text.Json.Serialization;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Mouse;

/// <summary>Mouse remapping preferences, stored as JSON in %APPDATA%\OmniControl\mouse.json.</summary>
public sealed class MouseSettings
{
    public const int Layers = 10;

    public bool Enabled { get; set; } = true;

    /// <summary>True: the touchpad is read for trackpad gestures (off: no touchpad input is read at all).</summary>
    public bool TrackpadEnabled { get; set; } = true;

    /// <summary>
    /// The action per layer and input, keyed "layer.Button" (for example "1.Middle", "1.ThreeFingerUp" for a trackpad gesture). A missing
    /// key leaves the input as it is.
    /// </summary>
    public Dictionary<string, string> Bindings { get; set; } = new();

    /// <summary>Names the user gave to layers (layer number to name); a layer without one is called "Layer N".</summary>
    public Dictionary<int, string> LayerNames { get; set; } = new();

    /// <summary>True: the Layer drop-down lists only layer 1, named layers and layers with mappings.</summary>
    public bool OnlyUsedLayers { get; set; } = true;

    /// <summary>Only read from files saved while the program still had profiles; <see cref="Load"/> keeps the default profile's mappings and drops the rest.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LegacyProfile>? Profiles { get; set; }

    public sealed class LegacyProfile
    {
        public Dictionary<string, string> Bindings { get; set; } = new();
    }

    private const string FileName = "mouse.json";

    public static MouseSettings Load()
    {
        var settings = AppDataStore.Load<MouseSettings>(FileName) ?? new();
        if (settings.Profiles is { Count: > 0 } && settings.Bindings.Count == 0) settings.Bindings = settings.Profiles[0].Bindings;
        settings.Profiles = null;
        return settings;
    }

    public void Save() => AppDataStore.Save(FileName, this);

    public static string KeyOf(int layer, MouseButton button) => $"{layer}.{button}";

    public string? Get(int layer, MouseButton button) => Bindings.GetValueOrDefault(KeyOf(layer, button));

    /// <summary>Every layer's name as shown, each different from the others (a repeated name gets its layer number added).</summary>
    public IReadOnlyDictionary<int, string> LayerLabels()
    {
        var own = Enumerable.Range(1, Layers).ToDictionary(n => n, n => LayerNames.TryGetValue(n, out var name) && name.Trim().Length > 0 ? name.Trim() : $"Layer {n}");
        return own.ToDictionary(p => p.Key, p => own.Count(o => o.Value == p.Value) > 1 ? $"{p.Value} ({p.Key})" : p.Value);
    }

    /// <summary>How many buttons and trackpad gestures have an action on <paramref name="layer"/>.</summary>
    public int MappingCount(int layer) => Bindings.Keys.Count(k => k.StartsWith($"{layer}.", StringComparison.Ordinal));

    /// <summary>The layers the drop-down offers: all of them, or (with <see cref="OnlyUsedLayers"/>) layer 1, named and mapped ones, and any layer in <paramref name="alwaysShown"/>.</summary>
    public IReadOnlyList<int> ListedLayers(params int[] alwaysShown) =>
        Enumerable.Range(1, Layers)
            .Where(n => !OnlyUsedLayers || n == 1 || LayerNames.ContainsKey(n) || MappingCount(n) > 0 || alwaysShown.Contains(n))
            .ToList();

    public void SetName(int layer, string name)
    {
        if (name.Trim().Length == 0) LayerNames.Remove(layer);
        else LayerNames[layer] = name.Trim();
    }

    public void ClearLayer(int layer)
    {
        foreach (var key in Bindings.Keys.Where(k => k.StartsWith($"{layer}.", StringComparison.Ordinal)).ToList()) Bindings.Remove(key);
    }

    public void Set(int layer, MouseButton button, string spec)
    {
        if (spec == MouseActionCatalog.Default) Bindings.Remove(KeyOf(layer, button));
        else Bindings[KeyOf(layer, button)] = spec;
    }
}
