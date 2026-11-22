namespace OmniControl.App.Shared.Ui.Pages;

/// A search result's words, defined once: the page makes its card with <see cref="Label"/> and its result with
/// <see cref="PageViewModel"/>'s Entry, and its word source (<c>FeaturePage.Words</c>) lists <see cref="Words"/>, so search finds the
/// page without making it.
/// <param name="Title">The result's title when it is not the label.</param>
public sealed record SearchCard(string Label, string Keywords, string? Group = null, string? Title = null)
{
    public string Words => SearchEntry.WordsOf(Title ?? Label, Group, Label, Keywords);
}
