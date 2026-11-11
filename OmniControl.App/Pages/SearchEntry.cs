namespace OmniControl.App.Pages;

/// <summary>One editable setting shown in the global search results.</summary>
public sealed class SearchEntry
{
    public required string Title { get; init; }
    public required string Category { get; init; }

    /// <summary>The live control: any <see cref="IEditorItem"/> that App.axaml has a card template for.</summary>
    public required object Editor { get; init; }

    public string Label => (Editor as IEditorItem)?.Label ?? string.Empty;
    public string Keywords { get; init; } = string.Empty;

    public bool Matches(string query) =>
        Title.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Category.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Label.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Keywords.Contains(query, StringComparison.OrdinalIgnoreCase);
}
