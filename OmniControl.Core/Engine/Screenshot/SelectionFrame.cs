namespace OmniControl.Core.Engine.Screenshot;

/// <summary>
/// The pure geometry of the area overlay's selection, in the picture's pixels: the frame's line, its eight handles, and what must be
/// painted again when the selection changes. Only the strips that turn bright or dim and the old and new frame are repainted, so a
/// moved or resized selection costs a few thin strips, not the monitor (as Lightshot does it).
/// </summary>
public static class SelectionFrame
{
    /// <summary>Where the eight handles sit, as fractions of the selection's width and height.</summary>
    private static readonly (double X, double Y)[] HandleSpots =
        { (0, 0), (0.5, 0), (1, 0), (1, 0.5), (1, 1), (0.5, 1), (0, 1), (0, 0.5) };

    /// <summary>The frame's line: four bars <paramref name="thickness"/> pixels wide astride the selection's edge (a 1 px line just outside it).</summary>
    public static IEnumerable<ScreenRect> Line(ScreenRect selection, int thickness)
    {
        var outer = Inflate(selection, (thickness + 1) / 2);
        var side = Math.Max(0, outer.Height - 2 * thickness);
        yield return new ScreenRect(outer.X, outer.Y, outer.Width, thickness);
        yield return new ScreenRect(outer.X, outer.Bottom - thickness, outer.Width, thickness);
        yield return new ScreenRect(outer.X, outer.Y + thickness, thickness, side);
        yield return new ScreenRect(outer.Right - thickness, outer.Y + thickness, thickness, side);
    }

    /// <summary>The eight square handles of <paramref name="size"/> pixels, centred on the corners and the middle of each side.</summary>
    public static IEnumerable<ScreenRect> Handles(ScreenRect selection, int size)
    {
        foreach (var (fx, fy) in HandleSpots)
        {
            var x = (int)Math.Round(selection.X + selection.Width * fx - size / 2.0);
            var y = (int)Math.Round(selection.Y + selection.Height * fy - size / 2.0);
            yield return new ScreenRect(x, y, size, size);
        }
    }

    /// <summary>
    /// What looks different after the selection changed from <paramref name="before"/> to <paramref name="after"/> (null = none): the
    /// parts that turned bright or dim, and a strip along each side of both frames that holds the line and the handles.
    /// </summary>
    public static List<ScreenRect> Changed(ScreenRect? before, ScreenRect? after, int thickness, int handleSize)
    {
        var changed = new List<ScreenRect>();
        if (before == after) return changed;
        if (before is { } b && after is { } a)
        {
            changed.AddRange(Subtract(b, a));
            changed.AddRange(Subtract(a, b));
        }
        else changed.Add((before ?? after)!.Value);
        var reach = Math.Max((thickness + 1) / 2, (handleSize + 1) / 2) + 1;
        if (before is { } old) changed.AddRange(Edges(old, reach));
        if (after is { } now) changed.AddRange(Edges(now, reach));
        return changed;
    }

    /// <summary>The parts of <paramref name="a"/> outside <paramref name="b"/>: at most four rectangles.</summary>
    public static IEnumerable<ScreenRect> Subtract(ScreenRect a, ScreenRect b)
    {
        var inside = a.Intersect(b);
        if (inside.IsEmpty)
        {
            if (!a.IsEmpty) yield return a;
            yield break;
        }
        var parts = new[]
        {
            new ScreenRect(a.X, a.Y, a.Width, inside.Y - a.Y),
            new ScreenRect(a.X, inside.Bottom, a.Width, a.Bottom - inside.Bottom),
            new ScreenRect(a.X, inside.Y, inside.X - a.X, inside.Height),
            new ScreenRect(inside.Right, inside.Y, a.Right - inside.Right, inside.Height),
        };
        foreach (var part in parts)
            if (!part.IsEmpty) yield return part;
    }

    /// <summary>A strip <paramref name="reach"/> pixels to each side of every edge of the selection.</summary>
    private static IEnumerable<ScreenRect> Edges(ScreenRect s, int reach)
    {
        var across = 2 * reach;
        yield return new ScreenRect(s.X - reach, s.Y - reach, s.Width + across, across);
        yield return new ScreenRect(s.X - reach, s.Bottom - reach, s.Width + across, across);
        yield return new ScreenRect(s.X - reach, s.Y - reach, across, s.Height + across);
        yield return new ScreenRect(s.Right - reach, s.Y - reach, across, s.Height + across);
    }

    private static ScreenRect Inflate(ScreenRect r, int by) => new(r.X - by, r.Y - by, r.Width + 2 * by, r.Height + 2 * by);
}
