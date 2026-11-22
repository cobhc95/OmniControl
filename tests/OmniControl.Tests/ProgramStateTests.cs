using OmniControl.App.Features.Automation;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.States;
using OmniControl.App.Shared.Scripts;

namespace OmniControl.Tests;

/// Programs as states: the one-time move of old program triggers, that the state fires at the same moments the old events did, the
/// picker's lists (two parts, the programs section) and the user's added programs. No Windows, no files outside the isolated data folder.
public class ProgramStateTests
{
    private static StateRegistry NewRegistry() => new(work => work());

    private static AutomationRule Rule(string trigger, params string[] more)
    {
        var rule = new AutomationRule { Trigger = trigger };
        rule.MoreTriggers.AddRange(more);
        return rule;
    }

    [Theory]
    [InlineData("program.start:chrome", "state:program.chrome")]
    [InlineData("program.start:C:\\Apps\\Chrome.exe", "state:program.chrome")]
    [InlineData("program.close:Chrome.exe", "state:program.chrome|when=turns%20off")]
    [InlineData("program.close:notepad", "state:program.notepad|when=turns%20off")]
    public void Old_program_triggers_become_the_programs_state_with_the_matching_setting(string old, string expected) =>
        Assert.Equal(expected, ProgramTriggerMigration.Converted(old));

    [Fact]
    public void The_move_rewrites_every_trigger_of_every_rule_once_and_leaves_the_rest()
    {
        var rules = new[] { Rule("program.start:chrome", "idle|min=5", "program.close:steam"), Rule("login"), Rule("program.start:") };
        Assert.Equal(2, ProgramTriggerMigration.Run(rules));
        Assert.Equal("state:program.chrome", rules[0].Trigger);
        Assert.Equal(new[] { "idle|min=5", "state:program.steam|when=turns%20off" }, rules[0].MoreTriggers);
        Assert.Equal("login", rules[1].Trigger);
        Assert.Equal("program.start:", rules[2].Trigger); // no program named: none can be made any more, nothing to follow
        Assert.Equal(0, ProgramTriggerMigration.Run(rules)); // nothing is left to move
    }

    [Fact]
    public void The_moved_trigger_fires_at_the_moments_the_old_start_and_close_events_did()
    {
        var registry = NewRegistry();
        var settings = new AutomationSettings();
        var programs = new ProgramStates(registry, settings, _ => { });
        var start = ProgramTriggerMigration.Converted("program.start:chrome");
        var close = ProgramTriggerMigration.Converted("program.close:chrome");
        var changes = new List<StateChange>();
        registry.ValueChanged += changes.Add;
        programs.Follow(new[] { ProgramStates.IdOf("chrome") }, new[] { ProgramStates.IdOf("chrome") });

        bool Fired(string trigger) => changes.Count > 0 && AutomationTriggers.FiresOn(trigger, changes[^1]);

        programs.Saw(new HashSet<string> { "chrome", "steam" }); // the first look is a baseline: a program already running does not start
        Assert.False(Fired(start));
        changes.Clear();

        programs.Saw(new HashSet<string> { "steam" });
        Assert.True(Fired(close));
        Assert.False(Fired(start));
        changes.Clear();

        programs.Saw(new HashSet<string> { "steam", "notepad" }); // another program's start is not this one's
        Assert.Empty(changes);

        programs.Saw(new HashSet<string> { "steam", "chrome" });
        Assert.True(Fired(start));
        Assert.False(Fired(close));
    }

    [Fact]
    public void A_program_followed_for_the_first_time_is_a_baseline_even_after_a_picker_read_it()
    {
        var registry = NewRegistry();
        var programs = new ProgramStates(registry, new AutomationSettings(), _ => { });
        programs.PickerOpened();
        programs.Offer(new[] { new ProgramFound("chrome", "Google Chrome", @"C:\Chrome\chrome.exe", false) }, new HashSet<string>()); // read: not running
        Assert.Equal(0, registry.Now("program.chrome")!.Number);
        var changes = new List<StateChange>();
        registry.ValueChanged += changes.Add;

        programs.Follow(new[] { "program.chrome" }, new[] { "program.chrome" }); // a rule follows it; Chrome started since the picker read
        programs.Saw(new HashSet<string> { "chrome" });
        Assert.DoesNotContain(changes, c => AutomationTriggers.FiresOn("state:program.chrome", c));
    }

    [Fact]
    public void Programs_are_listed_in_order_with_their_source_and_the_picker_drops_the_ones_only_it_offered()
    {
        var registry = NewRegistry();
        var settings = new AutomationSettings();
        settings.AddedPrograms.Add(new AddedProgram(@"D:\Games\Steam\steam.exe", "Steam"));
        var programs = new ProgramStates(registry, settings, _ => { });
        programs.Follow(new[] { "program.zoom" }, Array.Empty<string>());
        programs.PickerOpened();
        programs.Offer(new[]
        {
            new ProgramFound("chrome", "Google Chrome", @"C:\c\chrome.exe", true),
            new ProgramFound("blender", "Blender", @"C:\b\blender.exe", false),
            new ProgramFound("zoom", "Zoom", @"C:\z\zoom.exe", true),
        }, new HashSet<string> { "chrome" });

        var listed = registry.States.Where(s => s.Group == ProgramStates.Section).ToList();
        Assert.Equal(new[] { "Steam is running", "Zoom is running", "Google Chrome is running", "Blender is running" }, listed.Select(s => s.Name));
        Assert.Equal(new[] { "added by you", "in a rule", "running now", "installed" }, listed.Select(s => s.Note));
        Assert.Equal(1, registry.Now("program.chrome")!.Number);
        Assert.Equal(0, registry.Now("program.blender")!.Number);

        programs.PickerClosed();
        Assert.Equal(new[] { "program.steam", "program.zoom" }, registry.States.Where(s => s.Group == ProgramStates.Section).Select(s => s.Id));
    }

    [Fact]
    public void An_added_program_is_forgotten_by_its_remove_and_a_rule_keeps_it_listed()
    {
        var registry = NewRegistry();
        var settings = new AutomationSettings();
        settings.AddedPrograms.Add(new AddedProgram(@"D:\Games\Steam\steam.exe", "Steam"));
        var programs = new ProgramStates(registry, settings, _ => { });
        programs.Follow(Array.Empty<string>(), Array.Empty<string>());
        Assert.NotNull(registry.Find("program.steam"));
        Assert.Equal(@"D:\Games\Steam\steam.exe", programs.FactsOf("program.steam")!.Path);

        programs.Follow(new[] { "program.steam" }, Array.Empty<string>());
        Assert.Equal("added by you", registry.Find("program.steam")!.Note); // still the user's own
        programs.FactsOf("program.steam")!.Remove!();
        Assert.Empty(settings.AddedPrograms);
        Assert.Equal("in a rule", registry.Find("program.steam")!.Note); // a rule still names it
        Assert.Null(programs.FactsOf("program.steam")!.Remove);
    }

    [Fact]
    public void A_program_typed_by_name_is_kept_by_its_identity()
    {
        Assert.Equal("state:program.notepad", AutomationTriggers.Tidy("state:program.Notepad.exe"));
        Assert.Equal("state:program.notepad|when=turns%20off", AutomationTriggers.Tidy("state:program.Notepad|when=turns%20off"));
        Assert.Equal("state:lamp", AutomationTriggers.Tidy("state:lamp"));
        Assert.Equal("login", AutomationTriggers.Tidy("login"));
    }

    private sealed class Sources : ITriggerSources
    {
        public StateRegistry Registry { get; } = NewRegistry();
        public ProgramStates? Programs { get; set; }
        public IReadOnlyList<StateEntry> States => Registry.States;
        public StateEntry? State(string id) => Registry.Find(id);
        public StateValue? Now(string stateId) => Registry.Now(stateId);
        public DateTime? ChangedAt(string stateId) => Registry.ChangedAt(stateId);
        public IEnumerable<HotkeyAction> Actions => new[] { new HotkeyAction("volume.up", "Volume up", "Volume", string.Empty, _ => { }) };
        public HotkeyAction? Action(string id) => null;
        public ActionSection? SectionOf(string section) => section == "Displays" ? new ActionSection("Displays", "M1,1 H5 V5 Z", 1) : null;
        public ProgramFacts? ProgramOf(string stateId) => Programs?.FactsOf(stateId);
    }

    [Fact]
    public void The_trigger_list_has_two_parts_with_the_programs_as_states_between_pinned_and_last_rows()
    {
        var sources = new Sources();
        sources.Programs = new ProgramStates(sources.Registry, new AutomationSettings(), _ => { });
        sources.Programs.Follow(new[] { "program.zoom" }, Array.Empty<string>());
        sources.Registry.Register(new StateEntry("displays.on", "Screen", "Displays", StateKind.OnOff, () => StateValue.Of(true), "test"));
        sources.Registry.Register(new StateEntry("power.battery", "On battery", "Power", StateKind.OnOff, () => StateValue.Of(false), "test"));

        var groups = PickChoice.Group(AutomationTriggers.Choices(sources, addProgram: _ => Task.FromResult<StateEntry?>(null)));

        Assert.Equal(new[]
        {
            "Start up and power", "Time and idle", "Programs", "USB devices", "Keyboard", "When an action runs", "Displays",
        }, groups.Select(g => g.Name));
        Assert.Equal(Enumerable.Repeat("PC and time", 6).Append("Your pages"), groups.Select(g => g.Part));
        Assert.All(groups.Where(g => g.Part == "PC and time"), g => Assert.NotNull(g.Place));
        Assert.Contains(groups[0].Choices, c => c.Spec == "state:power.battery"); // a power state sits with the start-up triggers
        Assert.Contains(groups[0].Choices, c => c.Spec == AutomationTriggers.Login);
        Assert.DoesNotContain(groups.SelectMany(g => g.Choices), c => c.Spec.StartsWith("program.", StringComparison.Ordinal)); // no program trigger of its own any more

        var programs = groups[2].Choices;
        Assert.NotNull(programs[0].Command); // the pinned row
        Assert.Equal("＋ Add a program…", programs[0].Name);
        Assert.Equal("state:program.zoom", programs[1].Spec);
        Assert.Equal("Zoom is running", programs[1].Name);
        Assert.Equal("in a rule", programs[1].Detail);
        Assert.Equal("Not known", programs[1].Value); // no look or picker read has told yet
        Assert.Equal("Pick by name…", programs[^1].Name);
        Assert.NotNull(programs[^1].ParamHint);
        Assert.Contains(programs[^1].Options!, o => o.Key == "when");
    }

    [Fact]
    public void A_program_state_says_starts_and_closes_while_other_states_keep_turns_on_and_off()
    {
        var sources = new Sources();
        sources.Programs = new ProgramStates(sources.Registry, new AutomationSettings(), _ => { });
        sources.Programs.Follow(new[] { "program.zoom" }, Array.Empty<string>());
        sources.Registry.Register(new StateEntry("lamp", "Lamp", "Smart Devices", StateKind.OnOff, () => StateValue.Of(true), "test"));

        Assert.Equal("when Zoom starts", AutomationTriggers.PhraseOf("state:program.zoom", sources));
        Assert.Equal("when Zoom closes", AutomationTriggers.PhraseOf("state:program.zoom|when=turns%20off", sources));
        Assert.Equal("when Zoom starts or closes", AutomationTriggers.PhraseOf("state:program.zoom|when=changes", sources));
        Assert.Equal("when Lamp turns on", AutomationTriggers.PhraseOf("state:lamp", sources));
        Assert.Equal("Zoom is running", AutomationTriggers.ChipOf("state:program.zoom", sources));

        var when = AutomationTriggers.Choices(sources).Single(c => c.Spec == "state:program.zoom").Options!.Single();
        Assert.Equal(new[] { "turns on", "turns off", "changes" }, when.Choices); // what is stored never changes
        var item = new OptionItem(when, "turns off");
        Assert.Equal(new[] { "starts", "closes", "starts or closes" }, item.ChoiceWords.Select(w => w.Word));
        Assert.Equal("closes", item.Display);
        item.Chosen = item.ChoiceWords[0];
        Assert.Equal("turns on", item.Value);
        var lamp = AutomationTriggers.Choices(sources).Single(c => c.Spec == "state:lamp").Options!.Single();
        Assert.Equal(new[] { "turns on", "turns off", "changes" }, new OptionItem(lamp, "turns on").ChoiceWords.Select(w => w.Word));
    }

    [Fact]
    public void Without_an_add_command_the_programs_have_no_pinned_row()
    {
        var choices = AutomationTriggers.Choices(new Sources());
        Assert.DoesNotContain(choices, c => c.Command is not null);
        Assert.Contains(choices, c => c.Name == "Pick by name…");
    }

    [Fact]
    public void The_picker_shows_the_two_parts_as_headings_over_their_sections()
    {
        var sources = new Sources();
        sources.Registry.Register(new StateEntry("displays.on", "Screen", "Displays", StateKind.OnOff, () => StateValue.Of(true), "test"));
        var model = PickerModel.On(AutomationTriggers.Choices(sources), string.Empty, "trigger", AutomationTriggers.SearchText);
        var rows = model.Sections;
        Assert.Equal(new[] { "PC and time", "Your pages" }, rows.Where(r => r.IsHeading).Select(r => r.Name));
        Assert.Equal("Start up and power", rows[1].Name);
        Assert.Equal("power", rows[1].Icon);
        Assert.Equal("Displays", rows[^1].Name);
    }
}
