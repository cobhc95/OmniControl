namespace OmniControl.App.Shared.Ui.Pages;

/// THE one definition of what a page offers to search: its <see cref="SearchCard"/>s, each with the live editor that shows it (null while the
/// card is not on the page now, or while the page is not made). A page writes one method that fills a builder from its services and, when it
/// has one, its instance; its word source (<c>FeaturePage.Words</c>) is <see cref="Words"/> of the builder made without an instance, and its
/// results are <see cref="Entries"/> of the builder made with it, so the two can never differ.
public sealed class SearchBuilder
{
    private readonly List<(SearchCard Card, object? Editor)> _cards = new();

    /// A card, with its live editor when there is one (no editor: the words only).
    public SearchBuilder Add(SearchCard card, object? editor = null)
    {
        _cards.Add((card, editor));
        return this;
    }

    /// A card per item of a changing list (features, folders, devices): each with the editor the item has now, if any.
    public SearchBuilder AddEach<T>(IEnumerable<T> items, Func<T, SearchCard> card, Func<T, object?>? editor = null)
    {
        foreach (var item in items) Add(card(item), editor?.Invoke(item));
        return this;
    }

    /// The words of every card, for the page's word source.
    public IEnumerable<string> Words => _cards.Select(c => c.Card.Words).ToList();

    /// The results of the cards that have an editor.
    public IEnumerable<SearchEntry> Entries(string category) =>
        _cards.Where(c => c.Editor is not null).Select(c => SearchEntry.Of(category, c.Card, c.Editor!)).ToList();
}
