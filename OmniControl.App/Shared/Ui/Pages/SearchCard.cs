namespace OmniControl.App.Shared.Ui.Pages;

/// A card's search words, defined once: the page makes the card from it (<c>new ToggleItem(card, ...)</c>: label, tooltip and muted line come from
/// the card, <c>new ChoiceItem(card, ...)</c> its choices too) and its result through its <see cref="SearchBuilder"/>, and its word source
/// (<c>FeaturePage.Words</c>) lists <see cref="Words"/>, so search finds the page without making it. Everything a user can read on the card
/// is searchable: its label, <see cref="Keywords"/> (words that are not shown), the <see cref="Choices"/> of a list or selector, the rows of a
/// <see cref="Menu"/>, the <see cref="Tip"/> and the muted <see cref="Note"/>.
/// <param name="Title">The result's title when it is not the label.</param>
public sealed record SearchCard(string Label, string Keywords, string? Group = null, string? Title = null)
{
    /// The options of a pull-down, selector or gallery, from the list that fills it (never typed again).
    public IReadOnlyList<string> Choices { get; init; } = Array.Empty<string>();

    /// The rows of the card's menu that are always there (not the ones that come from devices or settings, which the card's editor adds).
    public IReadOnlyList<string> Menu { get; init; } = Array.Empty<string>();

    /// The card's tooltip; null: none.
    public string? Tip { get; init; }

    /// The muted line under the label; null: none.
    public string? Note { get; init; }

    /// Everything on the card besides its label and keywords.
    public IEnumerable<string> Details => Choices.Concat(Menu).Append(Tip).Append(Note).Where(d => !string.IsNullOrEmpty(d))!;

    public string Words => SearchEntry.WordsOf(new[] { Title ?? Label, Group, Label, Keywords }.Concat(Details).ToArray());
}
