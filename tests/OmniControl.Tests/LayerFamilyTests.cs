using System.Text.Json;
using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Rules;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Tests;

/// <summary>The mouse and the trackpad each have their own layers: storage, the upgrade of an old mouse.json, the layer in force, the action lists.</summary>
public class LayerFamilyTests
{
    private const string OldFile = """
        {"Enabled":true,"TrackpadEnabled":false,"TakeOverWindowsGestures":true,
         "Bindings":{"1.Middle":"keys:Ctrl+C","1.ThreeFingerTap":"keys:Alt+Tab","2.PinchIn":"layer.next|wrap=1","2.Back":"layer.set.3","3.FourFingerUp":"layer.set.2"},
         "LayerNames":{"2":"Zoom","5":"Games"},"OnlyUsedLayers":false}
        """;

    private static MouseSettings LoadOld()
    {
        var settings = JsonSerializer.Deserialize<MouseSettings>(OldFile)!;
        settings.Upgrade();
        return settings;
    }

    [Fact]
    public void AnOldFile_KeepsEveryMapping_AndGivesTheTrackpadItsOwnLayers()
    {
        var settings = LoadOld();

        Assert.Equal("keys:Ctrl+C", settings.Get(1, MouseButton.Middle));
        Assert.Equal("keys:Alt+Tab", settings.Get(1, MouseButton.ThreeFingerTap));
        Assert.Equal("layer.set.3", settings.Get(2, MouseButton.Back)); // a button's layer action still moves the mouse's layer
        Assert.Equal("trackpad.layer.next|wrap=1", settings.Get(2, MouseButton.PinchIn)); // a gesture's now moves the trackpad's
        Assert.Equal("trackpad.layer.set.2", settings.Get(3, MouseButton.FourFingerUp));

        var trackpad = settings.LayersOf(InputFamily.Trackpad);
        Assert.Null(trackpad.NameOf(2)); // the mouse's names are never copied
        Assert.Null(trackpad.NameOf(5));
        Assert.True(trackpad.OnlyUsed);
        Assert.False(settings.LayersOf(InputFamily.Mouse).OnlyUsed);
        Assert.Equal("Games", settings.LayersOf(InputFamily.Mouse).NameOf(5));
    }

    [Fact]
    public void TheUpgrade_RunsOnce()
    {
        var settings = LoadOld();
        settings.Bindings["4.PinchOut"] = "layer.next"; // stored after the upgrade on purpose: not rewritten again
        var saved = JsonSerializer.Deserialize<MouseSettings>(JsonSerializer.Serialize(settings))!;
        saved.Upgrade();

        Assert.Equal("layer.next", saved.Get(4, MouseButton.PinchOut));
        Assert.Null(saved.LayersOf(InputFamily.Trackpad).NameOf(2));
    }

    [Fact]
    public void AnInheritedTrackpadName_IsReset_OnceOnly()
    {
        var settings = JsonSerializer.Deserialize<MouseSettings>(
            """{"LayerNames":{"1":"Laptop","2":"Zoom"},"TrackpadLayerNames":{"1":"Laptop","2":"Pinch"}}""")!;
        settings.Upgrade();
        var trackpad = settings.LayersOf(InputFamily.Trackpad);
        Assert.Null(trackpad.NameOf(1));
        Assert.Equal("Trackpad 1", trackpad.Labels()[1]);
        Assert.Equal("Pinch", trackpad.NameOf(2));

        trackpad.SetName(1, "Laptop"); // typed later: kept
        settings.Upgrade();
        Assert.Equal("Laptop", trackpad.NameOf(1));
        Assert.Equal("Laptop", settings.LayersOf(InputFamily.Mouse).NameOf(1));
    }

    [Fact]
    public void TheTrackpad_ListsThreeLayersByDefault_TheMouseDoesNot()
    {
        var settings = new MouseSettings();
        settings.Upgrade();
        var trackpad = settings.LayersOf(InputFamily.Trackpad);
        Assert.Equal(new[] { 1, 2, 3 }, trackpad.Listed());
        Assert.Equal(new[] { "Trackpad 1", "Trackpad 2", "Trackpad 3" }, new[] { 1, 2, 3 }.Select(n => trackpad.Labels()[n]));
        Assert.Equal("Layer 4", trackpad.Labels()[4]);
        Assert.Equal(new[] { 1 }, settings.LayersOf(InputFamily.Mouse).Listed());
        Assert.Equal("Layer 2", settings.LayersOf(InputFamily.Mouse).Labels()[2]);
    }

    [Fact]
    public void ClearingATrackpadName_RestoresTheDefault()
    {
        var settings = new MouseSettings();
        settings.Upgrade();
        var trackpad = settings.LayersOf(InputFamily.Trackpad);
        trackpad.SetName(2, "Desktops");
        Assert.Equal("Desktops", trackpad.Labels()[2]);
        trackpad.SetName(2, "  ");
        Assert.Equal("Trackpad 2", trackpad.Labels()[2]);
        Assert.Equal("Trackpad 2", trackpad.DefaultNameOf(2));
    }

    [Fact]
    public void ANewFile_HasEmptyTrackpadLayers()
    {
        var settings = new MouseSettings();
        settings.Upgrade();
        Assert.NotNull(settings.TrackpadLayerNames);
        Assert.Empty(settings.TrackpadLayerNames!);
    }

    [Fact]
    public void LayerSets_CountNameAndClearOnlyTheirOwnInputs()
    {
        var settings = LoadOld();
        var mouse = settings.LayersOf(InputFamily.Mouse);
        var trackpad = settings.LayersOf(InputFamily.Trackpad);

        Assert.Equal(1, mouse.MappingCount(1));
        Assert.Equal(1, trackpad.MappingCount(1));
        Assert.Equal(1, mouse.MappingCount(2));
        Assert.Equal(1, trackpad.MappingCount(2));

        trackpad.Clear(1);
        Assert.Null(settings.Get(1, MouseButton.ThreeFingerTap));
        Assert.Equal("keys:Ctrl+C", settings.Get(1, MouseButton.Middle));

        trackpad.SetName(3, "Desktops");
        Assert.Equal("Desktops", trackpad.Labels()[3]);
        Assert.Equal("Layer 3", mouse.Labels()[3]);
        Assert.Contains(3, trackpad.Listed());
        Assert.DoesNotContain(4, trackpad.Listed());
    }

    [Fact]
    public void TheTakeover_LooksAtEveryTrackpadLayer()
    {
        var settings = new MouseSettings { Bindings = { ["7.ThreeFingerTap"] = "keys:Alt+Tab", ["3.PinchIn"] = "keys:Ctrl+Minus|original=1" } };
        Assert.True(settings.ReplacesWindows(MouseButton.ThreeFingerTap));
        Assert.False(settings.ReplacesWindows(MouseButton.PinchIn));
    }

    [Fact]
    public void TheLayerInForce_IsKeptPerFamily()
    {
        var state = new LayerState();
        state.Choose(InputFamily.Mouse, 3);
        Assert.Equal(3, state.Current(InputFamily.Mouse));
        Assert.Equal(1, state.Current(InputFamily.Trackpad));

        state.Hold(MouseButton.Back, InputFamily.Trackpad, 4); // "Hold for trackpad layer 4" on a mouse button
        Assert.Equal(4, state.Current(InputFamily.Trackpad));
        Assert.Equal(3, state.Current(InputFamily.Mouse));
        Assert.Equal(InputFamily.Trackpad, state.Release(MouseButton.Back));
        Assert.Null(state.Release(MouseButton.Back));
        Assert.Equal(1, state.Current(InputFamily.Trackpad));

        state.Step(InputFamily.Trackpad, -1, wrap: true);
        Assert.Equal(MouseSettings.Layers, state.Current(InputFamily.Trackpad));
        Assert.Equal(3, state.Current(InputFamily.Mouse));
    }

    [Fact]
    public void EachCard_OffersItsOwnFamilysLayerActions()
    {
        var none = Array.Empty<HotkeyAction>();
        var gesture = MouseActionCatalog.Choices(none, hasRelease: false, gesture: true)
            .Select(c => MouseActionCatalog.LayerActionOf(c.Spec)).OfType<LayerAction>().ToList();
        var button = MouseActionCatalog.Choices(none)
            .Select(c => MouseActionCatalog.LayerActionOf(c.Spec)).OfType<LayerAction>().ToList();

        Assert.NotEmpty(gesture);
        Assert.All(gesture, a => Assert.Equal(InputFamily.Trackpad, a.Family));
        Assert.DoesNotContain(gesture, a => a.Move == LayerMove.Hold); // a gesture can't be held
        Assert.Contains(button, a => a is { Family: InputFamily.Mouse, Move: LayerMove.Next });
        Assert.All(button.Where(a => a.Family == InputFamily.Trackpad), a => Assert.Equal(LayerMove.Hold, a.Move));
    }

    [Fact]
    public void LayerActionIds_ReadBack()
    {
        foreach (var family in new[] { InputFamily.Mouse, InputFamily.Trackpad })
        foreach (var move in Enum.GetValues<LayerMove>())
        {
            var layer = move is LayerMove.Switch or LayerMove.Hold ? 4 : 0;
            Assert.Equal(new LayerAction(family, move, layer), MouseActionCatalog.LayerActionOf(MouseActionCatalog.LayerActionId(family, move, layer)));
        }
        Assert.Null(MouseActionCatalog.LayerActionOf("keys:Ctrl+C"));
        Assert.Equal("layer.next", MouseActionCatalog.LayerActionId(InputFamily.Mouse, LayerMove.Next)); // ids stored by older versions
        Assert.Equal("layer.hold.2", MouseActionCatalog.LayerActionId(InputFamily.Mouse, LayerMove.Hold, 2));
    }

    [Fact]
    public void RulesWithATrackpadTrigger_UseTheTrackpadsLayers()
    {
        Assert.Equal(InputFamily.Trackpad, RuleTriggers.FamilyOf("trackpad.three.up"));
        Assert.All(RuleTriggers.All.Where(t => t.Category == "Trackpad"), t => Assert.Equal(InputFamily.Trackpad, t.Family));
        Assert.Equal(InputFamily.Mouse, RuleTriggers.FamilyOf("click.middle"));
        Assert.Equal(InputFamily.Mouse, RuleTriggers.FamilyOf("key:Ctrl+Alt+K"));
        Assert.Equal(InputFamily.Mouse, RuleTriggers.FamilyOf(string.Empty));
    }
}
