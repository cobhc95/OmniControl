using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages;

/// <summary>An on/off setting. Calls the apply action when changed.</summary>
public sealed partial class ToggleItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Action<bool> _apply;

    public string Label { get; }

    /// <summary>An icon id of the icon library drawn before the label (Other PCs' mirrored cards); null: no icon, the card looks as without.</summary>
    public string? Icon { get; init; }

    /// <summary>A fact about the setting the label does not say (a limit), shown as the card's tooltip; null: none.</summary>
    public string? Tip { get; init; }

    /// <summary>A muted second line under the label of a full-width card (what the setting does or costs); null: none.</summary>
    public string? Note { get; init; }

    public bool HasNote => !string.IsNullOrEmpty(Note);

    /// <summary>True: the card takes half the width and may share a row with the next half-width card.</summary>
    public bool Half { get; init; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateWords))] private bool _isOn;

    /// <summary>What the switch acts on ("Default microphone: Blue Yeti"), put before On/Off in a half-width card's tooltip; empty: none. May change while shown.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateWords))] private string _subject = string.Empty;

    /// <summary>"On" or "Off": the tooltip of a half-width card's switch, which has no words of its own.</summary>
    public string StateWords => Subject.Length > 0 ? $"{Subject} · {(IsOn ? "On" : "Off")}" : IsOn ? "On" : "Off";

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
