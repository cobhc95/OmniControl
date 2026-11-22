using System.Globalization;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Input.Mouse;

/// One input family's layers (the mouse's or the trackpad's) as a preset group of the preset manager: the layers its drop-down lists,
/// the one in force marked "in use", how many mappings each holds. Applying switches to the layer; deleting clears its mappings and
/// takes its name away (a layer that is always listed is only cleared); adding names the next layer that is not listed. Layers keep
/// their numbers, because "Hold for layer N", "Switch to layer N" and the custom rules refer to them, so they are not reordered.
/// Everything goes through <see cref="ILayerControl"/>, which saves and applies.
public sealed class LayerPresets : PresetProvider, IDisposable
{
    /// The preset manager's ids of the two layer groups (also part of their shortcut actions' ids: never rename).
    public const string MouseGroupId = "mouse.buttons", TrackpadGroupId = "mouse.trackpad";

    /// The layers' "Only named or used layers" switch, said once for the manager and for search.
    public const string OnlyUsedLabel = "Only named or used layers", OnlyUsedTip = "The layer drop-down and this list leave out layers with no name and no mappings";

    private const string AllListed = "Every layer is listed already.";
    private const string KeepNumbers = "Layers keep their numbers: \"Hold for layer N\" and the custom rules refer to them.";

    private readonly ILayerControl _mouse;
    private readonly InputFamily _family;

    public LayerPresets(ILayerControl mouse, InputFamily family)
    {
        (_mouse, _family) = (mouse, family);
        Words = new PresetWords("Switch to", "Switch to layer", "+ Add a layer", "Names the next unused layer so it is listed");
        Switch = new PresetSwitch(OnlyUsedLabel, OnlyUsedTip,
            () => Layers.OnlyUsed, on => _mouse.SetOnlyUsedLayers(_family, on));
        mouse.LayerChanged += OnLayerChanged;
        mouse.LayersChanged += OnLayersChanged;
    }

    private LayerSet Layers => _mouse.Settings.LayersOf(_family);

    public override PresetWords Words { get; }

    public override PresetSwitch? Switch { get; }

    public override IReadOnlyList<PresetEntry> Entries
    {
        get
        {
            var layers = Layers;
            var labels = layers.Labels();
            return layers.Listed(_mouse.LayerOf(_family)).Select(n => EntryOf(layers, labels, n)).ToList();
        }
    }

    public override PresetEntry? Find(string id)
    {
        var layers = Layers;
        return LayerOf(id) is { } layer ? EntryOf(layers, layers.Labels(), layer) : null;
    }

    public override string? AddUnavailableReason => FreeLayer() is null ? AllListed : null;

    private PresetEntry EntryOf(LayerSet layers, IReadOnlyDictionary<int, string> labels, int layer)
    {
        var count = layers.MappingCount(layer);
        var always = layers.IsAlwaysListed(layer);
        return new PresetEntry(layer.ToString(CultureInfo.InvariantCulture), labels[layer])
        {
            InUse = _mouse.LayerOf(_family) == layer,
            Tags = new[] { new TagItem(count == 0 ? "no mappings" : count == 1 ? "1 mapping" : $"{count} mappings") },
            CanMove = false,
            ClearsOnly = always,
            CanDelete = !always || count > 0,
        };
    }

    private static int? LayerOf(string id) =>
        int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var layer) && layer >= 1 && layer <= MouseSettings.Layers ? layer : null;

    private int? FreeLayer()
    {
        var listed = Layers.Listed(_mouse.LayerOf(_family));
        return Enumerable.Range(1, MouseSettings.Layers).Where(n => !listed.Contains(n)).Cast<int?>().FirstOrDefault();
    }

    public override string? Apply(string id)
    {
        if (LayerOf(id) is { } layer) _mouse.SetLayer(_family, layer);
        return null;
    }

    public override PresetChange Add()
    {
        if (FreeLayer() is not { } layer) return PresetChange.Refused(AllListed);
        _mouse.RenameLayer(_family, layer, Layers.DefaultNameOf(layer));
        return PresetChange.Made(layer.ToString(CultureInfo.InvariantCulture));
    }

    /// An empty name gives the layer its default name back.
    public override PresetChange Rename(string id, string name)
    {
        if (LayerOf(id) is not { } layer) return PresetChange.Done;
        if (name.Trim().Length > 0 && PresetRules.Error(name) is { } problem) return PresetChange.Refused(problem);
        _mouse.RenameLayer(_family, layer, name);
        return PresetChange.Done;
    }

    public override PresetChange Move(string id, int index) => PresetChange.Refused(KeepNumbers);

    public override Action? Delete(string id)
    {
        if (LayerOf(id) is not { } layer) return null;
        var layers = Layers;
        var before = layers.Snapshot(layer);
        var always = layers.IsAlwaysListed(layer);
        if (always && before.Bindings.Count == 0) return null;
        _mouse.ClearLayer(_family, layer);
        if (!always) _mouse.RenameLayer(_family, layer, string.Empty);
        return () => _mouse.RestoreLayer(_family, before);
    }

    public override event Action? Changed;

    private void OnLayerChanged(InputFamily family, int layer)
    {
        if (family == _family) Changed?.Invoke();
    }

    private void OnLayersChanged() => Changed?.Invoke();

    public void Dispose()
    {
        _mouse.LayerChanged -= OnLayerChanged;
        _mouse.LayersChanged -= OnLayersChanged;
    }
}
