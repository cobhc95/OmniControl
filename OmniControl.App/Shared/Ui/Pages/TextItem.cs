namespace OmniControl.App.Shared.Ui.Pages;

/// A card with a one-line text setting (a name). Calls the apply action when the text changes.
public sealed partial class TextItem : ObservableObject, IEditorItem
{
    private readonly Action<string> _apply;

    public string Label { get; }

    /// The grey words shown while the box is empty; null: none.
    public string? Hint { get; init; }

    [ObservableProperty] private string _text;

    public TextItem(string label, string text, Action<string> apply) => (Label, _text, _apply) = (label, text, apply);

    /// A text card made from its search card: the label is the card's, and its muted line the grey hint of the empty box.
    public TextItem(SearchCard card, string text, Action<string> apply) : this(card.Label, text, apply) => Hint = card.Note;

    public IEnumerable<string> SearchTexts => Hint is { Length: > 0 } hint ? new[] { hint } : Array.Empty<string>();

    partial void OnTextChanged(string value) => _apply(value);
}
