namespace OmniControl.App.Shared.Ui.Pages;

/// One editable setting shown in the global search results. Pages make them through their <see cref="SearchBuilder"/>.
public sealed class SearchEntry
{
    public required string Title { get; init; }

    /// The page it is on (what the category filter matches).
    public required string Category { get; init; }

    /// The part of the page it belongs to (a hotkey's heading, "Hardware"...), or null.
    public string? Group { get; init; }

    /// The other PC the result runs on; its mark replaces the heading under the title.
    public Services.RemotePcLabel? RemotePc { get; init; }
    public bool ShowsHeading => RemotePc is null;

    /// What the result shows under its title: the category, then the group.
    public string Heading => Services.Heading.Join(Category, Group);

    /// The live control: any <see cref="IEditorItem"/> that App.axaml has a card template for.
    public required object Editor { get; init; }

    public string Label => (Editor as IEditorItem)?.Label ?? string.Empty;
    public string Keywords { get; init; } = string.Empty;

    /// The texts inside the card that the title does not say: its choices, menu items, tooltip and muted line. Searched like the title.
    public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();

    /// What the search matches (<see cref="TextMatch"/>).
    public IEnumerable<string?> SearchFields => new[] { Title, Heading, Label, Keywords }.Concat(Details);

    /// A result for <paramref name="editor"/> made from its card: the card's words and the editor's own live ones (its options now, its menu's rows).
    public static SearchEntry Of(string category, SearchCard card, object editor) => new()
    {
        Title = card.Title ?? card.Label,
        Category = category,
        Group = card.Group,
        Editor = editor,
        Keywords = card.Keywords,
        Details = card.Details.Concat((editor as IEditorItem)?.SearchTexts ?? Array.Empty<string>()).Distinct().ToList(),
    };

    /// The detail text that holds the query's words when the title and heading do not show them (so the result can say where it matched); null when they do.
    public string? FoundIn(IReadOnlyList<string> queryWords)
    {
        if (Services.FuzzyMatch.Hits(Title, queryWords).Count > 0 || Services.FuzzyMatch.Hits(Heading, queryWords).Count > 0) return null;
        return Details.FirstOrDefault(d => Services.FuzzyMatch.Hits(d, queryWords).Count > 0);
    }

    /// One result's words for a page's word source (<c>FeaturePage.Words</c>), read without its page: its title, group, label, keywords and
    /// details (search adds the page's title).
    public static string WordsOf(params string?[] fields) => string.Join('\n', fields.Where(f => !string.IsNullOrEmpty(f)));
}
