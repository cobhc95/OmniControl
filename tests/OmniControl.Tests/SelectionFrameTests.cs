using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.Tests;

/// <summary>
/// The area overlay's picture surface repaints only <see cref="SelectionFrame.Changed"/> when the selection changes. Every pixel that looks
/// different afterwards (dim to bright, bright to dim, the old or new frame and handles) must lie inside those rectangles, or the screen
/// keeps a stale stripe. Checked on a small model of the screen, pixel by pixel.
/// </summary>
public class SelectionFrameTests
{
    private const int Size = 120, Line = 2, Handle = 7;

    /// <summary>What each pixel shows for a selection: 0 dim, 1 bright, 2 frame or handle (drawn last, as the surface does).</summary>
    private static int[,] Look(ScreenRect? selection)
    {
        var look = new int[Size, Size];
        if (selection is not { } s) return look;
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                look[y, x] = s.Contains(x, y) ? 1 : 0;
        foreach (var part in SelectionFrame.Line(s, Line).Concat(SelectionFrame.Handles(s, Handle)))
            for (var y = Math.Max(0, part.Y); y < Math.Min(Size, part.Bottom); y++)
                for (var x = Math.Max(0, part.X); x < Math.Min(Size, part.Right); x++)
                    look[y, x] = 2;
        return look;
    }

    public static TheoryData<ScreenRect?, ScreenRect?> Moves => new()
    {
        { null, new ScreenRect(20, 20, 50, 40) },
        { new ScreenRect(20, 20, 50, 40), null },
        { new ScreenRect(20, 20, 50, 40), new ScreenRect(23, 18, 50, 40) },   // moved a little
        { new ScreenRect(20, 20, 50, 40), new ScreenRect(60, 70, 50, 40) },   // moved far, no overlap
        { new ScreenRect(20, 20, 50, 40), new ScreenRect(20, 20, 61, 47) },   // resized from a corner
        { new ScreenRect(20, 20, 50, 40), new ScreenRect(15, 25, 40, 20) },   // smaller and moved
        { new ScreenRect(30, 30, 1, 1), new ScreenRect(30, 30, 2, 3) },       // a selection just starting
    };

    [Theory]
    [MemberData(nameof(Moves))]
    public void Changed_CoversEveryPixelThatLooksDifferent(ScreenRect? before, ScreenRect? after)
    {
        var old = Look(before);
        var now = Look(after);
        var repainted = SelectionFrame.Changed(before, after, Line, Handle);
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (old[y, x] != now[y, x])
                    Assert.True(repainted.Any(r => r.Contains(x, y)), $"pixel {x},{y} changed but is not repainted");
    }

    [Fact]
    public void Changed_IsSmallForASmallMove()
    {
        var before = new ScreenRect(100, 100, 2000, 1500);
        var repainted = SelectionFrame.Changed(before, before.Offset(3, 2), 2, 12);
        var pixels = repainted.Sum(r => (long)r.Width * r.Height);
        Assert.True(pixels < 2000L * 1500 / 10, $"a 3 px move repaints {pixels} pixels");
    }

    [Fact]
    public void Changed_NothingWhenTheSelectionStays() =>
        Assert.Empty(SelectionFrame.Changed(new ScreenRect(5, 5, 10, 10), new ScreenRect(5, 5, 10, 10), Line, Handle));

    [Fact]
    public void Subtract_LeavesTheRestAsAtMostFourPieces()
    {
        var a = new ScreenRect(0, 0, 10, 10);
        var pieces = SelectionFrame.Subtract(a, new ScreenRect(3, 3, 4, 4)).ToList();
        Assert.Equal(4, pieces.Count);
        Assert.Equal(100 - 16, pieces.Sum(p => p.Width * p.Height));
        Assert.Equal(new[] { a }, SelectionFrame.Subtract(a, new ScreenRect(20, 20, 5, 5)));
        Assert.Empty(SelectionFrame.Subtract(a, new ScreenRect(-1, -1, 20, 20)));
    }

    [Fact]
    public void Wash_BlendsTheColourAtItsStrengthAndMakesOpaque()
    {
        var pixels = new byte[] { 200, 100, 0, 7, 255, 255, 255, 255 };
        ScreenImage.Wash(pixels, 0x73000000); // black at 45 % (115 of 255), the overlay's default dim
        Assert.Equal(new byte[] { 110, 55, 0, 255, 140, 140, 140, 255 }, pixels);
        var untouched = new byte[] { 1, 2, 3, 4 };
        ScreenImage.Wash(untouched, 0x00FF0000); // no strength: the picture as it was, made opaque
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, untouched);
    }

    [Fact]
    public void Union_HoldsBoth() =>
        Assert.Equal(new ScreenRect(-2, 1, 14, 11), new ScreenRect(-2, 4, 3, 3).Union(new ScreenRect(5, 1, 7, 11)));
}
