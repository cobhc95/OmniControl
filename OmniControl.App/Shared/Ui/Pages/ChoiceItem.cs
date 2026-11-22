namespace OmniControl.App.Shared.Ui.Pages;

/// A pick-one setting (theme, input source...). Calls the apply action when changed.
public sealed partial class ChoiceItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Action<string> _apply;

    public string Label { get; }

    /// An icon id of the icon library drawn before the label (Other PCs' mirrored cards); null: no icon, the card looks as without.
    public string? Icon { get; init; }

    /// True: the card takes half the width and may share a row with the next half-width card.
    public bool Half { get; init; }

    /// The entries to choose from; may be replaced while the card is shown (the Mouse and Trackpad page's layer list).
    [ObservableProperty] private IReadOnlyList<string> _options;

    [ObservableProperty] private string _selected;

    /// Greyed out, with a reason, while the choice cannot be made (a monitor that does not list its inputs).
    public Availability Availability { get; } = new();

    /// For a preset drop-down: what its "Manage presets…" row does (opens the preset manager at the group); null for a plain choice.
    public Action? Manage { get; init; }

    /// True: a chip menu draws a thin line under the first option, which stands apart from the rest ("All displays" before each display).
    public bool FirstApart { get; init; }

    /// A fact about the setting the label does not say, shown as the card's tooltip; null: none.
    public string? Tip { get; init; }

    /// The card's tooltip: why it is unavailable, else <see cref="Tip"/>.
    public string? Hint => Availability.Reason ?? Tip;

    private bool _loading;

    public ChoiceItem(string label, IReadOnlyList<string> options, string selected, Action<string> apply)
    {
        Label = label;
        Options = options;
        _selected = selected;
        _apply = apply;
        Availability.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Hint));
    }

    /// A choice made from its search card: label and tooltip are the card's, and its options are the card's choices.
    public ChoiceItem(SearchCard card, string selected, Action<string> apply) : this(card.Label, card.Choices, selected, apply) => Tip = card.Tip;

    public IEnumerable<string> SearchTexts => Options.Append(Tip).Where(t => !string.IsNullOrEmpty(t))!;

    /// Shows a list and choice read from elsewhere without applying it.
    public void Load(IReadOnlyList<string> options, string selected)
    {
        _loading = true;
        Options = options;
        Selected = selected;
        _loading = false;
    }

    partial void OnSelectedChanged(string value)
    {
        if (!_loading) _apply(value);
    }
}
