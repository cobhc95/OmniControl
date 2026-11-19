namespace OmniControl.Core.Engine.Mouse;

/// A rectangle on the screen in physical pixels; <see cref="Right"/> and <see cref="Bottom"/> are just outside it.
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public bool Intersects(ScreenRect other) =>
        !IsEmpty && !other.IsEmpty && other.Left < Right && Left < other.Right && other.Top < Bottom && Top < other.Bottom;

    /// The part both rectangles share (empty when they do not meet).
    public ScreenRect Intersect(ScreenRect other) =>
        new(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));

    /// What is left of this rectangle once <paramref name="other"/> is cut out of it: up to four non-overlapping pieces.
    public IEnumerable<ScreenRect> Minus(ScreenRect other)
    {
        if (!Intersects(other)) { if (!IsEmpty) yield return this; yield break; }
        var top = Math.Max(Top, other.Top);
        var bottom = Math.Min(Bottom, other.Bottom);
        var pieces = new[]
        {
            this with { Bottom = top },                                      // above the cut
            this with { Top = bottom },                                      // below it
            new ScreenRect(Left, top, Math.Max(Left, other.Left), bottom),   // left of it
            new ScreenRect(Math.Min(Right, other.Right), top, Right, bottom), // right of it
        };
        foreach (var piece in pieces)
            if (!piece.IsEmpty) yield return piece;
    }
}

/// One rectangle where a gesture area applies.
public readonly record struct ScreenArea(PointerAreaKind Kind, ScreenRect Rect);

/// One gesture area as drawn: <see cref="Bounds"/> is the whole area, <see cref="Pieces"/> the parts of it where it really applies
/// once the areas that win over it (<see cref="PointerArea.At"/>'s order) are cut out.
public sealed record MappedArea(PointerAreaKind Kind, ScreenRect Bounds, IReadOnlyList<ScreenRect> Pieces);
