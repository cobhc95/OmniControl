namespace OmniControl.App.Services;

/// <summary>
/// A list made ready for searching: each item's fields normalised once (<see cref="TextMatch.Haystack"/>), so a key press only scans
/// prepared text. Immutable after it is made, so it may be searched off the UI thread (<see cref="SearchRunner{T}"/>); a list that
/// changes makes a new index.
/// </summary>
public sealed class SearchIndex<T>
{
    // How many items are checked between two looks at the cancellation token.
    private const int CancelCheckEvery = 64;

    private readonly (T Item, string Text)[] _entries;

    public SearchIndex(IEnumerable<T> items, Func<T, IEnumerable<string?>> fieldsOf) =>
        _entries = items.Select(i => (i, TextMatch.Haystack(fieldsOf(i)))).ToArray();

    public int Count => _entries.Length;

    /// <summary>The items that match <paramref name="query"/> (<see cref="TextMatch"/>), in list order; all of them for an empty query.</summary>
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
}
