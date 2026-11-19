using OmniControl.Core.Engine.Mouse;
using OmniControl.Core.Engine.Trackpad;

namespace OmniControl.Tests;

/// Taps, double taps and swipes read from synthetic touchpad frames (no touchpad needed).
public class GestureRecogniserTests
{
    private static TouchContact[] Fingers(int count, float dx = 0, float dy = 0)
    {
        var c = new TouchContact[count];
        for (var i = 0; i < count; i++) c[i] = new TouchContact { X = 0.3f + 0.1f * i + dx, Y = 0.3f + dy, Touching = true };
        return c;
    }

    private static TrackpadGesture? Tap(GestureRecogniser r, int fingers, long start, long length = 100)
    {
        r.Feed(Fingers(fingers), start);
        r.Feed(Fingers(fingers), start + length / 2);
        return r.Feed(Array.Empty<TouchContact>(), start + length);
    }

    [Theory]
    [InlineData(1, MouseButton.OneFingerTap)]
    [InlineData(2, MouseButton.TwoFingerTap)]
    [InlineData(3, MouseButton.ThreeFingerTap)]
    public void QuickStillTouch_IsATap(int fingers, MouseButton expected) =>
        Assert.Equal(expected, Tap(new GestureRecogniser(TimeSpan.FromMilliseconds(300)), fingers, 1000)?.Button);

    [Fact]
    public void SlowTouch_IsNotATap() =>
        Assert.Null(Tap(new GestureRecogniser(TimeSpan.FromMilliseconds(300)), 1, 1000, 400));

    [Fact]
    public void MovingTouch_IsNotATap()
    {
        var r = new GestureRecogniser(TimeSpan.FromMilliseconds(300));
        r.Feed(Fingers(1), 1000);
        r.Feed(Fingers(1, 0.05f), 1050);
        Assert.Null(r.Feed(Array.Empty<TouchContact>(), 1100));
    }

    [Fact]
    public void FingersLandingFarApart_AreNotOneTap()
    {
        var r = new GestureRecogniser(TimeSpan.FromMilliseconds(300));
        r.Feed(Fingers(1), 1000);
        r.Feed(Fingers(2), 1150);
        Assert.Null(r.Feed(Array.Empty<TouchContact>(), 1200));
    }

    [Fact]
    public void TwoTapsOfTheSameFingers_GiveADoubleTapInsteadOfTheSecondTap()
    {
        var r = new GestureRecogniser(TimeSpan.FromMilliseconds(300));
        Assert.Equal(MouseButton.TwoFingerTap, Tap(r, 2, 1000)?.Button);
        Assert.Equal(MouseButton.TwoFingerDoubleTap, Tap(r, 2, 1200)?.Button);
        Assert.Equal(MouseButton.TwoFingerTap, Tap(r, 2, 1400)?.Button); // the pair was used up
    }

    [Fact]
    public void TwoTapsWithDifferentFingers_AreNotADoubleTap()
    {
        var r = new GestureRecogniser(TimeSpan.FromMilliseconds(300));
        Tap(r, 1, 1000);
        Assert.Equal(MouseButton.TwoFingerTap, Tap(r, 2, 1200)?.Button);
    }

    [Fact]
    public void Swipe_StillWorks_AndIsNotAlsoATap()
    {
        var r = new GestureRecogniser(TimeSpan.FromMilliseconds(300));
        r.Feed(Fingers(3), 1000);
        Assert.Equal(MouseButton.ThreeFingerRight, r.Feed(Fingers(3, 0.2f), 1050)?.Button);
        Assert.Null(r.Feed(Array.Empty<TouchContact>(), 1100));
    }

    [Fact]
    public void Pinch_StillWorks_AndIsNotAlsoATap()
    {
        var r = new GestureRecogniser(TimeSpan.FromMilliseconds(300));
        r.Feed(Fingers(2), 1000);
        var wide = new[] { new TouchContact { X = 0.2f, Y = 0.3f, Touching = true }, new TouchContact { X = 0.6f, Y = 0.3f, Touching = true } };
        Assert.Equal(MouseButton.PinchOut, r.Feed(wide, 1050)?.Button);
        Assert.Null(r.Feed(Array.Empty<TouchContact>(), 1100));
    }
}
