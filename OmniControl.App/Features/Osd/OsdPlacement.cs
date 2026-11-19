namespace OmniControl.App.Features.Osd;

/// THE placement and screen maths of the OSD (pure, nothing is drawn): where a pop-up of a given size goes on a screen's working area for
/// a <see cref="OsdSpot"/>, and the way back from a position on screen (a drag, a nudge) to a spot. The real pop-up (<see cref="OsdWindow"/>)
/// and the placement window's preview (<see cref="OsdPreview"/>) both use it, so they can never disagree. Areas and sizes are in screen
/// pixels, offsets in device-independent pixels (a screen's scale converts).
public static class OsdPlacement
{
    /// The spot of one of the seven built-in places: pinned at the edge or corner, <see cref="Timing.OsdMargin"/> in.
    public static OsdSpot Of(OsdPosition position) => position switch
    {
        OsdPosition.TopLeft => new(OsdAnchor.TopLeft, Timing.OsdMargin, Timing.OsdMargin),
        OsdPosition.TopCenter => new(OsdAnchor.Top, 0, Timing.OsdMargin),
        OsdPosition.TopRight => new(OsdAnchor.TopRight, Timing.OsdMargin, Timing.OsdMargin),
        OsdPosition.Center => new(OsdAnchor.Center, 0, 0),
        OsdPosition.BottomLeft => new(OsdAnchor.BottomLeft, Timing.OsdMargin, Timing.OsdMargin),
        OsdPosition.BottomRight => new(OsdAnchor.BottomRight, Timing.OsdMargin, Timing.OsdMargin),
        _ => new(OsdAnchor.Bottom, 0, Timing.OsdMargin),
    };

    /// -1 for the left column of the grid, 1 for the right one, 0 for the middle.
    public static int Column(OsdAnchor a) => a switch
    {
        OsdAnchor.TopLeft or OsdAnchor.Left or OsdAnchor.BottomLeft => -1,
        OsdAnchor.TopRight or OsdAnchor.Right or OsdAnchor.BottomRight => 1,
        _ => 0,
    };

    /// -1 for the top row of the grid, 1 for the bottom one, 0 for the middle.
    public static int Row(OsdAnchor a) => a switch
    {
        OsdAnchor.TopLeft or OsdAnchor.Top or OsdAnchor.TopRight => -1,
        OsdAnchor.BottomLeft or OsdAnchor.Bottom or OsdAnchor.BottomRight => 1,
        _ => 0,
    };

    /// The label of the sideways offset; it names the direction the number moves the pop-up from the anchor.
    public static string AcrossLabel(OsdAnchor a) => Column(a) switch { -1 => "Right from left", 1 => "Left from right", _ => "Right from center" };

    /// The label of the up-down offset ("Up from bottom").
    public static string AwayLabel(OsdAnchor a) => Row(a) switch { -1 => "Down from top", 1 => "Up from bottom", _ => "Up from center" };

    /// The pop-up's size on a screen in pixels, from its content (measuring is free when nothing changed size).
    public static PixelSize SizeOf(Control content, double scale)
    {
        content.Measure(Size.Infinity);
        return new PixelSize((int)(content.DesiredSize.Width * scale) + Timing.OsdRoundingSlack, (int)(content.DesiredSize.Height * scale) + Timing.OsdRoundingSlack);
    }

    /// Where the pop-up's top-left corner goes (kept inside the working area, so no offset can push it off the screen).
    public static PixelPoint Position(OsdSpot spot, PixelRect area, double scale, PixelSize size)
    {
        var across = (int)Math.Round(spot.Across * scale);
        var away = (int)Math.Round(spot.Away * scale);
        var x = Column(spot.Anchor) switch
        {
            -1 => area.X + across,
            1 => area.Right - size.Width - across,
            _ => area.X + (area.Width - size.Width) / 2 + across,
        };
        var y = Row(spot.Anchor) switch
        {
            -1 => area.Y + away,
            1 => area.Bottom - size.Height - away,
            _ => area.Y + (area.Height - size.Height) / 2 - away,
        };
        return new PixelPoint(Math.Clamp(x, area.X, Math.Max(area.X, area.Right - size.Width)), Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - size.Height)));
    }

    /// The spot with this anchor that puts the pop-up at <paramref name="at"/> (the way back from <see cref="Position"/>).
    public static OsdSpot SpotAt(OsdAnchor anchor, PixelPoint at, PixelRect area, double scale, PixelSize size)
    {
        var across = Column(anchor) switch
        {
            -1 => at.X - area.X,
            1 => area.Right - size.Width - at.X,
            _ => at.X - (area.X + (area.Width - size.Width) / 2),
        };
        var away = Row(anchor) switch
        {
            -1 => at.Y - area.Y,
            1 => area.Bottom - size.Height - at.Y,
            _ => area.Y + (area.Height - size.Height) / 2 - at.Y,
        };
        return new OsdSpot(anchor, LimitOffset(across / scale), LimitOffset(away / scale));
    }

    /// The same place on screen, pinned to another anchor (the offsets change, the pop-up does not move).
    public static OsdSpot Repin(OsdSpot spot, OsdAnchor anchor, PixelRect area, double scale, PixelSize size) =>
        SpotAt(anchor, Position(spot, area, scale, size), area, scale, size);

    /// A dragged position pulled to the nearest guide within <see cref="Timing.OsdSnap"/>: the screen's edges, the built-in margin lines and the centre
    /// lines, each axis on its own.
    public static PixelPoint Snap(PixelPoint at, PixelRect area, double scale, PixelSize size)
    {
        var margin = (int)Math.Round(Timing.OsdMargin * scale);
        var reach = Timing.OsdSnap * scale;
        var x = Nearest(at.X, reach, area.X, area.X + margin, area.X + (area.Width - size.Width) / 2, area.Right - size.Width - margin, area.Right - size.Width);
        var y = Nearest(at.Y, reach, area.Y, area.Y + margin, area.Y + (area.Height - size.Height) / 2, area.Bottom - size.Height - margin, area.Bottom - size.Height);
        return new PixelPoint(x, y);
    }

    /// The spot moved <paramref name="right"/> and <paramref name="down"/> on screen (negative: left, up), in device-independent pixels.
    public static OsdSpot Nudge(OsdSpot spot, int right, int down) => spot with
    {
        Across = LimitOffset(spot.Across + (Column(spot.Anchor) == 1 ? -right : right)),
        Away = LimitOffset(spot.Away + (Row(spot.Anchor) == -1 ? down : -down)),
    };

    public static int LimitOffset(double offset) => (int)Math.Clamp(Math.Round(offset), -Timing.OsdOffsetMax, Timing.OsdOffsetMax);

    private static int Nearest(int value, double reach, params int[] guides)
    {
        var best = value;
        var bestGap = reach + 1;
        foreach (var guide in guides)
        {
            var gap = Math.Abs(guide - value);
            if (gap > reach || gap >= bestGap) continue;
            (best, bestGap) = (guide, gap);
        }
        return best;
    }
}
