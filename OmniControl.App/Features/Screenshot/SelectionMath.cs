using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// What a press on the selection grabs: nothing, the whole of it (move) or one of its eight handles.
public enum Grip { None, Move, N, NE, E, SE, S, SW, W, NW }

/// Where a bar may sit around the selection, in order of preference.
public enum BarSide { Right, Below }

/// The pure rules of the area selection, in image pixels: drawing it by a drag, its handles, moving and resizing it (always inside the
/// picture), the arrow-key nudge, and where the toolbars go so they stay on screen.
public static class SelectionMath
{
    /// One arrow key press moves the selection this far; with Shift, <see cref="NudgeLarge"/>.
    public const int NudgeSmall = 1, NudgeLarge = 10;

    /// The rectangle a drag from one point to another covers, held to <paramref name="bounds"/>.
    public static ScreenRect FromDrag(int x0, int y0, int x1, int y1, ScreenRect bounds)
    {
        x0 = Math.Clamp(x0, bounds.X, bounds.Right);
        x1 = Math.Clamp(x1, bounds.X, bounds.Right);
        y0 = Math.Clamp(y0, bounds.Y, bounds.Bottom);
        y1 = Math.Clamp(y1, bounds.Y, bounds.Bottom);
        return new ScreenRect(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));
    }

    /// What a press at the point grabs: a handle when within <paramref name="reach"/> of it, the inside, or nothing.
    public static Grip HitTest(ScreenRect sel, int x, int y, int reach)
    {
        bool Near(int a, int b) => Math.Abs(a - b) <= reach;
        var midX = sel.X + sel.Width / 2;
        var midY = sel.Y + sel.Height / 2;
        var left = Near(x, sel.X);
        var right = Near(x, sel.Right);
        var top = Near(y, sel.Y);
        var bottom = Near(y, sel.Bottom);
        if (top && left) return Grip.NW;
        if (top && right) return Grip.NE;
        if (bottom && left) return Grip.SW;
        if (bottom && right) return Grip.SE;
        if (top && Near(x, midX)) return Grip.N;
        if (bottom && Near(x, midX)) return Grip.S;
        if (left && Near(y, midY)) return Grip.W;
        if (right && Near(y, midY)) return Grip.E;
        return sel.Contains(x, y) ? Grip.Move : Grip.None;
    }

    /// The selection after a handle was dragged by (dx, dy): the dragged edges follow, it turns over cleanly past the other edge, held to the picture.
    public static ScreenRect Resize(ScreenRect start, Grip grip, int dx, int dy, ScreenRect bounds)
    {
        int left = start.X, top = start.Y, right = start.Right, bottom = start.Bottom;
        if (grip is Grip.W or Grip.NW or Grip.SW) left += dx;
        if (grip is Grip.E or Grip.NE or Grip.SE) right += dx;
        if (grip is Grip.N or Grip.NE or Grip.NW) top += dy;
        if (grip is Grip.S or Grip.SE or Grip.SW) bottom += dy;
        return FromDrag(left, top, right, bottom, bounds);
    }

    /// The selection moved by (dx, dy), kept whole inside the picture.
    public static ScreenRect Move(ScreenRect start, int dx, int dy, ScreenRect bounds) => start with
    {
        X = Math.Clamp(start.X + dx, bounds.X, Math.Max(bounds.X, bounds.Right - start.Width)),
        Y = Math.Clamp(start.Y + dy, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - start.Height)),
    };

    /// An arrow key: the selection moves one step (<see cref="NudgeLarge"/> with Shift) in that direction.
    public static ScreenRect Nudge(ScreenRect sel, int dirX, int dirY, bool large, ScreenRect bounds)
    {
        var step = large ? NudgeLarge : NudgeSmall;
        return Move(sel, dirX * step, dirY * step, bounds);
    }

    /// The top-left corner of a bar (in screen units of the overlay) beside a selection: the toolbar right of it, else left of it, else inside
    /// its right edge; the action bar under it (right-aligned), else above it, else inside its bottom edge. Always held inside the area.
    public static (double X, double Y) PlaceBar(BarSide side, double selX, double selY, double selW, double selH,
        double barW, double barH, double areaW, double areaH, double gap)
    {
        double x, y;
        if (side == BarSide.Right)
        {
            y = selY;
            x = selX + selW + gap + barW <= areaW ? selX + selW + gap
                : selX - gap - barW >= 0 ? selX - gap - barW
                : selX + selW - gap - barW;
        }
        else
        {
            x = selX + selW - barW;
            y = selY + selH + gap + barH <= areaH ? selY + selH + gap
                : selY - gap - barH >= 0 ? selY - gap - barH
                : selY + selH - gap - barH;
        }
        return (Math.Clamp(x, 0, Math.Max(0, areaW - barW)), Math.Clamp(y, 0, Math.Max(0, areaH - barH)));
    }

    /// The action bar's corner when a vertical bar (the toolbar, at <paramref name="tool"/>) must never be touched: its normal spot from
    /// <see cref="PlaceBar"/>; if that meets the vertical bar, straight down to just under it (plus the gap); if that does not fit the
    /// area, left of the vertical bar at the normal height. Always held inside the area.
    public static (double X, double Y) PlaceBarAvoiding(double selX, double selY, double selW, double selH, double barW, double barH,
        (double X, double Y, double W, double H) tool, double areaW, double areaH, double gap)
    {
        var (x, y) = PlaceBar(BarSide.Below, selX, selY, selW, selH, barW, barH, areaW, areaH, gap);
        bool Meets(double bx, double by) => bx < tool.X + tool.W && bx + barW > tool.X && by < tool.Y + tool.H && by + barH > tool.Y;
        if (!Meets(x, y)) return (x, y);
        var below = tool.Y + tool.H + gap;
        if (below + barH <= areaH) return (x, below);
        return (Math.Clamp(tool.X - gap - barW, 0, Math.Max(0, areaW - barW)), y);
    }
}
