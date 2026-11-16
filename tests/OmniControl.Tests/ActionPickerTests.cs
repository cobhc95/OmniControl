using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Picker;

namespace OmniControl.Tests;

/// <summary>The Choose-the-action window: sections and groups from the master entries, the settings an entry offers, search across sections, and old stored actions that were merged.</summary>
public class ActionPickerTests
{
    private static readonly HotkeyAction[] None = Array.Empty<HotkeyAction>();

    private static HotkeyAction Action(string id, string title, string category, string? group = null) => new(id, title, category, "", _ => { }, group: group);

    [Theory]
    [InlineData("volume.up", "omni", "volume.up")]
    [InlineData("volume.mute", "omni", "volume.mute")]
    [InlineData("desktop.lock", "omni", "automation.power.lock")]
    [InlineData("omni:automation.open", "omni", "app.show")]
    [InlineData("browser.home", "launch.browser", "")]
    [InlineData("keys:Ctrl+C", "keys", "Ctrl+C")]
    public void AnActionThatWasMerged_IsReadAsTheOneThatReplacedIt(string stored, string id, string parameter)
    {
        var action = MouseAction.Parse(stored);

        Assert.Equal(id, action.Id);
        Assert.Equal(parameter, action.Param);
        Assert.Equal(MouseActionCatalog.Migrated(stored), MouseAction.Split(stored).Action);
    }

    [Fact]
    public void AMergedActionKeepsItsSettings()
    {
        var action = MouseAction.Parse("volume.up|times=3");

        Assert.Equal("omni", action.Id);
        Assert.Equal(3, action.Options.Int(MouseOptions.Times));
    }

    [Fact]
    public void EveryBuiltInEntryHasASectionAndAShortUniqueName()
    {
        var all = MouseActionCatalog.Choices(None);

        Assert.All(all, c => Assert.False(string.IsNullOrEmpty(c.Heading), c.Spec));
        Assert.All(all, c => Assert.DoesNotContain(" - ", c.Name));
        Assert.Equal(all.Count, all.Select(c => c.Name).Distinct().Count());
        Assert.DoesNotContain(all, c => c.Spec is "volume.up" or "desktop.lock" or "browser.home");
    }

    [Fact]
    public void SectionsFollowTheirPlaceAndEntriesGroupUnderTheirHeading()
    {
        var late = new ActionSection("Zeta", "M0,0 L1,1", 2);
        var early = new ActionSection("Alpha", "M0,0 L1,1", 1);
        var choices = new[]
        {
            new PickChoice("z", "Zeta", "Z one", Heading: "Group", Place: late),
            new PickChoice("a", "Alpha", "A plain", Place: early),
            new PickChoice("b", "Alpha", "A grouped", Heading: "Group", Place: early),
            new PickChoice("c", "Alpha", "A second plain", Place: early),
        };

        var groups = PickChoice.Group(choices);
        var model = new PickerModel(groups, null, new Dictionary<string, string>()) { Noun = "trigger" };

        Assert.Equal(new[] { "Alpha", "Zeta" }, model.Sections.Select(s => s.Name));
        Assert.Equal(new[] { "A plain", "A second plain", "Group", "A grouped" }, model.Actions.Select(a => a.Text)); // those without a heading first, the heading its own row
        Assert.Equal(new[] { false, false, true, false }, model.Actions.Select(a => a.IsHeading));
        Assert.Equal(0, model.ActionIndex);
        model.Move(1);
        model.Move(1);
        Assert.Equal(3, model.ActionIndex); // the heading is skipped
        model.Move(1);
        Assert.Equal(0, model.ActionIndex); // and it still wraps
    }

    [Fact]
    public void ASearchCoversEverySection_AndNamesTheSectionOfEachMatch()
    {
        var choices = new[]
        {
            new PickChoice("a", "Displays", "Brightness up"),
            new PickChoice("b", "Volume", "Volume up"),
            new PickChoice("c", "Power", "Sleep"),
        };
        var model = new PickerModel(PickChoice.Group(choices), null, new Dictionary<string, string>()) { Noun = "trigger" };

        model.Query = "up";
        model.Settle(); // the search runs off the UI thread; a key that acts on the list takes its result at once

        Assert.Equal(new[] { "Brightness up", "Volume up" }, model.Actions.Select(a => a.Text));
        Assert.Equal(new[] { "Displays", "Volume" }, model.Actions.Select(a => a.Hint));
    }

    [Fact]
    public void TheSettingsOfTheHighlightedEntryAreAlwaysShown_AndUseGivesThemBack()
    {
        var shortcut = MouseActionCatalog.Choices(None).Single(c => c.Spec == MouseActionCatalog.Keys + ":");
        var model = new PickerModel(PickChoice.Group(new[] { shortcut }), null, new Dictionary<string, string>()) { Noun = "trigger" };

        Assert.Equal(shortcut.Name, model.SettingsTitle);
        Assert.True(model.OptionItems.Count > 1); // the shortcut text, then times and repeat
        model.OptionItems[0].Value = "Ctrl+Shift+T";

        var pick = model.Use()!;

        Assert.Equal(shortcut, pick.Choice);
        Assert.Equal("Ctrl+Shift+T", pick.Parameter);
    }

    [Fact]
    public void ALevelAction_OffersHowFarItMoves_AndAPowerActionItsCountdown()
    {
        var level = HotkeyAction.ForLevel("v.up", "Volume up", "Volume", "", new HotkeyLevel("volume", "Volume"), +1, (_, _) => { });
        var power = new HotkeyAction("p.off", "Shut down", "Automation", "", _ => { }) { Accepts = ActionSettings.Countdown | ActionSettings.Force, PickerSection = ActionSections.Power };

        var list = MouseActionCatalog.Choices(new[] { level, power });

        Assert.Contains(list.Single(c => c.Spec == "omni:v.up").Options!, o => o.Key == MouseOptions.Step.Key);
        var shutDown = list.Single(c => c.Spec == "omni:p.off");
        Assert.Equal(ActionSections.Power, shutDown.Category);
        Assert.Contains(shutDown.Options!, o => o.Key == MouseOptions.Countdown.Key);
        Assert.Contains(shutDown.Options!, o => o.Key == MouseOptions.ForceClose.Key);
        Assert.DoesNotContain(shutDown.Options!, o => o.Key == MouseOptions.Times.Key); // powering off twice is not a thing
    }

    [Fact]
    public void ALevelActionRunsByTheStepAnInputChose_AndOthersIgnoreIt()
    {
        var moved = new List<int>();
        var level = HotkeyAction.ForLevel("v.up", "Volume up", "Volume", "", new HotkeyLevel("volume", "Volume"), +1, (_, delta) => moved.Add(delta));
        var plain = 0;
        var other = new HotkeyAction("o", "Other", "Volume", "", _ => plain++);

        level.RunWith(new RunSettings(Step: 12));
        level.RunWith(new RunSettings());
        other.RunWith(new RunSettings(Step: 12));

        Assert.Equal(new[] { 12, HotkeyAction.DefaultStep }, moved);
        Assert.Equal(1, plain);
    }

    [Fact]
    public void AnActionListedUnderAnotherSection_TakesItsCategoryAsItsHeading()
    {
        var app = new HotkeyAction("app.x", "Open X", "Apps", "", _ => { }) { PickerSection = ActionSections.Launch };
        var grouped = Action("g", "Sleep", "Automation", "Power");

        Assert.Equal("Apps", app.PickerHeading);
        Assert.Equal(ActionSections.Launch, app.Section);
        Assert.Equal("Power", grouped.PickerHeading);
        Assert.Null(Action("n", "Volume up", "Volume").PickerHeading);
    }

    [Fact]
    public void AListButtonCanBeHiddenForNow()
    {
        var hidden = false;
        var button = new HotkeyAction("app.showhidden", "Show hidden apps", "Apps", "", _ => { })
        {
            PickerSection = ActionSections.Launch,
            Command = _ => Task.FromResult<IReadOnlyList<HotkeyAction>>(Array.Empty<HotkeyAction>()),
            IsListed = () => hidden,
        };
        var choices = MouseActionCatalog.Choices(new[] { button }).Where(c => c.Category == ActionSections.Launch).ToList();
        var model = new PickerModel(PickChoice.Group(choices), null, new Dictionary<string, string>()) { Noun = "trigger" };

        Assert.DoesNotContain(model.Actions, a => a.IsCommand);
        hidden = true;
        model.Query = " ";
        model.Query = string.Empty;
        Assert.Contains(model.Actions, a => a.IsCommand);
    }
}
