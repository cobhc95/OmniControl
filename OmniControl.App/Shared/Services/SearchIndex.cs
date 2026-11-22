namespace OmniControl.App.Shared.Services;

/// A list made ready for searching: each item's fields normalised once (<see cref="TextMatch.Haystack"/>), so a key press only scans
/// prepared text. Immutable after it is made, so it may be searched off the UI thread (<see cref="SearchRunner{T}"/>); a list that
/// changes makes a new index.
public sealed class SearchIndex<T>
{
    // How many items are checked between two looks at the cancellation token.
    private const int CancelCheckEvery = 64;

    private readonly (T Item, string Text)[] _entries;

    // Every distinct word of every item, and each item's words as places in that list, made at the first typo-tolerant search only (most
    // searches find an exact match). A query word is compared with each distinct word once, however many items hold it.
    private readonly Lazy<(string[] Words, int[][] OfItem)> _fuzzy;

    public SearchIndex(IEnumerable<T> items, Func<T, IEnumerable<string?>> fieldsOf)
    {
        _entries = items.Select(i => (i, TextMatch.Haystack(fieldsOf(i)))).ToArray();
        _fuzzy = new Lazy<(string[], int[][])>(MakeWords);
    }

    public int Count => _entries.Length;

    private (string[] Words, int[][] OfItem) MakeWords()
    {
        var places = new Dictionary<string, int>(StringComparer.Ordinal);
        var ofItem = _entries.Select(e => FuzzyMatch.WordsOf(e.Text).Select(w =>
        {
            if (!places.TryGetValue(w, out var at)) places[w] = at = places.Count;
            return at;
        }).ToArray()).ToArray();
        var words = new string[places.Count];
        foreach (var (word, at) in places) words[at] = word;
        return (words, ofItem);
    }

    /// The items that match <paramref name="query"/> (<see cref="TextMatch"/>) and <paramref name="accept"/>, in list order; all of them for an empty query.
    /// <exception cref="OperationCanceledException">A newer query took over.</exception>
    public List<T> Match(string query, CancellationToken token = default, Func<T, bool>? accept = null)
    {
        var words = TextMatch.Words(query);
        var found = new List<T>();
        for (var i = 0; i < _entries.Length; i++)
        {
            if (i % CancelCheckEvery == 0) token.ThrowIfCancellationRequested();
            if (TextMatch.AllIn(words, _entries[i].Text) && accept?.Invoke(_entries[i].Item) != false) found.Add(_entries[i].Item);
        }
        return found;
    }

    /// The items whose words are near the query's (<see cref="FuzzyMatch"/>) and that <paramref name="accept"/>, the nearest first and then in list
    /// order, at most <see cref="FuzzyMatch.Cap"/>; for when <see cref="Match"/> found nothing. The filter acts before the cap, so a filtered list
    /// still shows its own nearest items.
    /// <exception cref="OperationCanceledException">A newer query took over.</exception>
    public List<T> Similar(string query, CancellationToken token = default, Func<T, bool>? accept = null)
    {
        var words = TextMatch.Words(query);
        if (words.Length == 0) return new List<T>();
        var (distinct, ofItem) = _fuzzy.Value;
        var near = words.Select(q => distinct.Select(w => FuzzyMatch.Between(q, w)).ToArray()).ToArray();
        var found = new List<(int Score, int Order, T Item)>();
        for (var i = 0; i < _entries.Length; i++)
        {
            if (i % CancelCheckEvery == 0) token.ThrowIfCancellationRequested();
            var score = ScoreOf(near, ofItem[i]);
            if (score >= 0 && accept?.Invoke(_entries[i].Item) != false) found.Add((score, i, _entries[i].Item));
        }
        return found.OrderBy(f => f.Score).ThenBy(f => f.Order).Take(FuzzyMatch.Cap).Select(f => f.Item).ToList();
    }

    // The total edits of every query word to its nearest word of the item, or -1 when one of them has none near enough.
    private static int ScoreOf(int[][] near, int[] itemWords)
    {
        var total = 0;
        foreach (var distances in near)
        {
            var best = -1;
            foreach (var at in itemWords)
            {
                var d = distances[at];
                if (d >= 0 && (best < 0 || d < best)) best = d;
                if (best == 0) break;
            }
            if (best < 0) return -1;
            total += best;
        }
        return total;
    }
}
