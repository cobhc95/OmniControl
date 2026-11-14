using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Mouse;

namespace OmniControl.App.Pages.Mouse;

/// <summary>One line of the layer manager: a layer's name (editable), how many of the family's inputs it maps, and what can be done with it.</summary>
public sealed partial class LayerRow : ObservableObject
{
    private readonly MouseService _mouse;
    private readonly InputFamily _family;
    private bool _loading;

    public int Layer { get; }

    /// <summary>What the name box shows when no name was given ("Layer 3").</summary>
    public string DefaultName => _mouse.Settings.LayersOf(_family).DefaultNameOf(Layer);

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _mappings = string.Empty;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _canClear;

    public LayerRow(MouseService mouse, InputFamily family, int layer)
    {
        _mouse = mouse;
        _family = family;
        Layer = layer;
        Refresh();
    }

    /// <summary>Takes the current state from the service.</summary>
    public void Refresh()
    {
        _loading = true;
        var layers = _mouse.Settings.LayersOf(_family);
        var count = layers.MappingCount(Layer);
        var custom = layers.NameOf(Layer) ?? string.Empty;
        if (Name != custom) Name = custom;
        Mappings = count == 0 ? "none" : count == 1 ? "1 mapping" : $"{count} mappings";
        CanClear = count > 0;
        IsActive = _mouse.LayerOf(_family) == Layer;
        _loading = false;
    }

    partial void OnNameChanged(string value)
    {
        if (!_loading) _mouse.RenameLayer(_family, Layer, value);
    }

    [RelayCommand]
    private void SwitchTo() => _mouse.SetLayer(_family, Layer);

    [RelayCommand]
    private void Clear() => _mouse.ClearLayer(_family, Layer);
}

/// <summary>
/// State of the "Manage mouse layers" or "Manage trackpad layers" window (one model, the family chosen by the cogwheel that opened it):
/// every layer with its name and mappings, and the switch that trims the layer list.
/// </summary>
public sealed partial class LayerManagerModel : ObservableObject, IDisposable
{
    private readonly MouseService _mouse;
    private readonly InputFamily _family;
    private bool _loading;

    /// <summary>The window's title.</summary>
    public string Title { get; }

    public ObservableCollection<LayerRow> Rows { get; } = new();

    [ObservableProperty] private bool _onlyUsed;

    public LayerManagerModel(MouseService mouse, InputFamily family)
    {
        _mouse = mouse;
        _family = family;
        Title = $"Manage {family.Noun()} layers";
        for (var layer = 1; layer <= MouseSettings.Layers; layer++) Rows.Add(new LayerRow(mouse, family, layer));
        _loading = true;
        OnlyUsed = mouse.Settings.LayersOf(family).OnlyUsed;
        _loading = false;
        mouse.LayerChanged += OnLayerChanged;
        mouse.LayersChanged += Refresh;
    }

    private void OnLayerChanged(InputFamily family, int layer)
    {
        if (family == _family) Refresh();
    }

    private void Refresh()
    {
        foreach (var row in Rows) row.Refresh();
    }

    partial void OnOnlyUsedChanged(bool value)
    {
        if (!_loading) _mouse.SetOnlyUsedLayers(_family, value);
    }

    public void Dispose()
    {
        _mouse.LayerChanged -= OnLayerChanged;
        _mouse.LayersChanged -= Refresh;
    }
}
