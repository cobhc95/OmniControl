using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Mouse;

namespace OmniControl.App.Pages.Mouse;

/// <summary>One line of the layer manager: a layer's name (editable), how many buttons and gestures it maps, and what can be done with it.</summary>
public sealed partial class LayerRow : ObservableObject
{
    private readonly MouseService _mouse;
    private bool _loading;

    public int Layer { get; }

    /// <summary>What the name box shows when no name was given ("Layer 3").</summary>
    public string DefaultName => $"Layer {Layer}";

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _mappings = string.Empty;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _canClear;

    public LayerRow(MouseService mouse, int layer)
    {
        _mouse = mouse;
        Layer = layer;
        Refresh();
    }

    /// <summary>Takes the current state from the service.</summary>
    public void Refresh()
    {
        _loading = true;
        var count = _mouse.Settings.MappingCount(Layer);
        _mouse.Settings.LayerNames.TryGetValue(Layer, out var custom);
        if (Name != (custom ?? string.Empty)) Name = custom ?? string.Empty;
        Mappings = count == 0 ? "none" : count == 1 ? "1 mapping" : $"{count} mappings";
        CanClear = count > 0;
        IsActive = _mouse.Layer == Layer;
        _loading = false;
    }

    partial void OnNameChanged(string value)
    {
        if (!_loading) _mouse.RenameLayer(Layer, value);
    }

    [RelayCommand]
    private void SwitchTo() => _mouse.SetLayer(Layer);

    [RelayCommand]
    private void Clear() => _mouse.ClearLayer(Layer);
}

/// <summary>State of the "Manage mouse layers" window: every layer with its name and mappings, and the switch that trims the layer list.</summary>
public sealed partial class LayerManagerModel : ObservableObject, IDisposable
{
    private readonly MouseService _mouse;
    private bool _loading;

    public ObservableCollection<LayerRow> Rows { get; } = new();

    [ObservableProperty] private bool _onlyUsed;

    public LayerManagerModel(MouseService mouse)
    {
        _mouse = mouse;
        for (var layer = 1; layer <= MouseSettings.Layers; layer++) Rows.Add(new LayerRow(mouse, layer));
        _loading = true;
        OnlyUsed = mouse.Settings.OnlyUsedLayers;
        _loading = false;
        mouse.LayerChanged += OnLayerChanged;
        mouse.LayersChanged += Refresh;
    }

    private void OnLayerChanged(int layer) => Refresh();

    private void Refresh()
    {
        foreach (var row in Rows) row.Refresh();
    }

    partial void OnOnlyUsedChanged(bool value)
    {
        if (!_loading) _mouse.SetOnlyUsedLayers(value);
    }

    public void Dispose()
    {
        _mouse.LayerChanged -= OnLayerChanged;
        _mouse.LayersChanged -= Refresh;
    }
}
