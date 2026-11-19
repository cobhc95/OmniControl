using OmniControl.Core;
using OmniControl.Core.Engine.Trackpad;

namespace OmniControl.Tests;

/// The takeover's safety net: closing a panel Windows opened right after a gesture, against a fake window probe (nothing is pressed).
public class ShellSurfaceGuardTests
{
    private static readonly IntPtr Editor = new(100), Search = new(200), TaskView = new(300), Other = new(400);

    private sealed class FakeProbe : IShellSurfaceProbe
    {
        public IntPtr Front = Editor;
        public readonly List<IntPtr> Dismissed = new();

        public IntPtr Foreground() => Front;

        public ShellSurface SurfaceOf(IntPtr window) =>
            window == Search ? ShellSurface.Search : window == TaskView ? ShellSurface.TaskView : ShellSurface.None;

        public bool Dismiss(IntPtr window)
        {
            if (window != Front) return false;
            Dismissed.Add(window);
            Front = Editor;
            return true;
        }
    }

    private static readonly ShellSurface TapOpens = ShellSurface.Search | ShellSurface.NotificationCentre;

    private DateTime _now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private ShellSurfaceGuard Guard(FakeProbe probe, List<string>? log = null) => new(probe, () => _now) { Log = log is null ? null : log.Add };

    [Fact]
    public void APanelThatAppearsWithinTheWatch_IsClosed()
    {
        var probe = new FakeProbe();
        var log = new List<string>();
        var guard = Guard(probe, log);

        Assert.True(guard.Arm("Three-finger tap", TapOpens));
        Assert.True(guard.Look()); // nothing yet
        _now = _now.AddMilliseconds(150);
        probe.Front = Search;

        Assert.False(guard.Look());
        Assert.Equal(new[] { Search }, probe.Dismissed);
        Assert.Contains(log, l => l.Contains("Search") && l.Contains("closed it"));
    }

    [Fact]
    public void APanelThatAppearsAfterTheWatch_IsLeftAlone()
    {
        var probe = new FakeProbe();
        var guard = Guard(probe);
        guard.Arm("Three-finger tap", TapOpens);

        _now = _now.AddMilliseconds(CoreTiming.TrackpadSurfaceWatchMs + 1);
        probe.Front = Search; // the user opened search on purpose a moment later

        Assert.False(guard.Look());
        Assert.False(guard.Look());
        Assert.Empty(probe.Dismissed);
    }

    [Fact]
    public void APanelAlreadyInFrontBeforeTheGesture_IsNeverWatched()
    {
        var probe = new FakeProbe { Front = Search };
        var guard = Guard(probe);

        Assert.False(guard.Arm("Three-finger tap", TapOpens));
        Assert.False(guard.Look());
        Assert.Empty(probe.Dismissed);
    }

    [Fact]
    public void OtherWindows_AndOtherPanels_AreLeftAlone()
    {
        var probe = new FakeProbe();
        var guard = Guard(probe);
        guard.Arm("Three-finger tap", TapOpens);

        probe.Front = Other; // the action switched windows
        Assert.True(guard.Look());
        probe.Front = TaskView; // not a panel the tap opens
        Assert.True(guard.Look());
        Assert.Empty(probe.Dismissed);

        probe.Front = Search; // still within the watch: Windows' own tap
        Assert.False(guard.Look());
        Assert.Equal(new[] { Search }, probe.Dismissed);
    }

    [Fact]
    public void AGestureWithoutPanels_IsNotWatched() =>
        Assert.False(Guard(new FakeProbe()).Arm("Pinch in", ShellSurface.None));

    [Fact]
    public void OnlyOneLook_ClosesThePanel()
    {
        var probe = new FakeProbe();
        var guard = Guard(probe);
        guard.Arm("Three-finger tap", TapOpens);
        probe.Front = Search;
        probe.Dismissed.Clear();

        guard.Look();
        probe.Front = Search; // a second look that saw the same panel (two loops) finds the watch gone
        guard.Look();

        Assert.Single(probe.Dismissed);
    }

    [Fact]
    public void ClosesAreLimitedPerMinute()
    {
        var probe = new FakeProbe();
        var log = new List<string>();
        var guard = Guard(probe, log);

        for (var i = 0; i < CoreTiming.TrackpadSurfaceDismissLimit + 3; i++)
        {
            guard.Arm("Three-finger tap", TapOpens);
            probe.Front = Search;
            guard.Look();
            probe.Front = Editor;
            _now = _now.AddMilliseconds(100);
        }

        Assert.Equal(CoreTiming.TrackpadSurfaceDismissLimit, probe.Dismissed.Count);
        Assert.Single(log, l => l.Contains("left open"));

        _now = _now.AddMinutes(1);
        guard.Arm("Three-finger tap", TapOpens);
        probe.Front = Search;
        guard.Look();
        Assert.Equal(CoreTiming.TrackpadSurfaceDismissLimit + 1, probe.Dismissed.Count);
    }

    [Fact]
    public void TheMasterListKnowsWindowsPanels()
    {
        Assert.Equal(ShellSurface.Search, ShellSurfaces.Of("SearchHost", "Windows.UI.Core.CoreWindow"));
        Assert.Equal(ShellSurface.TaskView, ShellSurfaces.Of("explorer", "XamlExplorerHostIslandWindow"));
        Assert.Equal(ShellSurface.None, ShellSurfaces.Of("explorer", "CabinetWClass")); // a File Explorer window is never closed
        Assert.Equal(ShellSurface.None, ShellSurfaces.Of("notepad", "Notepad"));
    }

    [Fact]
    public void OnlyTheGesturesThatOpenAPanel_SayWhichOne()
    {
        Assert.Equal(TapOpens, TrackpadGestures.Of(OmniControl.Core.Engine.Mouse.MouseButton.ThreeFingerTap)!.Opens);
        Assert.Equal(ShellSurface.TaskView, TrackpadGestures.Of(OmniControl.Core.Engine.Mouse.MouseButton.ThreeFingerUp)!.Opens);
        Assert.Equal(ShellSurface.None, TrackpadGestures.Of(OmniControl.Core.Engine.Mouse.MouseButton.PinchIn)!.Opens);
    }
}
