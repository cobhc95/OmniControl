namespace OmniControl.Core.Engine.Screenshot;

/// One window behind the area overlay: its visible frame (without the invisible resize border) and the name shown while it is hovered.
public sealed record DesktopWindow(ScreenRect Frame, string Name);

/// One straight part of an edge a selection may snap to: a vertical edge at X = <see cref="At"/> from Y = <see cref="Start"/> to
/// <see cref="End"/>, or a horizontal one at Y = <see cref="At"/> from X = <see cref="Start"/> to <see cref="End"/>.
public readonly record struct SnapEdge(int At, int Start, int End);

/// The windows and the visible edges behind the area overlay, read once when it opens (the picture is frozen, so they cannot move).
/// Windows are front to back. Rectangles are in physical pixels: on the desktop as read, in a monitor's picture after <see cref="Within"/>.
public sealed record DesktopLayout(IReadOnlyList<DesktopWindow> Windows, IReadOnlyList<SnapEdge> Vertical, IReadOnlyList<SnapEdge> Horizontal)
{
    public static readonly DesktopLayout Empty = new(Array.Empty<DesktopWindow>(), Array.Empty<SnapEdge>(), Array.Empty<SnapEdge>());

    /// The part of the layout on one monitor, moved into that monitor's picture pixels (its top-left corner becomes 0,0).
    public DesktopLayout Within(ScreenRect monitor)
    {
        var area = new ScreenRect(0, 0, monitor.Width, monitor.Height);
        var windows = Windows
            .Select(w => w with { Frame = w.Frame.Offset(-monitor.X, -monitor.Y).Intersect(area) })
            .Where(w => !w.Frame.IsEmpty)
            .ToList();
        return new DesktopLayout(windows,
            Clip(Vertical, -monitor.X, -monitor.Y, monitor.Width, monitor.Height),
            Clip(Horizontal, -monitor.Y, -monitor.X, monitor.Height, monitor.Width));
    }

    /// Edges moved by (<paramref name="along"/> across the edge, <paramref name="span"/> along it), keeping those inside the picture.
    private static List<SnapEdge> Clip(IReadOnlyList<SnapEdge> edges, int along, int span, int size, int length)
    {
        var kept = new List<SnapEdge>();
        foreach (var e in edges)
        {
            var at = e.At + along;
            var start = Math.Max(0, e.Start + span);
            var end = Math.Min(length, e.End + span);
            if (at >= 0 && at <= size && end > start) kept.Add(new SnapEdge(at, start, end));
        }
        return kept;
    }
}

/// The pure geometry of <see cref="DesktopLayout"/>: which windows can be seen at all and which parts of their edges are not hidden
/// behind a window in front, so a selection never snaps to a line that is not in the picture.
public static class SnapEdges
{
    /// At most this many visible pieces are kept per window while working out what is hidden (enough for any real desktop).
    private const int PieceLimit = 64;

    /// The layout for windows given front to back (<paramref name="frames"/>, with names) plus the monitors' own edges and their work
    /// areas (<paramref name="screens"/>: the taskbar's edge), which nothing hides. Edges shorter than <paramref name="shortest"/> are left out.
    public static DesktopLayout Build(IReadOnlyList<DesktopWindow> frames, IEnumerable<ScreenRect> screens, int shortest)
    {
        var windows = new List<DesktopWindow>();
        var vertical = new List<SnapEdge>();
        var horizontal = new List<SnapEdge>();
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i].Frame;
            if (frame.IsEmpty || !Seen(frame, frames, i)) continue;
            windows.Add(frames[i]);
            AddVisible(vertical, frame.X, frame.Y, frame.Bottom, frames, i, true, shortest);
            AddVisible(vertical, frame.Right, frame.Y, frame.Bottom, frames, i, true, shortest);
            AddVisible(horizontal, frame.Y, frame.X, frame.Right, frames, i, false, shortest);
            AddVisible(horizontal, frame.Bottom, frame.X, frame.Right, frames, i, false, shortest);
        }
        foreach (var screen in screens.Where(s => !s.IsEmpty))
        {
            vertical.Add(new SnapEdge(screen.X, screen.Y, screen.Bottom));
            vertical.Add(new SnapEdge(screen.Right, screen.Y, screen.Bottom));
            horizontal.Add(new SnapEdge(screen.Y, screen.X, screen.Right));
            horizontal.Add(new SnapEdge(screen.Bottom, screen.X, screen.Right));
        }
        return new DesktopLayout(windows, vertical.Distinct().ToList(), horizontal.Distinct().ToList());
    }

    /// True when some part of window <paramref name="index"/> is not covered by the windows in front of it.
    private static bool Seen(ScreenRect frame, IReadOnlyList<DesktopWindow> frames, int index)
    {
        var pieces = new List<ScreenRect> { frame };
        for (var front = 0; front < index && pieces.Count > 0; front++)
        {
            var cover = frames[front].Frame;
            pieces = pieces.SelectMany(p => SelectionFrame.Subtract(p, cover)).Take(PieceLimit).ToList();
        }
        return pieces.Count > 0;
    }

    /// Adds the parts of one edge (at <paramref name="at"/>, from <paramref name="start"/> to <paramref name="end"/>) that no window in front hides.
    private static void AddVisible(List<SnapEdge> into, int at, int start, int end, IReadOnlyList<DesktopWindow> frames, int index, bool vertical, int shortest)
    {
        var parts = new List<(int Start, int End)> { (start, end) };
        // The line of pixels the edge is drawn on: the first row or column inside the window, or the last one for its right and bottom edges.
        var own = frames[index].Frame;
        var line = vertical ? (at == own.Right ? at - 1 : at) : (at == own.Bottom ? at - 1 : at);
        for (var front = 0; front < index && parts.Count > 0; front++)
        {
            var cover = frames[front].Frame;
            var (lineFrom, lineTo, spanFrom, spanTo) = vertical ? (cover.X, cover.Right, cover.Y, cover.Bottom) : (cover.Y, cover.Bottom, cover.X, cover.Right);
            if (cover.IsEmpty || line < lineFrom || line >= lineTo) continue;
            parts = parts.SelectMany(p => Cut(p, spanFrom, spanTo)).ToList();
        }
        foreach (var (s, e) in parts)
            if (e - s >= shortest) into.Add(new SnapEdge(at, s, e));
    }

    /// One stretch with [<paramref name="from"/>, <paramref name="to"/>) cut out: none, one or two stretches.
    private static IEnumerable<(int Start, int End)> Cut((int Start, int End) part, int from, int to)
    {
        if (to <= part.Start || from >= part.End)
        {
            yield return part;
            yield break;
        }
        if (from > part.Start) yield return (part.Start, from);
        if (to < part.End) yield return (to, part.End);
    }
}
