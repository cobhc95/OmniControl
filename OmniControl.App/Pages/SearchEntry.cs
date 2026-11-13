using OmniControl.App.Services;
namespace OmniControl.App.Pages;

/// <summary>One editable setting shown in the global search results. Pages make them with <see cref="PageViewModel"/>'s Entry.</summary>
public sealed class SearchEntry
{
    public required string Title { get; init; }

    /// <summary>The page it is on (what the category filter matches).</summary>
    public required string Category { get; init; }

    /// <summary>The part of the page it belongs to (a hotkey's heading, "Hardware"...), or null.</summary>
    public string? Group { get; init; }

    /// <summary>What the result shows under its title: the category, then the group.</summary>
    public string Heading => Services.Heading.Join(Category, Group);

    /// <summary>The live control: any <see cref="IEditorItem"/> that App.axaml has a card template for.</summary>
    public required object Editor { get; init; }

    public string Label => (Editor as IEditorItem)?.Label ?? string.Empty;
    public string Keywords { get; init; } = string.Empty;

    public bool Matches(string query) =>
        TextMatch.Any(query, Title, Heading, Label, Keywords);
}
