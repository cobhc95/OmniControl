using OmniControl.App.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Tests;

/// <summary>When a trackpad gesture's action replaces Windows' own gesture, and how the "also keep the original" setting reads per input.</summary>
public class GestureOptionTests
{
    private static MouseSettings With(string key, string spec) => new() { Bindings = { [key] = spec } };

    [Fact]
    public void AGestureAtDefault_NeverReplacesWindows()
    {
        Assert.False(new MouseSettings().ReplacesWindows(MouseButton.ThreeFingerTap));
        Assert.False(With("1.ThreeFingerTap", MouseActionCatalog.Default).ReplacesWindows(MouseButton.ThreeFingerTap));
    }

    [Fact]
    public void ARemappedGesture_ReplacesWindows_AlsoWhenStoredByAnOlderVersionWithoutTheOption()
    {
        Assert.True(With("1.ThreeFingerTap", "keys:Alt+Tab").ReplacesWindows(MouseButton.ThreeFingerTap));
        Assert.True(With("1.ThreeFingerTap", MouseActionCatalog.Disabled).ReplacesWindows(MouseButton.ThreeFingerTap));
        Assert.False(With("1.ThreeFingerTap", "keys:Alt+Tab").ReplacesWindows(MouseButton.ThreeFingerUp));
    }

    [Fact]
    public void TheKeepOriginalOption_LeavesWindowsAlone()
    {
        var settings = With("1.PinchIn", "keys:Ctrl+Minus|original=1");
        Assert.False(settings.ReplacesWindows(MouseButton.PinchIn));

        settings.Bindings["2.PinchIn"] = "keys:Ctrl+Minus"; // another layer without it still replaces
        Assert.True(settings.ReplacesWindows(MouseButton.PinchIn));
    }

    [Fact]
    public void TheOptionIsOneSetting_WordedByTheKindOfInput()
    {
        var gesture = MouseActionCatalog.Choices(Array.Empty<OmniControl.App.Hotkeys.HotkeyAction>(), hasRelease: false, gesture: true);
        var button = MouseActionCatalog.Choices(Array.Empty<OmniControl.App.Hotkeys.HotkeyAction>());

        foreach (var choice in gesture.Where(c => c.Spec != MouseActionCatalog.Default && c.Spec != MouseActionCatalog.Disabled && c.Options is not null))
            Assert.Contains(choice.Options!, o => o.Key == MouseOptions.KeepOriginal.Key && o.Label == MouseOptions.KeepWindowsGesture.Label);
        Assert.DoesNotContain(gesture.SelectMany(c => c.Options ?? Array.Empty<ActionOption>()), o => o.Label == MouseOptions.KeepOriginal.Label);
        Assert.Contains(button.SelectMany(c => c.Options ?? Array.Empty<ActionOption>()), o => o.Label == MouseOptions.KeepOriginal.Label);
        Assert.Equal(MouseOptions.KeepOriginal.Key, MouseOptions.KeepWindowsGesture.Key);
    }

    [Fact]
    public void TheTakeoverIsAlwaysOn_ForARemappedGesture_EvenWhenAnOldFileTurnedTheRemovedSwitchOff()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<MouseSettings>(
            """{"Enabled":true,"TakeOverWindowsGestures":false,"Bindings":{"1.ThreeFingerTap":"keys:Ctrl+C","1.PinchIn":"keys:Ctrl+Minus|original=1"}}""")!;
        old.Upgrade();

        var wanted = TrackpadService.WantedOff(old);

        Assert.Contains(OmniControl.Core.Engine.Trackpad.WindowsGesture.ThreeFingerTap, wanted);
        Assert.DoesNotContain(OmniControl.Core.Engine.Trackpad.WindowsGesture.PinchToZoom, wanted); // its action keeps Windows' own
        Assert.DoesNotContain(OmniControl.Core.Engine.Trackpad.WindowsGesture.ThreeFingerSwipes, wanted); // at Default
        old.Enabled = false;
        Assert.Empty(TrackpadService.WantedOff(old)); // "Remap mouse and trackpad" off gives everything back
    }

    [Fact]
    public void TheSafetyNet_WatchesOnlyARemappedGesture_WithoutTheOption_OnTheLayerInForce()
    {
        var settings = With("1.ThreeFingerTap", "keys:Ctrl+C");
        Assert.True(TrackpadService.Guards(settings, 1, MouseButton.ThreeFingerTap));
        Assert.False(TrackpadService.Guards(settings, 2, MouseButton.ThreeFingerTap)); // Default on layer 2
        Assert.False(TrackpadService.Guards(new MouseSettings(), 1, MouseButton.ThreeFingerTap)); // Default

        Assert.False(TrackpadService.Guards(With("1.ThreeFingerTap", "keys:Ctrl+C|original=1"), 1, MouseButton.ThreeFingerTap)); // option ticked
        Assert.False(TrackpadService.Guards(With("1.ThreeFingerTap", "desktop.search"), 1, MouseButton.ThreeFingerTap)); // opens Search itself
        Assert.False(TrackpadService.Guards(With("1.ThreeFingerUp", "keys:Alt+Tab"), 1, MouseButton.ThreeFingerUp)); // the switcher looks like Task view

        settings.Enabled = false;
        Assert.False(TrackpadService.Guards(settings, 1, MouseButton.ThreeFingerTap));
    }

    [Fact]
    public void EveryTrackpadGesture_IsRecognisedAsOne() =>
        Assert.All(OmniControl.Core.Engine.Trackpad.TrackpadGestures.All, g => Assert.True(g.Button.IsTrackpadGesture()));
}
