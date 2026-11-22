namespace OmniControl.App.Shared.Ui.Pages;

/// A card whose value is a row of choice pills (<see cref="ChoiceChips"/>): one of a few quick values, or several parts that are each on
/// or off (the gadget's quick refresh choices, the weather's Shows), with an optional muted note under them. Each chip's own command
/// does the choosing; the owner sets the chips' <see cref="ChipItem.IsSelected"/>.
public sealed class ChoiceChipsItem : IEditorItem, IHalfWidth
{
    public ChoiceChipsItem(string label, IReadOnlyList<ChipItem> chips) => (Label, Chips) = (label, chips);

    /// A chips card made from its search card: the label and muted line are the card's.
    public ChoiceChipsItem(SearchCard card, IReadOnlyList<ChipItem> chips) : this(card.Label, chips) => Note = card.Note;

    public IEnumerable<string> SearchTexts => Chips.Select(c => c.Name).Append(Note).Where(t => !string.IsNullOrEmpty(t))!;

    public string Label { get; }

    public IReadOnlyList<ChipItem> Chips { get; }

    /// A chip menu after the pills, for a part of the choice that has more than on and off (the gadget's temperature sign); null: none.
    public ChoiceItem? Menu { get; init; }

    public bool HasMenu => Menu is not null;

    /// A muted line under the chips (what the choice costs); null: none.
    public string? Note { get; init; }

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public bool Half { get; init; }
}

