namespace OmniControl.App.Shared.Ui.Pages;

/// A card with one button that does something once (turn the screen off, reset a picture, remove a folder), or a few (Backup and
/// restore: Export..., Import..., Wipe all...; the later ones are <see cref="Extra"/>). Not a setting, so it has no value; its
/// description may change (what the last press did).
public sealed partial class ButtonItem : ObservableObject, IEditorItem, IHalfWidth
{
    public string Label { get; }

    /// True: the card takes half the width and may share a row with the next half-width card; the description is then only a tooltip.
    public bool Half { get; init; }

    /// The text on the button (it may change with the state: "Turn on", "Turn off").
    [ObservableProperty] private string _buttonText;

    /// A short line beside the label saying what the button does or how to undo it (or what it last did); also its tooltip.
    [ObservableProperty] private string _description;

    public IRelayCommand Command { get; }

    public Availability Availability { get; } = new();

    /// More buttons after the first, in order (full-width card only).
    public IReadOnlyList<CardAction> Extra { get; init; } = Array.Empty<CardAction>();

    /// A button card made from its search card: the label is the card's, and its muted line is the description until the owner changes it.
    public ButtonItem(SearchCard card, string buttonText, Action run) : this(card.Label, buttonText, card.Note ?? string.Empty, run)
    {
    }

    public IEnumerable<string> SearchTexts => Extra.Select(a => a.Text).Append(ButtonText).Append(Description).Where(t => !string.IsNullOrEmpty(t));

    public ButtonItem(string label, string buttonText, string description, Action run)
    {
        Label = label;
        _buttonText = buttonText;
        _description = description;
        Command = new RelayCommand(run);
    }
}

/// One more button on a <see cref="ButtonItem"/>: its words, what it does, and whether it destroys something (a red pill).
public sealed class CardAction
{
    public CardAction(string text, Action run, bool danger = false)
    {
        Text = text;
        Command = new RelayCommand(run);
        Danger = danger;
    }

    public string Text { get; }
    public IRelayCommand Command { get; }
    public bool Danger { get; }
}
