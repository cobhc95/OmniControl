namespace OmniControl.App.Shared.Ui.Pages;

/// A card whose choice is a small menu (the shared chip and <see cref="MenuRowList"/> pop-down) instead of a plain drop-down list: it can hold
/// parts a drop-down cannot, such as a thin line between groups, a muted heading, a way out to a manager, or a list where several rows are
/// ticked (the OSD's Position and Show on). The rows are asked every time the menu opens, and again after <see cref="Refresh"/>, so they
/// follow the settings; each row's own command does the choosing.
public sealed partial class MenuChoiceItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Func<string> _words;
    private readonly Func<IReadOnlyList<MenuRowItem>> _rows;

    public string Label { get; }

    /// True: the card takes half the width and may share a row with the next half-width card.
    public bool Half { get; init; }

    /// A fact about the choice the label does not say, shown as the chip's tooltip; null: none.
    public string? Tip { get; init; }

    /// Told when the menu opens (true) and closes, for rows that are worth reading only while someone looks at them; null: none.
    public Action<bool>? OpenChanged { get; init; }

    /// What the chip says: the choice now.
    [ObservableProperty] private string _text;

    /// <param name="words">The chip's words (the choice now).</param>
    /// <param name="rows">The menu's rows.</param>
    public MenuChoiceItem(string label, Func<string> words, Func<IReadOnlyList<MenuRowItem>> rows)
    {
        (Label, _words, _rows) = (label, words, rows);
        _text = words();
    }

    /// A menu card made from its search card: label and tooltip are the card's.
    public MenuChoiceItem(SearchCard card, Func<string> words, Func<IReadOnlyList<MenuRowItem>> rows) : this(card.Label, words, rows) => Tip = card.Tip;

    /// The chip's words, the tooltip and the menu's rows as they are now (texts, details and tooltips).
    public IEnumerable<string> SearchTexts => Rows.SelectMany(r => new[] { r.Text, r.Detail, r.Tip }).Append(Tip).Append(Text).Where(t => !string.IsNullOrEmpty(t))!;

    /// The menu's rows as they are now.
    public IReadOnlyList<MenuRowItem> Rows => _rows();

    /// Reads the chip's words and the rows again (the setting changed elsewhere, or by one of the rows).
    public void Refresh()
    {
        Text = _words();
        OnPropertyChanged(nameof(Rows));
    }
}
