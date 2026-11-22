namespace OmniControl.App.Shared.Ui.Pages;

/// An on/off setting. Calls the apply action when changed.
public sealed partial class ToggleItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Action<bool> _apply;

    public string Label { get; }

    /// An icon id of the icon library drawn before the label (Other PCs' mirrored cards); null: no icon, the card looks as without.
    public string? Icon { get; init; }

    /// A fact about the setting the label does not say (a limit), shown as the card's tooltip; null: none.
    public string? Tip { get; init; }

    /// A muted line under the label (what the setting does or costs); null: none. May change while shown.
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNote))] private string? _note;

    public bool HasNote => !string.IsNullOrEmpty(Note);

    /// The label and note drawn greyed (<c>.unavailable</c>), the switch still usable: what it switches does not run now (App settings' Features).
    [ObservableProperty] private bool _dimmed;

    /// The switch greyed and not usable, and left out of the group's "all on/off": there is nothing to switch (a feature Setup did not install).
    [ObservableProperty] private bool _unavailable;

    /// True: the card takes half the width and may share a row with the next half-width card.
    public bool Half { get; init; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateWords))] private bool _isOn;

    /// What the switch acts on ("Default microphone: Blue Yeti"), put before On/Off in a half-width card's tooltip; empty: none. May change while shown.
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StateWords))] private string _subject = string.Empty;

    /// "On" or "Off": the tooltip of a half-width card's switch, which has no words of its own.
    public string StateWords => Subject.Length > 0 ? $"{Subject} · {(IsOn ? "On" : "Off")}" : IsOn ? "On" : "Off";

    public ToggleItem(string label, bool isOn, Action<bool> apply)
    {
        Label = label;
        _isOn = isOn;
        _apply = apply;
    }

    private bool _loading;

    /// Shows a state read from elsewhere (what Windows has now) without applying it.
    public void Load(bool isOn)
    {
        _loading = true;
        IsOn = isOn;
        _loading = false;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (_loading) return;
        if (Unavailable) Load(!value);
        else _apply(value);
    }
}
