using Avalonia;

namespace OmniControl.App.Services;

/// <summary>The one rule of a drag or a cut-and-paste in a list (<see cref="Controls.ListDragReorder"/>, <see cref="Controls.PanelDragReorder"/>): pure, no UI.</summary>
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

    /// <summary>
    /// The slot one dragged item lands in (<see cref="Controls.PanelDragReorder"/>: a grid of tiles or a list of rows): the slot whose centre is
    /// nearest to the dragged item's centre now. The slots are where the items stand before the drag, in their order. -1 when there are none.
    /// </summary>
    public static int TargetSlot(IReadOnlyList<Rect> slots, Point centre)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < slots.Count; i++)
        {
            Vector d = slots[i].Center - centre;
            var distance = d.X * d.X + d.Y * d.Y;
            if (distance < bestDistance) (best, bestDistance) = (i, distance);
        }
        return best;
    }

    /// <summary>The slot item <paramref name="i"/> is shown in while the item at <paramref name="from"/> is dragged over slot <paramref name="to"/>: the items between move one place aside.</summary>
    public static int ShownSlot(int i, int from, int to) =>
        i == from ? to
        : from < to && i > from && i <= to ? i - 1
        : to < from && i >= to && i < from ? i + 1
        : i;
}
