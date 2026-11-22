namespace OmniControl.App.Shared.Services;

/// The typo-tolerant half of search, used only when <see cref="TextMatch"/> finds nothing: a query word is "similar" to a word of the text
/// when it is within a few edits (Damerau-Levenshtein, adjacent swaps count once) of the whole word or of the same-length start of it
/// (a half-typed word with a slip), and the highlight of a result's text. Pure; all texts are already lower case (<see cref="TextMatch.Haystack"/>).
public static class FuzzyMatch
{
    /// How many similar results are shown at most.
    public const int Cap = 20;

    // A word of this length or less must be typed right: one edit in "vol" would match half the words.
    private const int ExactOnlyUpTo = 3, OneEditUpTo = 7;

    // A highlight of a near miss covers at least this many shared start letters.
    private const int MinSharedStart = 3;

    /// How many edits a query word of this length may be away from a word of the text.
    public static int Allowed(int length) => length <= ExactOnlyUpTo ? 0 : length <= OneEditUpTo ? 1 : 2;

    /// The distinct words of a text (letters and digits), in the order they appear.
    public static string[] WordsOf(string text)
    {
        var words = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (inWord && start < 0) start = i;
            else if (!inWord && start >= 0)
            {
                var word = text[start..i];
                if (word.Length > 1 && !words.Contains(word)) words.Add(word);
                start = -1;
            }
        }
        return words.ToArray();
    }

    /// The edits between two words (optimal string alignment: insert, delete, replace, swap two neighbours).
    public static int Distance(string a, string b)
    {
        var prev2 = new int[b.Length + 1];
        var prev = new int[b.Length + 1];
        var row = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                row[j] = Math.Min(Math.Min(prev[j] + 1, row[j - 1] + 1), prev[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) row[j] = Math.Min(row[j], prev2[j - 2] + 1);
            }
            (prev2, prev, row) = (prev, row, prev2);
        }
        return prev[b.Length];
    }

    /// How far <paramref name="query"/> is from <paramref name="word"/> (0: it is in it), or -1 when more than allowed for its length.
    public static int Between(string query, string word)
    {
        if (word.Contains(query, StringComparison.Ordinal)) return 0;
        var allowed = Allowed(query.Length);
        if (allowed == 0 || query.Length - word.Length > allowed) return -1;
        var best = Distance(query, word);
        if (word.Length > query.Length) best = Math.Min(best, Distance(query, word[..query.Length]));
        return best <= allowed ? best : -1;
    }

    /// The total edits of every query word to its nearest word of the text, or -1 when one of them has none near enough.
    public static int Score(IReadOnlyList<string> query, IReadOnlyList<string> textWords)
    {
        var total = 0;
        foreach (var q in query)
        {
            var best = -1;
            foreach (var w in textWords)
            {
                var d = Between(q, w);
                if (d >= 0 && (best < 0 || d < best)) best = d;
                if (best == 0) break;
            }
            if (best < 0) return -1;
            total += best;
        }
        return total;
    }

    /// The parts of <paramref name="text"/> to highlight for the query words: where a word is in it, else the start a near miss shares with a word.
    public static IReadOnlyList<(int Start, int Length)> Hits(string text, IReadOnlyList<string> query)
    {
        var found = new List<(int Start, int Length)>();
        foreach (var q in query.Where(q => q.Length > 0))
        {
            var at = text.IndexOf(q, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                for (; at >= 0; at = text.IndexOf(q, at + q.Length, StringComparison.OrdinalIgnoreCase)) found.Add((at, q.Length));
                continue;
            }
            foreach (var (start, length) in WordSpans(text))
            {
                var shared = 0;
                while (shared < length && shared < q.Length && char.ToLowerInvariant(text[start + shared]) == q[shared]) shared++;
                if (shared >= MinSharedStart && shared >= q.Length - Allowed(q.Length)) found.Add((start, shared));
            }
        }
        return Merge(found);
    }

    private static IEnumerable<(int Start, int Length)> WordSpans(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (inWord && start < 0) start = i;
            else if (!inWord && start >= 0)
            {
                yield return (start, i - start);
                start = -1;
            }
        }
    }

    private static List<(int Start, int Length)> Merge(List<(int Start, int Length)> spans)
    {
        var merged = new List<(int Start, int Length)>();
        foreach (var (start, length) in spans.OrderBy(s => s.Start))
        {
            if (merged.Count > 0 && start <= merged[^1].Start + merged[^1].Length)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].Length, start + length - merged[^1].Start));
            else merged.Add((start, length));
        }
        return merged;
    }
}
