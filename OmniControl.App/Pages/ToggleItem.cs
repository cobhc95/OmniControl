using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages;

/// <summary>An on/off setting. Calls the apply action when changed.</summary>
public sealed partial class ToggleItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Action<bool> _apply;

    public string Label { get; }

    /// <summary>A fact about the setting the label does not say (a limit), shown as the card's tooltip; null: none.</summary>
    public string? Tip { get; init; }

    /// <summary>True: the card takes half the width and may share a row with the next half-width card.</summary>
    public bool Half { get; init; }

    [ObservableProperty] private bool _isOn;

    public ToggleItem(string label, bool isOn, Action<bool> apply)
    {
        Label = label;
        _isOn = isOn;
        _apply = apply;
    }

    private bool _loading;

    /// <summary>Shows a state read from elsewhere (what Windows has now) without applying it.</summary>
    public void Load(bool isOn)
    {
        _loading = true;
        IsOn = isOn;
        _loading = false;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_loading) _apply(value);
    }
}
