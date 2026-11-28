using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// One edge the selection is snapped to, for the guide line: vertical at X = <see cref="At"/> (or horizontal at Y = <see cref="At"/>),
/// running from <see cref="From"/> to <see cref="To"/> (the snapped window edge stretched to cover the selection).
public readonly record struct SnapGuide(bool Vertical, int At, int From, int To);

/// The pure rules of snapping the area selection to one monitor's window edges (its <see cref="DesktopLayout"/>, in picture pixels):
/// an edge being dragged jumps onto a visible window, taskbar or monitor edge within <paramref name="reach"/> pixels, but only to an edge
/// that runs alongside the selection; and which window is under a point (for picking a window with a click). The snapped edges are
/// added to the guides list given.
public sealed class WindowSnap(DesktopLayout layout)
{
    /// How far an edge jumps, in screen units (times the monitor's scale for picture pixels).
    public const double Distance = 8;

    public IReadOnlyList<DesktopWindow> Windows => layout.Windows;

    /// The front-most window whose frame holds the point, or null (the empty desktop).
    public DesktopWindow? WindowAt(int x, int y)
    {
        foreach (var window in layout.Windows)
            if (window.Frame.Contains(x, y)) return window;
        return null;
    }

    /// A new selection dragged from (x0, y0) to (x1, y1): both corners snap.
    public ScreenRect Drag(int x0, int y0, int x1, int y1, ScreenRect bounds, int reach, List<SnapGuide> guides)
    {
        int top = Math.Min(y0, y1), bottom = Math.Max(y0, y1), left = Math.Min(x0, x1), right = Math.Max(x0, x1);
        var sx0 = Snap(x0, true, top, bottom, reach, guides);
        var sx1 = Snap(x1, true, top, bottom, reach, guides);
        var sy0 = Snap(y0, false, left, right, reach, guides);
        var sy1 = Snap(y1, false, left, right, reach, guides);
        return SelectionMath.FromDrag(sx0, sy0, sx1, sy1, bounds);
    }

    /// The selection after a handle was dragged by (dx, dy): only the dragged edges snap.
    public ScreenRect Resize(ScreenRect start, Grip grip, int dx, int dy, ScreenRect bounds, int reach, List<SnapGuide> guides)
    {
        int left = start.X, top = start.Y, right = start.Right, bottom = start.Bottom;
        bool west = grip is Grip.W or Grip.NW or Grip.SW, east = grip is Grip.E or Grip.NE or Grip.SE;
        bool north = grip is Grip.N or Grip.NE or Grip.NW, south = grip is Grip.S or Grip.SE or Grip.SW;
        if (west) left += dx;
        if (east) right += dx;
        if (north) top += dy;
        if (south) bottom += dy;
        // the edges may have crossed (the selection turns over), so the span along an edge is taken in order
        int spanTop = Math.Min(top, bottom), spanBottom = Math.Max(top, bottom);
        if (west) left = Snap(left, true, spanTop, spanBottom, reach, guides);
        if (east) right = Snap(right, true, spanTop, spanBottom, reach, guides);
        int spanLeft = Math.Min(left, right), spanRight = Math.Max(left, right);
        if (north) top = Snap(top, false, spanLeft, spanRight, reach, guides);
        if (south) bottom = Snap(bottom, false, spanLeft, spanRight, reach, guides);
        return SelectionMath.FromDrag(left, top, right, bottom, bounds);
    }

    /// The selection moved by (dx, dy), its size kept: the nearer of its left and right edges snaps, and the nearer of its top and bottom.
    public ScreenRect Move(ScreenRect start, int dx, int dy, ScreenRect bounds, int reach, List<SnapGuide> guides)
    {
        var moved = SelectionMath.Move(start, dx, dy, bounds);
        var x = Nearer(moved.X, moved.Right, moved.Width, true, moved.Y, moved.Bottom, reach);
        var y = Nearer(moved.Y, moved.Bottom, moved.Height, false, moved.X, moved.Right, reach);
        var snapped = SelectionMath.Move(moved, x.Shift, y.Shift, bounds);
        if (x.Edge is { } ex && snapped.X - moved.X == x.Shift) guides.Add(Guide(true, ex, snapped.Y, snapped.Bottom));
        if (y.Edge is { } ey && snapped.Y - moved.Y == y.Shift) guides.Add(Guide(false, ey, snapped.X, snapped.Right));
        return snapped;
    }

    /// How far the selection moves so its nearer side (low or high) meets an edge, and that edge; (0, null) when none is within reach.
    private (int Shift, SnapEdge? Edge) Nearer(int low, int high, int size, bool vertical, int spanFrom, int spanTo, int reach)
    {
        var a = Closest(low, vertical, spanFrom, spanTo, reach);
        var b = Closest(high, vertical, spanFrom, spanTo, reach);
        if (a is null && b is null) return (0, null);
        if (b is null || a is { } ea && Math.Abs(ea.At - low) <= Math.Abs(b.Value.At - high)) return (a!.Value.At - low, a);
        return (b.Value.At - high, b);
    }

    /// <paramref name="value"/> moved onto the closest edge within reach that runs alongside [<paramref name="spanFrom"/>, <paramref name="spanTo"/>].
    private int Snap(int value, bool vertical, int spanFrom, int spanTo, int reach, List<SnapGuide> guides)
    {
        if (Closest(value, vertical, spanFrom, spanTo, reach) is not { } edge) return value;
        guides.Add(Guide(vertical, edge, spanFrom, spanTo));
        return edge.At;
    }

    private SnapEdge? Closest(int value, bool vertical, int spanFrom, int spanTo, int reach)
    {
        SnapEdge? best = null;
        var bestGap = int.MaxValue;
        foreach (var edge in vertical ? layout.Vertical : layout.Horizontal)
        {
            if (spanTo < edge.Start - reach || spanFrom > edge.End + reach) continue; // not alongside the selection
            var gap = Math.Abs(edge.At - value);
            if (gap > reach || gap >= bestGap) continue;
            best = edge;
            bestGap = gap;
        }
        return best;
    }

    private static SnapGuide Guide(bool vertical, SnapEdge edge, int spanFrom, int spanTo) =>
        new(vertical, edge.At, Math.Min(edge.Start, spanFrom), Math.Max(edge.End, spanTo));
}
