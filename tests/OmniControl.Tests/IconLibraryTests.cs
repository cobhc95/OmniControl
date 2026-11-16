using Avalonia;
using OmniControl.App.Icons;
using OmniControl.App.Services;
using OmniControl.App.States;
using OmniControl.App.Tray;
using Xunit;

namespace OmniControl.Tests;

/// <summary>The icon library's search and suggestions, the tray's drag maths and stored-order edits, and the live state re-check throttle (no UI, no Windows).</summary>
public class IconLibraryTests
{
    [Fact]
    public void Library_has_one_hundred_icons_with_unique_ids() =>
        Assert.Equal(100, IconLibrary.All.Select(i => i.Id).Distinct().Count());

    [Theory]
    [InlineData("light", "bulb")]
    [InlineData("LAMP", "lamp")]
    [InlineData("fx", "sliders")]
    [InlineData("bulb off", "bulb-off")]
    public void Search_finds_by_name_keywords_and_id_ignoring_case(string query, string expected) =>
        Assert.Contains(IconLibrary.Search(query), i => i.Id == expected);

    [Fact]
    public void Search_needs_every_word_and_empty_finds_all()
    {
        Assert.DoesNotContain(IconLibrary.Search("bulb off"), i => i.Id == "bulb");
        Assert.Equal(IconLibrary.All.Count, IconLibrary.Search(" ").Count);
        Assert.Empty(IconLibrary.Search("zzqq"));
    }

    [Fact]
    public void Earlier_values_read_as_their_library_icons()
    {
        Assert.Equal(IconLibrary.Sliders, IconLibrary.Current("FxIcon"));
        Assert.Equal("strip", IconLibrary.Find("DeviceStripIcon")?.Id);
        Assert.Equal("user:abc", IconLibrary.Current("user:abc"));
    }

    [Fact]
    public void Default_is_the_first_known_suggestion_else_the_own_icon()
    {
        Assert.Equal("sun", IconLibrary.DefaultOf(new[] { "nope", "sun" }, "moon"));
        Assert.Equal("moon", IconLibrary.DefaultOf(null, "moon"));
        Assert.Equal(IconLibrary.Mic, IconLibrary.DefaultOf(Array.Empty<string>(), "MicIcon"));
        Assert.Null(IconLibrary.DefaultOf(null, null));
    }

    [Fact]
    public void Suggestions_fall_back_to_own_icon_then_name_words_at_most_three()
    {
        Assert.Equal(new[] { "sun", "moon" }, IconLibrary.Suggestions(new[] { "sun" }, "moon", null));
        var fromName = IconLibrary.Suggestions(null, null, "Kitchen lamp");
        Assert.Contains("lamp", fromName);
        Assert.True(IconLibrary.Suggestions(null, "bulb", "Volume up and down").Count <= IconLibrary.SuggestionCount);
        Assert.Empty(IconLibrary.Suggestions(null, null, "zz"));
    }

    [Fact]
    public void Svg_shapes_are_read_and_invisible_frames_skipped()
    {
        var drawing = SvgShapes.Read("""<svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="3"><path stroke="none" d="M0 0h48v48H0z" fill="none"/><circle cx="24" cy="24" r="10"/><line x1="0" y1="0" x2="4" y2="4"/></svg>""");
        Assert.NotNull(drawing);
        Assert.True(drawing!.Stroked);
        Assert.Equal(48, drawing.Width);
        Assert.DoesNotContain("h48", drawing.PathData);
        Assert.Contains("L4,4", drawing.PathData);
        Assert.Null(SvgShapes.Read("not xml"));
    }

    [Fact]
    public void Drag_lands_in_the_nearest_slot()
    {
        var slots = new[] { new Rect(0, 0, 10, 10), new Rect(20, 0, 10, 10), new Rect(0, 20, 10, 10) };
        Assert.Equal(1, Reorder.TargetSlot(slots, new Point(24, 6)));
        Assert.Equal(2, Reorder.TargetSlot(slots, new Point(4, 30)));
        Assert.Equal(-1, Reorder.TargetSlot(Array.Empty<Rect>(), new Point(0, 0)));
    }

    [Theory]
    [InlineData(0, 1, 3, 0)] // forward: the ones passed move back a place
    [InlineData(3, 1, 3, 2)]
    [InlineData(1, 1, 3, 3)]
    [InlineData(4, 1, 3, 4)]
    [InlineData(1, 3, 1, 2)] // backward: the ones passed move on a place
    [InlineData(2, 3, 1, 3)]
    [InlineData(0, 3, 1, 0)]
    public void Others_slide_aside_one_place(int i, int from, int to, int shown) => Assert.Equal(shown, Reorder.ShownSlot(i, from, to));

    private static TrayTarget Tile(TrayElement element, string stateId) =>
        new(element, null, new StateEntry(stateId, stateId, "Smart devices", StateKind.OnOff, () => null, "test"));

    [Fact]
    public void Moving_one_device_of_a_pattern_spells_the_pattern_out_and_keeps_it_last()
    {
        var fx = TrayElement.OfState("volume.master.fx");
        var devices = TrayElement.OfState("devices.*.power");
        var shown = new[] { Tile(fx, "volume.master.fx"), Tile(devices, "devices.a.power"), Tile(devices, "devices.b.power") };
        var moved = TrayOrder.Move(new[] { fx, devices }, shown, 2, 0);
        Assert.Equal(new[] { "devices.b.power", "volume.master.fx", "devices.a.power", "devices.*.power" }, moved.Select(e => e.State));
    }

    [Fact]
    public void Move_to_the_end_and_missing_elements_keep_their_place()
    {
        var (a, missing, b, c) = (TrayElement.OfAction("a"), TrayElement.OfAction("gone"), TrayElement.OfAction("b"), TrayElement.OfAction("c"));
        var shown = new[] { new TrayTarget(a, null, null), new TrayTarget(b, null, null), new TrayTarget(c, null, null) };
        Assert.Equal(new[] { "gone", "b", "c", "a" }, TrayOrder.Move(new[] { a, missing, b, c }, shown, 0, 2).Select(e => e.Action));
    }

    [Fact]
    public void Replace_keeps_the_place_and_a_chosen_icon()
    {
        var a = TrayElement.OfAction("a");
        a.Icon = "bulb";
        var b = TrayElement.OfAction("b");
        var shown = new[] { new TrayTarget(a, null, null), new TrayTarget(b, null, null) };
        var replaced = TrayOrder.Replace(new[] { a, b }, shown, 0, TrayElement.OfAction("z"));
        Assert.Equal(new[] { "z", "b" }, replaced.Select(e => e.Action));
        Assert.Equal("bulb", replaced[0].Icon);
    }

    [Fact]
    public void Removing_a_pattern_item_takes_the_pattern_out()
    {
        var devices = TrayElement.OfState("devices.*.power");
        var shown = new[] { Tile(devices, "devices.a.power"), Tile(devices, "devices.b.power") };
        Assert.Equal(new[] { "devices.b.power" }, TrayOrder.Remove(new[] { devices }, shown, 0).Select(e => e.State));
    }

    [Fact]
    public void Recheck_waits_the_shortest_gap()
    {
        var now = new DateTime(2026, 11, 16, 12, 0, 0);
        var gap = TimeSpan.FromSeconds(10);
        Assert.True(RecheckThrottle.Due(null, now, gap));
        Assert.False(RecheckThrottle.Due(now.AddSeconds(-9), now, gap));
        Assert.True(RecheckThrottle.Due(now.AddSeconds(-10), now, gap));
        Assert.True(RecheckThrottle.Due(now.AddSeconds(-15), now, gap));
    }
}
