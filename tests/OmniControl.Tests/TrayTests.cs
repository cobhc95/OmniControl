using Avalonia;
using OmniControl.App.Features.States;
using OmniControl.App.Features.Tray;

namespace OmniControl.Tests;

/// The tray flyout's elements: id patterns, the defaults read from the master lists, missing ids, and where the flyout goes (no UI, no Windows).
public class TrayTests
{
    private static StateEntry Level(string id, string name) => new(id, name, "Displays", StateKind.Level, () => StateValue.Of(50), "test", Set: _ => { });

    private static StateEntry OnOff(string id, string name) => new(id, name, "Volume", StateKind.OnOff, () => StateValue.Of(true), "test");

    private static StateEntry Choice(string id) => new(id, "Default speaker", "Volume", StateKind.Choice, () => StateValue.Of("Speakers"), "test");

    private static HotkeyAction Action(string id, string title) => new(id, title, "Screenshot", string.Empty, _ => { });

    [Theory]
    [InlineData("displays.*.brightness", @"displays.\\.\DISPLAY1.brightness", true)]
    [InlineData("displays.*.brightness", "displays.laptop.brightness", true)]
    [InlineData("displays.*.brightness", "displays..brightness", false)]
    [InlineData("displays.*.brightness", "displays.laptop.contrast", false)]
    [InlineData("volume.master.level", "volume.master.level", true)]
    [InlineData("volume.master.level", "volume.master.levels", false)]
    public void Pattern_matches_any_text_for_its_star_and_only_itself_without_one(string pattern, string id, bool matches) =>
        Assert.Equal(matches, IdPattern.Matches(pattern, id));

    [Fact]
    public void Default_sliders_show_one_brightness_per_display_and_the_volume_with_its_speaker_choice()
    {
        var states = new[]
        {
            Level("displays.laptop.brightness", "Brightness (Laptop)"),
            Level("displays.laptop.contrast", "Contrast (Laptop)"),
            Level(@"displays.\\.\DISPLAY2.brightness", "Brightness (Dell)"),
            Level("volume.master.level", "Master volume"),
            Choice("volume.default.speaker"),
        };

        var sliders = TrayTargets.Sliders(TrayDefaults.Sliders, states);

        Assert.Equal(new[] { "displays.laptop.brightness", @"displays.\\.\DISPLAY2.brightness", "volume.master.level" }, sliders.Select(s => s.State!.Id));
        Assert.Equal("volume.default.speaker", sliders[2].Choice?.Id);
        Assert.Null(sliders[0].Choice);
    }

    [Fact]
    public void Missing_ids_and_states_of_the_wrong_kind_are_simply_not_shown()
    {
        var states = new[] { OnOff("volume.master.level", "Not a level"), OnOff("volume.mic.muted", "Microphone muted") };
        var actions = new[] { Action("screenshot.area", "Screenshot: area") };
        HotkeyAction? Find(string id) => actions.FirstOrDefault(a => a.Id == id);

        Assert.Empty(TrayTargets.Sliders(TrayDefaults.Sliders, states));
        var tiles = TrayTargets.Toggles(TrayDefaults.Tiles, states, actions, Find);
        Assert.Equal(new[] { "Microphone muted", "Screenshot: area" }, tiles.Select(t => t.Name).OrderBy(n => n));
    }

    [Fact]
    public void Default_tiles_give_one_tile_per_smart_device_in_the_defaults_order()
    {
        var states = new[] { OnOff("volume.master.fx", "FX"), OnOff("devices.a.power", "L530"), OnOff("devices.b.power", "Orbit 11") };
        HotkeyAction? Find(string id) => null;

        var tiles = TrayTargets.Toggles(TrayDefaults.Tiles, states, Array.Empty<HotkeyAction>(), Find);

        Assert.Equal(new[] { "FX", "L530", "Orbit 11" }, tiles.Select(t => t.Name));
    }

    [Fact]
    public void A_menu_toggle_finds_its_action_and_the_state_that_ticks_it()
    {
        var states = new[] { OnOff("automation.paused", "All rules paused") };
        var actions = new[] { Action("automation.pause", "Pause all rules") };
        HotkeyAction? Find(string id) => actions.FirstOrDefault(a => a.Id == id);

        var items = TrayDefaults.Menu.SelectMany(group => TrayTargets.Toggles(group, states, actions, Find)).ToList();

        var pause = Assert.Single(items);
        Assert.Equal("Pause all rules", pause.Name);
        Assert.Equal("automation.paused", pause.State?.Id);
    }

    [Fact]
    public void Flyout_sits_above_a_bottom_taskbar_and_inside_the_working_area()
    {
        var work = new PixelRect(0, 0, 1920, 1040);
        var at = TrayPlacement.Place(new PixelPoint(1900, 1060), work, new PixelSize(300, 400), 12);
        Assert.Equal(new PixelPoint(1920 - 300 - 12, 1040 - 400 - 12), at);
    }

    [Fact]
    public void Flyout_sits_beside_a_left_taskbar()
    {
        var work = new PixelRect(60, 0, 1860, 1080);
        var at = TrayPlacement.Place(new PixelPoint(30, 900), work, new PixelSize(300, 400), 12);
        Assert.Equal(new PixelPoint(72, 500), at);
    }
}
