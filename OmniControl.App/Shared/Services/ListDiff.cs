namespace OmniControl.App.Shared.Services;

/// Makes a shown list equal to a new one by the fewest removes, moves, inserts and replaces, matched by key, never by clearing it.
/// A list control then keeps the rows that stay and rebinds a recycled row to the item now at its place; clearing and refilling (or
/// swapping the whole list) is what let a recycled row keep drawing the previous list's entry (the picker's ghost row).
public static class ListDiff
{
    /// <param name="keyOf">What makes two items the same row (an entry, a heading's text); keys are unique within a list.</param>
    /// <param name="same">True when an item with the same key needs no new row (its shown content is equal); reference equality when null.</param>
    /// <param name="update">Lets the shown item take the wanted one's content in place, keeping its row; true when it could.</param>
    public static void Apply<T>(ObservableCollection<T> target, IReadOnlyList<T> wanted, Func<T, object> keyOf, Func<T, T, bool>? same = null,
        Func<T, T, bool>? update = null)
    {
        same ??= (a, b) => ReferenceEquals(a, b);
        var keep = new HashSet<object>(wanted.Select(keyOf));
        for (var i = target.Count - 1; i >= 0; i--)
            if (!keep.Contains(keyOf(target[i]))) target.RemoveAt(i);

        for (var i = 0; i < wanted.Count; i++)
        {
            var key = keyOf(wanted[i]);
            var at = IndexFrom(target, i, key, keyOf);
            if (at < 0)
            {
                target.Insert(i, wanted[i]);
                continue;
            }
            if (at != i) target.Move(at, i);
            if (same(target[i], wanted[i]) || update?.Invoke(target[i], wanted[i]) == true) continue;
            // Remove and insert, never a replace: a replace keeps the list's container for the row and only rebinds it, and a
            // virtualised panel then kept the old row's measured size and drawing under the new one (two layouts on top of each other).
            target.RemoveAt(i);
            target.Insert(i, wanted[i]);
        }
        while (target.Count > wanted.Count) target.RemoveAt(target.Count - 1);
    }

    private static int IndexFrom<T>(IList<T> list, int start, object key, Func<T, object> keyOf)
    {
        for (var i = start; i < list.Count; i++)
            if (Equals(keyOf(list[i]), key)) return i;
        return -1;
    }
}
