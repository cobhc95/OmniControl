using OmniControl.App.Features.Screenshot;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.Tests;

/// Snapping the screenshot selection to window edges (docs/mockups/screenshot-snap.html): hidden edge parts never snap, an edge snaps only
/// when it runs alongside the selection, and a click finds the front-most window. Two windows: Browser in front of Notes.
public class WindowSnapTests
{
    private const int Reach = 8;
    private static readonly ScreenRect Screen = new(0, 0, 640, 360);
    private static readonly DesktopWindow Browser = new(new ScreenRect(260, 120, 330, 210), "Browser");
    private static readonly DesktopWindow Notes = new(new ScreenRect(40, 30, 300, 200), "Notes");

    private static DesktopLayout Layout() => SnapEdges.Build(new[] { Browser, Notes }, new[] { Screen }, 8);

    [Fact]
    public void Hidden_parts_of_an_edge_are_left_out()
    {
        var layout = Layout();
        // Notes' right edge (x 340) is behind Browser from y 120 down, so only y 30..120 is kept
        Assert.Contains(new SnapEdge(340, 30, 120), layout.Vertical);
        Assert.DoesNotContain(layout.Vertical, e => e.At == 340 && e.End > 120);
        // Notes' bottom edge (y 230) is kept only left of Browser
        Assert.Contains(new SnapEdge(230, 40, 260), layout.Horizontal);
    }

    [Fact]
    public void A_window_wholly_behind_another_is_not_a_target()
    {
        var hidden = new DesktopWindow(new ScreenRect(300, 150, 50, 50), "Hidden");
        var layout = SnapEdges.Build(new[] { Browser, hidden }, Array.Empty<ScreenRect>(), 8);
        Assert.DoesNotContain(hidden, layout.Windows);
    }

    [Fact]
    public void A_dragged_corner_snaps_to_a_near_edge()
    {
        var guides = new List<SnapGuide>();
        var sel = new WindowSnap(Layout()).Drag(300, 200, 585, 325, Screen, Reach, guides);
        Assert.Equal(590, sel.Right);
        Assert.Equal(330, sel.Bottom);
        Assert.Contains(guides, g => g.Vertical && g.At == 590);
    }

    [Fact]
    public void An_edge_that_does_not_run_alongside_the_selection_does_not_pull()
    {
        var guides = new List<SnapGuide>();
        // x 337 is near Notes' right edge (x 340), but that edge's visible part ends at y 120 and the selection is far below it
        var sel = new WindowSnap(Layout()).Drag(100, 250, 337, 300, Screen, Reach, guides);
        Assert.Equal(337, sel.Right);
        Assert.Empty(guides);
    }

    [Fact]
    public void Moving_keeps_the_size_and_snaps_the_nearer_side()
    {
        var guides = new List<SnapGuide>();
        var start = new ScreenRect(300, 200, 100, 80);
        var sel = new WindowSnap(Layout()).Move(start, -36, 0, Screen, Reach, guides); // left edge lands at 264, 4 from Browser's 260
        Assert.Equal(260, sel.X);
        Assert.Equal((100, 80), (sel.Width, sel.Height));
    }

    [Fact]
    public void Resizing_snaps_only_the_dragged_edge()
    {
        var guides = new List<SnapGuide>();
        var start = new ScreenRect(300, 200, 100, 80);
        var sel = new WindowSnap(Layout()).Resize(start, Grip.E, 186, 0, Screen, Reach, guides); // right edge to 586
        Assert.Equal(590, sel.Right);
        Assert.Equal(300, sel.X);
    }

    [Fact]
    public void A_point_finds_the_front_most_window()
    {
        var snap = new WindowSnap(Layout());
        Assert.Equal(Browser, snap.WindowAt(300, 150)); // inside both: Browser is in front
        Assert.Equal(Notes, snap.WindowAt(60, 60));
        Assert.Null(snap.WindowAt(620, 20));
    }

    [Fact]
    public void A_monitor_sees_its_own_part_in_picture_pixels()
    {
        var right = Layout().Within(new ScreenRect(320, 0, 320, 360));
        Assert.Contains(right.Windows, w => w.Name == "Browser" && w.Frame == new ScreenRect(0, 120, 270, 210));
        Assert.Contains(new SnapEdge(270, 120, 330), right.Vertical);
    }
}
