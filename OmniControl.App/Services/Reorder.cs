namespace OmniControl.App.Services;

/// <summary>The one rule of a drag or a cut-and-paste in a list (<see cref="Controls.ListDragReorder"/>): pure, no UI.</summary>
public static class Reorder
{
    /// <summary>
    /// The list with the items at <paramref name="moved"/> taken out and put back, in their order, in front of the <paramref name="insertAt"/>-th
    /// of the items that stay (their count: the end). Indexes outside the list are ignored.
    /// </summary>
    public static List<T> Moved<T>(IReadOnlyList<T> list, IReadOnlyList<int> moved, int insertAt)
    {
        var picked = moved.Where(i => i >= 0 && i < list.Count).Distinct().OrderBy(i => i).ToList();
        var order = list.Where((_, i) => !picked.Contains(i)).ToList();
        order.InsertRange(Math.Clamp(insertAt, 0, order.Count), picked.Select(i => list[i]));
        return order;
    }
}
