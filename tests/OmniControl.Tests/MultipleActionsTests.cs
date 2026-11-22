using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Tests;

/// Several actions per input: storage (and old files), running order, the gesture takeover, and which actions may come second.
public class MultipleActionsTests
{
    private static readonly HotkeyAction[] NoOmniActions = Array.Empty<HotkeyAction>();

    [Fact]
    public void SeveralActions_SurviveASaveAndLoad()
    {
        var settings = new MouseSettings();
        settings.SetActions(1, MouseButton.ThreeFingerTap, new[] { "volume.mute", "keys:Ctrl+Shift+T|times=2", "run:C:\\a;b|c.exe" });

        var saved = JsonSerializer.Deserialize<MouseSettings>(JsonSerializer.Serialize(settings))!;
        saved.Upgrade();

        Assert.Equal(new[] { "volume.mute", "keys:Ctrl+Shift+T|times=2", "run:C:\\a;b|c.exe" }, saved.ActionsOf(1, MouseButton.ThreeFingerTap));
        Assert.Equal("volume.mute", saved.Get(1, MouseButton.ThreeFingerTap)); // the first stays where older versions read it
        Assert.Empty(saved.ActionsOf(2, MouseButton.ThreeFingerTap));
    }

    [Fact]
    public void AnOldFile_LoadsUnchanged_WithOneActionPerInput()
    {
        const string old = """{"Enabled":true,"Bindings":{"1.Middle":"keys:Ctrl+C","1.WheelUp":"volume.up"},"TrackpadLayerNames":{}}""";
        var settings = JsonSerializer.Deserialize<MouseSettings>(old)!;
        settings.Upgrade();

        Assert.Equal(new[] { "keys:Ctrl+C" }, settings.ActionsOf(1, MouseButton.Middle));
        Assert.Equal(new[] { "volume.up" }, settings.ActionsOf(1, MouseButton.WheelUp));
        Assert.Empty(settings.ExtraActions);

        var nulled = JsonSerializer.Deserialize<MouseSettings>("""{"Bindings":{"1.Middle":"keys:Ctrl+C"},"ExtraActions":null}""")!;
        nulled.Upgrade();
        Assert.Equal(new[] { "keys:Ctrl+C" }, nulled.ActionsOf(1, MouseButton.Middle));
    }

    [Fact]
    public void DefaultOrDisabledFirst_DropsTheOthers_AndTheCountIsCapped()
    {
        var settings = new MouseSettings();
        settings.SetActions(1, MouseButton.Back, new[] { "volume.mute", "media.next" });
        settings.Set(1, MouseButton.Back, MouseActionCatalog.Default);
        Assert.Empty(settings.ActionsOf(1, MouseButton.Back));
        Assert.Empty(settings.ExtraActions);

        settings.SetActions(1, MouseButton.Back, new[] { MouseActionCatalog.Disabled, "media.next" });
        Assert.Equal(new[] { MouseActionCatalog.Disabled }, settings.ActionsOf(1, MouseButton.Back));

        settings.SetActions(1, MouseButton.Back, Enumerable.Range(0, 30).Select(_ => "volume.up").ToList());
        Assert.Equal(MouseSettings.MaxActions, settings.ActionsOf(1, MouseButton.Back).Count);

        settings.SetActions(1, MouseButton.Back, Array.Empty<string>());
        Assert.Empty(settings.ActionsOf(1, MouseButton.Back));
    }

    [Fact]
    public void ClearingALayer_RemovesEveryActionOfItsInputs()
    {
        var settings = new MouseSettings();
        settings.SetActions(2, MouseButton.PinchIn, new[] { "volume.mute", "media.next" });
        settings.SetActions(2, MouseButton.Middle, new[] { "volume.mute", "media.next" });

        settings.LayersOf(InputFamily.Trackpad).Clear(2);

        Assert.Empty(settings.ActionsOf(2, MouseButton.PinchIn));
        Assert.Equal(2, settings.ActionsOf(2, MouseButton.Middle).Count);
    }

    [Fact]
    public async Task ASequence_RunsInOrder_AndStopsAtAFailure()
    {
        var ran = new List<int>();
        var log = new List<string>();
        await ActionSequence.RunAsync(new Action[] { () => ran.Add(1), () => ran.Add(2), () => ran.Add(3) }, TimeSpan.Zero, gapFirst: false, log.Add);
        Assert.Equal(new[] { 1, 2, 3 }, ran);
        Assert.Empty(log);

        ran.Clear();
        await ActionSequence.RunAsync(new Action[] { () => ran.Add(1), () => throw new InvalidOperationException("no"), () => ran.Add(3) },
            TimeSpan.FromMilliseconds(1), gapFirst: true, log.Add);
        Assert.Equal(new[] { 1 }, ran);
        Assert.Single(log);
    }

    [Fact]
    public void TheTakeover_FollowsTheFirstAction()
    {
        var settings = new MouseSettings();
        settings.SetActions(1, MouseButton.ThreeFingerTap, new[] { "desktop.switch|original=1", "volume.mute" });
        Assert.False(settings.ReplacesWindows(MouseButton.ThreeFingerTap)); // the first keeps Windows' gesture; the others do not change that

        settings.SetActions(1, MouseButton.ThreeFingerTap, new[] { "desktop.switch", "volume.mute|original=1" });
        Assert.True(settings.ReplacesWindows(MouseButton.ThreeFingerTap));

        settings.SetActions(1, MouseButton.ThreeFingerTap, Array.Empty<string>());
        Assert.False(settings.ReplacesWindows(MouseButton.ThreeFingerTap));
    }

    [Fact]
    public void LaterActions_AreOneShotOnly()
    {
        var later = MouseActionCatalog.ExtraChoices(NoOmniActions);

        Assert.DoesNotContain(later, c => c.Spec is MouseActionCatalog.Default or MouseActionCatalog.Disabled);
        var held = later.Single(c => c.Spec == MouseActionCatalog.SlowHold);
        Assert.Equal(MouseActionCatalog.OnlyFirstLasts, held.Unavailable);
        Assert.NotNull(later.Single(c => c.Spec == "layer.hold.2").Unavailable);
        Assert.Null(later.Single(c => c.Spec == "layer.next").Unavailable);
        Assert.Null(later.Single(c => c.Spec == MouseActionCatalog.SlowToggle).Unavailable);

        var shortcut = later.Single(c => c.Spec == MouseActionCatalog.Keys + ":");
        var keys = shortcut.Options!.Select(o => o.Key).ToList();
        Assert.Equal(new[] { MouseOptions.OnPress, MouseOptions.OnHold, MouseOptions.OnTwice, MouseOptions.OnThrice }, shortcut.Options!.Single(o => o.Key == MouseOptions.Trigger.Key).Choices);
        Assert.DoesNotContain(MouseOptions.RepeatEvery.Key, keys);
        Assert.DoesNotContain(MouseOptions.KeepOriginal.Key, keys);
        Assert.True(shortcut.Options!.Single(o => o.Key == MouseOptions.Times.Key).IsShown(_ => MouseOptions.OnPress)); // "times" shows for a later action's own trigger

        Assert.True(MouseActionCatalog.CanRunLater("volume.mute"));
        Assert.False(MouseActionCatalog.CanRunLater(MouseActionCatalog.WindowMove));
        Assert.False(MouseActionCatalog.CanRunLater(MouseActionCatalog.Default));

        var gestureLater = MouseActionCatalog.ExtraChoices(NoOmniActions, gesture: true);
        Assert.Contains(gestureLater, c => c.Spec == "trackpad.layer.next");
        Assert.DoesNotContain(gestureLater.Single(c => c.Spec == "media.next").Options ?? Array.Empty<ActionOption>(),
            o => o.Key == MouseOptions.KeepWindowsGesture.Key);
    }
}
