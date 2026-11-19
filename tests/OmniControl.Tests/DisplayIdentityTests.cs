using OmniControl.Core.Models;

namespace OmniControl.Tests;

/// A display is found again by its device path, so a renumbering never sends a turn to another screen; and the screen power actions keep their ids.
public class DisplayIdentityTests
{
    private static DisplayInfo Info(string name, string model, string path) => new(name, model, path, false, false);

    [Fact]
    public void FollowsTheMonitorWhenWindowsRenumbers()
    {
        var left = Info(@"\.\DISPLAY2", "Left", @"\?\path-left");
        var right = Info(@"\.\DISPLAY3", "Right", @"\?\path-right");
        var after = new[] { Info(@"\.\DISPLAY1", "Right", @"\?\path-right"), Info(@"\.\DISPLAY4", "Left", @"\?\PATH-LEFT") };
        Assert.Equal(@"\.\DISPLAY4", DisplayIdentity.Resolve(left, after));
        Assert.Equal(@"\.\DISPLAY1", DisplayIdentity.Resolve(right, after));
    }

    [Fact]
    public void NeverFallsBackToTheOldNameWhenThePathIsGone()
    {
        var gone = Info(@"\.\DISPLAY2", "Left", @"\?\path-left");
        var now = new[] { Info(@"\.\DISPLAY2", "Other", @"\?\path-other") };
        Assert.Null(DisplayIdentity.Resolve(gone, now));
    }

    [Fact]
    public void WithoutAPathNeedsTheSameNameAndModel()
    {
        var known = Info(@"\.\DISPLAY2", "Left", "");
        Assert.Equal(@"\.\DISPLAY2", DisplayIdentity.Resolve(known, new[] { Info(@"\.\DISPLAY2", "Left", "") }));
        Assert.Null(DisplayIdentity.Resolve(known, new[] { Info(@"\.\DISPLAY2", "Different", "") }));
        Assert.Null(DisplayIdentity.Resolve(known, Array.Empty<DisplayInfo>()));
    }

    [Fact]
    public void ScreenPowerOffActionsKeepTheirIds()
    {
        Assert.Equal("displays.power.off", OnOffFamily.SiblingId("displays.power", false));
        Assert.Equal("displays.screens.off", OnOffFamily.SiblingId("displays.screens", false));
    }
}
