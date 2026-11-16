namespace OmniControl.App.Mouse;

/// <summary>
/// What the layers' preset group (<see cref="LayerPresets"/>) needs of the mouse service: the stored layers, the layer in force, and the
/// changes that save and apply. <see cref="MouseService"/> is the one; the tests give a fake that holds settings in memory.
/// </summary>
public interface ILayerControl
{
    MouseSettings Settings { get; }

    int LayerOf(InputFamily family);

    void SetLayer(InputFamily family, int layer);

    void RenameLayer(InputFamily family, int layer, string name);

    /// <summary>Removes the family's mappings on the layer (its name stays).</summary>
    void ClearLayer(InputFamily family, int layer);

    /// <summary>Puts a layer back as it was (an undone clear or delete).</summary>
    void RestoreLayer(InputFamily family, LayerSnapshot snapshot);

    void SetOnlyUsedLayers(InputFamily family, bool on);

    /// <summary>A family's layer in force changed (UI thread).</summary>
    event Action<InputFamily, int>? LayerChanged;

    /// <summary>A layer was renamed, cleared or restored, or the list of layers shown changed (UI thread).</summary>
    event Action? LayersChanged;
}
