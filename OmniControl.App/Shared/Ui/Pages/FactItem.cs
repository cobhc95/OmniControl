namespace OmniControl.App.Shared.Ui.Pages;

/// A card that states a fact instead of holding a setting: its name, the value (plain), a muted note, tags (<see cref="TagItem"/>) and
/// at most one pill that does something about it ("Open on GitHub"). Everything but the name may change while the page is open.
public sealed partial class FactItem : ObservableObject, IEditorItem, IHalfWidth
{
    public string Label { get; }

    /// An icon id of the icon library drawn before the label (Other PCs' mirrored cards); null: none.
    public string? Icon { get; init; }

    /// True: the card takes half the width and may share a row with the next half-width card.
    public bool Half { get; init; }

    /// True: the card is laid out as a media line: the icon, the value (semi-bold) over the muted note, then, at the right of the same row, the <see cref="Pills"/> as icon-only pills with tooltips (Now playing); the label is not shown.
    public bool Stacked { get; init; }

    public bool IsPlain => !Stacked;

    /// Pills at the right of a stacked card's row (icon-only, name in the tooltip); empty: none.
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPills))] private IReadOnlyList<ChipItem> _pills = Array.Empty<ChipItem>();

    public bool HasPills => Pills.Count > 0;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasValue))] private string _value = string.Empty;

    /// A muted line after the value (when it was checked, what it means); wraps to a second line before the card grows.
    [ObservableProperty] private string _note = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasTags))] private IReadOnlyList<TagItem> _tags = Array.Empty<TagItem>();

    /// The words of the pill; empty: no pill.
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasAction), nameof(HasPillAction), nameof(HasLinkAction))] private string _actionText = string.Empty;

    public bool HasValue => Value.Length > 0;

    public bool HasTags => Tags.Count > 0;

    public bool HasAction => ActionText.Length > 0;

    /// True: the action is words inside the line (<c>Button.link</c>), not the accent pill (the device folder editor's "Change in Mixer").
    public bool ActionIsLink { get; init; }

    public bool HasPillAction => HasAction && !ActionIsLink;
    public bool HasLinkAction => HasAction && ActionIsLink;

    public IRelayCommand Command { get; }

    public FactItem(string label, Action? run = null)
    {
        Label = label;
        Command = new RelayCommand(() => run?.Invoke());
    }

    /// A fact card made from its search card: the label is the card's, and its muted line the note until the owner changes it.
    public FactItem(SearchCard card, Action? run = null) : this(card.Label, run) => _note = card.Note ?? string.Empty;

    public IEnumerable<string> SearchTexts => new[] { Value, Note, ActionText }.Where(t => !string.IsNullOrEmpty(t));
}
