namespace OmniControl.App.Features.Input.Mouse;

/// What the layers' preset group (<see cref="LayerPresets"/>) needs of the mouse service: the stored layers, the layer in force, and the
/// changes that save and apply. <see cref="MouseService"/> is the one; the tests give a fake that holds settings in memory.
public interface ILayerControl
{
    MouseSettings Settings { get; }

    int LayerOf(InputFamily family);

    void SetLayer(InputFamily family, int layer);

    void RenameLayer(InputFamily family, int layer, string name);

    /// Removes the family's mappings on the layer (its name stays).
    void ClearLayer(InputFamily family, int layer);

    /// Puts a layer back as it was (an undone clear or delete).
    void RestoreLayer(InputFamily family, LayerSnapshot snapshot);

    void SetOnlyUsedLayers(InputFamily family, bool on);

    /// A family's layer in force changed (UI thread).
    event Action<InputFamily, int>? LayerChanged;

    /// A layer was renamed, cleared or restored, or the list of layers shown changed (UI thread).
    event Action? LayersChanged;
}
