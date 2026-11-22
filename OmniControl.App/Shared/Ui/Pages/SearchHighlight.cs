using Avalonia.Controls.Documents;

namespace OmniControl.App.Shared.Ui.Pages;

/// THE highlight of a search result's text: a <c>TextBlock</c> with <see cref="TextProperty"/> and <see cref="QueryProperty"/> shows the text
/// with the parts the query matches tinted in the soft accent (<c>BrushAccentSoft</c>, which <see cref="Services.AccentService"/> keeps), exact
/// or near (<see cref="Services.FuzzyMatch.Hits"/>). With <see cref="EntryProperty"/> instead of the text it shows, quoted, the card's own text
/// (a choice, a menu item, the tooltip) that holds the match when the result's title and heading do not, and nothing otherwise.
public static class SearchHighlight
{
    private const string TintKey = "BrushAccentSoft";

    public static readonly AttachedProperty<string?> TextProperty = AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(SearchHighlight));
    public static readonly AttachedProperty<string?> QueryProperty = AvaloniaProperty.RegisterAttached<TextBlock, string?>("Query", typeof(SearchHighlight));
    public static readonly AttachedProperty<SearchEntry?> EntryProperty = AvaloniaProperty.RegisterAttached<TextBlock, SearchEntry?>("Entry", typeof(SearchHighlight));

    public static string? GetText(TextBlock box) => box.GetValue(TextProperty);
    public static void SetText(TextBlock box, string? value) => box.SetValue(TextProperty, value);
    public static string? GetQuery(TextBlock box) => box.GetValue(QueryProperty);
    public static void SetQuery(TextBlock box, string? value) => box.SetValue(QueryProperty, value);
    public static SearchEntry? GetEntry(TextBlock box) => box.GetValue(EntryProperty);
    public static void SetEntry(TextBlock box, SearchEntry? value) => box.SetValue(EntryProperty, value);

    static SearchHighlight()
    {
        TextProperty.Changed.AddClassHandler<TextBlock>((box, _) => Show(box));
        QueryProperty.Changed.AddClassHandler<TextBlock>((box, _) => Show(box));
        EntryProperty.Changed.AddClassHandler<TextBlock>((box, _) => Show(box));
    }

    private static void Show(TextBlock box)
    {
        var words = TextMatch.Words(GetQuery(box) ?? string.Empty);
        var entry = GetEntry(box);
        var found = entry?.FoundIn(words);
        var text = entry is null ? GetText(box) : found is null ? null : $"\"{found}\"";
        if (entry is not null) box.IsVisible = found is not null;
        var hits = string.IsNullOrEmpty(text) ? Array.Empty<(int Start, int Length)>() : Services.FuzzyMatch.Hits(text, words);
        if (hits.Count == 0)
        {
            box.Inlines = null;
            box.Text = text;
            return;
        }
        var tint = box.TryFindResource(TintKey, box.ActualThemeVariant, out var brush) ? brush as IBrush : null;
        var runs = new InlineCollection();
        var at = 0;
        foreach (var (start, length) in hits)
        {
            if (start > at) runs.Add(new Run(text![at..start]));
            runs.Add(new Run(text!.Substring(start, length)) { Background = tint });
            at = start + length;
        }
        if (at < text!.Length) runs.Add(new Run(text[at..]));
        box.Text = null;
        box.Inlines = runs;
    }
}
