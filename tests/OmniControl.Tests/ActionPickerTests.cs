using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Features.Hotkeys.Picker;

namespace OmniControl.Tests;

/// The Choose-the-action window: sections and groups from the master entries, the settings an entry offers, and search across sections.
public class ActionPickerTests
{
    private static readonly HotkeyAction[] None = Array.Empty<HotkeyAction>();

    private static HotkeyAction Action(string id, string title, string category, string? group = null) => new(id, title, category, "", _ => { }, group: group);

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

    // ---- Families: the directions of one thing are one line ----

    private static HotkeyAction[] PointerActions()
    {
        var speed = new HotkeyLevel("pointer", "Pointer speed");
        var scroll = new HotkeyLevel("scroll", "Scroll lines");
        var scheme = new ActionFamily("pointer.scheme", "Pointer scheme");
        return new[]
        {
            HotkeyAction.ForLevel("pointer.speed.up", "Pointer speed up", "Mouse", "", speed, +1, (_, _) => { }, group: "Pointer settings"),
            HotkeyAction.ForLevel("pointer.speed.down", "Pointer speed down", "Mouse", "", speed, -1, (_, _) => { }, group: "Pointer settings"),
            HotkeyAction.ForLevel("pointer.scroll.up", "Scroll lines up", "Mouse", "", scroll, +1, (_, _) => { }, group: "Pointer settings"),
            HotkeyAction.ForLevel("pointer.scroll.down", "Scroll lines down", "Mouse", "", scroll, -1, (_, _) => { }, group: "Pointer settings"),
            new HotkeyAction("pointer.scheme.next", "Next pointer scheme", "Mouse", "", _ => { }, group: "Pointer settings") { Family = scheme, FamilyRole = "Next" },
            new HotkeyAction("pointer.scheme.previous", "Previous pointer scheme", "Mouse", "", _ => { }, group: "Pointer settings") { Family = scheme, FamilyRole = "Previous" },
            new HotkeyAction("pointer.precision.toggle", "Toggle pointer precision", "Mouse", "", _ => { }, group: "Pointer settings"),
        };
    }

    private static PickerModel ManageModel(HotkeyAction[] actions) =>
        new(PickChoice.Group(MouseActionCatalog.ManageChoices(actions)), null, new Dictionary<string, string>()) { Noun = "trigger" };

    [Fact]
    public void TheDirectionsOfAFamilyAreOneLine_AndSingleActionsStayAlone()
    {
        var actions = PointerActions();
        var model = ManageModel(actions);

        var lines = model.Actions.Where(a => a.IsEntry).ToList();

        Assert.Equal(new[] { "Pointer speed", "Scroll lines", "Pointer scheme", "Toggle pointer precision" }, lines.Select(l => l.Text));
        Assert.Equal(7, lines.Sum(l => l.Members?.Count ?? 1)); // 7 actions, 4 lines
        Assert.Equal("Up · Down", lines[0].Summary);
    }

    [Fact]
    public void EveryDirectionKeepsItsOwnIdInTheList()
    {
        var actions = PointerActions();
        var model = ManageModel(actions);

        var specs = model.Actions.SelectMany(a => a.Members ?? (a.Choice is null ? Array.Empty<PickChoice>() : new[] { a.Choice })).Select(c => c.Spec);

        Assert.Equal(actions.Select(a => $"omni:{a.Id}").OrderBy(x => x), specs.OrderBy(x => x));
        Assert.Equal("Next", actions[4].FamilyRole);
        Assert.Equal("Up", actions[0].FamilyRole);
        Assert.Equal("Down", actions[1].FamilyRole);
    }

    [Fact]
    public void ASearchForADirection_ShowsTheFamilyLineWithThatDirectionChosen()
    {
        var model = ManageModel(PointerActions());

        model.Query = "pointer speed down";
        model.Settle();

        var line = Assert.Single(model.Actions, a => a.Text == "Pointer speed");
        Assert.NotNull(line.Members);
        Assert.Equal("omni:pointer.speed.down", model.ActiveChoice!.Spec);
    }

    [Fact]
    public void PickingFromAFamily_ReturnsTheDirectionChosenInTheDirectionSetting()
    {
        var model = ManageModel(PointerActions());
        model.Query = "Next pointer scheme";
        model.Settle();
        Assert.Equal("omni:pointer.scheme.next", model.Use()!.Choice.Spec);

        var pick = new PickerModel(PickChoice.Group(MouseActionCatalog.ManageChoices(PointerActions())), null, new Dictionary<string, string>()) { Noun = "trigger" };
        pick.Move(0);
        var direction = pick.OptionItems.Single(o => o.Option.Label == "Direction");
        Assert.Equal("Up", direction.Value);
        direction.Value = "Down";

        var result = pick.Use()!;

        Assert.Equal("omni:pointer.speed.down", result.Choice.Spec);
        Assert.DoesNotContain(result.Values.Keys, k => k.StartsWith('$'));
    }
}
