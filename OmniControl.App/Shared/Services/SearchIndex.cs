namespace OmniControl.App.Shared.Services;

/// A list made ready for searching: each item's fields normalised once (<see cref="TextMatch.Haystack"/>), so a key press only scans
/// prepared text. Immutable after it is made, so it may be searched off the UI thread (<see cref="SearchRunner{T}"/>); a list that
/// changes makes a new index.
public sealed class SearchIndex<T>
{
    // How many items are checked between two looks at the cancellation token.
    private const int CancelCheckEvery = 64;

    private readonly (T Item, string Text)[] _entries;

    // Each item's distinct words, made at the first typo-tolerant search only (most searches find an exact match).
    private readonly Lazy<string[][]> _words;

    public SearchIndex(IEnumerable<T> items, Func<T, IEnumerable<string?>> fieldsOf)
    {
        _entries = items.Select(i => (i, TextMatch.Haystack(fieldsOf(i)))).ToArray();
        _words = new Lazy<string[][]>(() => _entries.Select(e => FuzzyMatch.WordsOf(e.Text)).ToArray());
    }

    public int Count => _entries.Length;

    /// The items that match <paramref name="query"/> (<see cref="TextMatch"/>), in list order; all of them for an empty query.
    /// <exception cref="OperationCanceledException">A newer query took over.</exception>
    public List<T> Match(string query, CancellationToken token = default)
    {
        var words = TextMatch.Words(query);
        var found = new List<T>();
        for (var i = 0; i < _entries.Length; i++)
        {
            if (i % CancelCheckEvery == 0) token.ThrowIfCancellationRequested();
            if (TextMatch.AllIn(words, _entries[i].Text)) found.Add(_entries[i].Item);
        }
        return found;
    }

    /// The items whose words are near the query's (<see cref="FuzzyMatch"/>), the nearest first and then in list order, at most
    /// <see cref="FuzzyMatch.Cap"/>; for when <see cref="Match"/> found nothing.
    /// <exception cref="OperationCanceledException">A newer query took over.</exception>
    public List<T> Similar(string query, CancellationToken token = default)
    {
        var words = TextMatch.Words(query);
        var found = new List<(int Score, int Order, T Item)>();
        if (words.Length == 0) return new List<T>();
        var textWords = _words.Value;
        for (var i = 0; i < _entries.Length; i++)
        {
            if (i % CancelCheckEvery == 0) token.ThrowIfCancellationRequested();
            var score = FuzzyMatch.Score(words, textWords[i]);
            if (score >= 0) found.Add((score, i, _entries[i].Item));
        }
        return found.OrderBy(f => f.Score).ThenBy(f => f.Order).Take(FuzzyMatch.Cap).Select(f => f.Item).ToList();
    }
}
