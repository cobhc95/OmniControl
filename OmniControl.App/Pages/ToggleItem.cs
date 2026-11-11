using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages;

/// <summary>An on/off setting. Calls the apply action when changed.</summary>
public sealed partial class ToggleItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Action<bool> _apply;

    public string Label { get; }

    /// <summary>True: the card takes half the width and may share a row with the next half-width card.</summary>
    public bool Half { get; init; }

    [ObservableProperty] private bool _isOn;

    public ToggleItem(string label, bool isOn, Action<bool> apply)
    {
        Label = label;
        _isOn = isOn;
        _apply = apply;
    }

    partial void OnIsOnChanged(bool value) => _apply(value);
}
