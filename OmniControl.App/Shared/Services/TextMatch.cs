namespace OmniControl.App.Shared.Services;

/// The one search rule: a query matches when every word of it (split on spaces) is contained in at least one of the fields, ignoring case.
/// The text is normalised once (<see cref="Haystack"/>), so a list searched on every key keeps it in a <see cref="SearchIndex{T}"/>.
public static class TextMatch
{
    // Joins the fields: a word has no line break, so a word is in the joined text exactly when it is in one of the fields.
    private const char FieldBreak = '\n';

    public static bool Any(string query, params string?[] fields) => AllIn(Words(query), Haystack(fields));

    /// The query's words, normalised; none for an empty query (which matches everything).
    public static string[] Words(string query) =>
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Normalise).ToArray();

    /// The fields as one normalised text that <see cref="AllIn"/> searches.
    public static string Haystack(IEnumerable<string?> fields) =>
        Normalise(string.Join(FieldBreak, fields.Where(f => !string.IsNullOrEmpty(f))));

    public static bool AllIn(IReadOnlyList<string> words, string haystack)
    {
        foreach (var word in words)
            if (!haystack.Contains(word, StringComparison.Ordinal)) return false;
        return true;
    }

    private static string Normalise(string text) => text.ToLowerInvariant();
}
