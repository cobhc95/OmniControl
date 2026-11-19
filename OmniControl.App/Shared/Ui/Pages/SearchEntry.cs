namespace OmniControl.App.Shared.Ui.Pages;

/// One editable setting shown in the global search results. Pages make them with <see cref="PageViewModel"/>'s Entry.
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

    /// What the search matches (<see cref="TextMatch"/>).
    public IEnumerable<string?> SearchFields => new[] { Title, Heading, Label, Keywords };
}
