using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Automation;
using OmniControl.App.Shared.Contracts;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Features.States;

namespace OmniControl.Tests;

/// The master list of states, the state / "When an action runs" triggers and the loop limits (no UI, no Windows).
public class StateTriggerTests
{
    private static StateRegistry NewRegistry(RunChain? chain = null) => new(work => work(), () => chain);

    private sealed class Box
    {
        public StateValue? Value;
    }

    private static StateEntry OnOff(string id, Box box, string name = "Lamp") => new(id, name, "Smart Devices", StateKind.OnOff, () => box.Value, "test");

    [Fact]
    public void Registry_raises_a_change_only_when_the_value_really_changed()
    {
        var registry = NewRegistry();
        var box = new Box { Value = StateValue.Of(false) };
        registry.Register(OnOff("lamp", box));
        var changes = new List<StateChange>();
        registry.ValueChanged += changes.Add;

        registry.Notify("lamp"); // same value
        box.Value = StateValue.Of(false, DateTime.Now); // same value, another time
        registry.Notify("lamp");
        Assert.Empty(changes);

        box.Value = StateValue.Of(true);
        registry.Notify("lamp");
        var change = Assert.Single(changes);
        Assert.Equal(0, change.Before!.Number);
        Assert.Equal(1, change.After!.Number);
        Assert.Equal(1, registry.Now("lamp")!.Number);
    }

    [Fact]
    public void Registry_list_follows_register_rename_and_unregister()
    {
        var registry = NewRegistry();
        var listChanges = 0;
        registry.ListChanged += () => listChanges++;
        var box = new Box();
        registry.Register(OnOff("lamp", box, "Lamp"));
        registry.Register(OnOff("lamp", box, "Desk lamp")); // a rename: same id
        Assert.Equal("Desk lamp", Assert.Single(registry.States).Name);
        registry.Unregister("lamp");
        Assert.Empty(registry.States);
        Assert.Null(registry.Find("lamp"));
        registry.Notify("lamp"); // a late notify for a state that is gone does nothing
        Assert.Equal(3, listChanges);
    }

    [Fact]
    public void Registry_carries_the_chain_of_the_code_that_notified()
    {
        var chain = RunChain.Start("rule1");
        var registry = NewRegistry(chain);
        var box = new Box { Value = StateValue.Of(10) };
        registry.Register(new StateEntry("vol", "Volume", "Volume", StateKind.Level, () => box.Value, "test"));
        StateChange? seen = null;
        registry.ValueChanged += c => seen = c;
        box.Value = StateValue.Of(60);
        registry.Notify("vol");
        Assert.Same(chain, seen!.Chain);
    }

    [Theory]
    [InlineData(StateKind.OnOff, 0d, 1d, StateTriggerMath.TurnsOn, true)]
    [InlineData(StateKind.OnOff, 1d, 0d, StateTriggerMath.TurnsOn, false)]
    [InlineData(StateKind.OnOff, 1d, 0d, StateTriggerMath.TurnsOff, true)]
    [InlineData(StateKind.OnOff, 1d, 0d, StateTriggerMath.Changes, true)]
    [InlineData(StateKind.Level, 40d, 60d, StateTriggerMath.Above, true)]
    [InlineData(StateKind.Level, 50d, 60d, StateTriggerMath.Above, true)]
    [InlineData(StateKind.Level, 55d, 60d, StateTriggerMath.Above, false)] // already above: not crossing
    [InlineData(StateKind.Level, 60d, 40d, StateTriggerMath.Below, true)]
    [InlineData(StateKind.Level, 40d, 30d, StateTriggerMath.Below, false)]
    [InlineData(StateKind.Level, 40d, 50.2d, StateTriggerMath.Becomes, true)]
    [InlineData(StateKind.Level, 50d, 50.4d, StateTriggerMath.Becomes, false)] // already 50 after rounding
    public void Trigger_maths_fires_on_crossing_only(StateKind kind, double before, double after, string when, bool fires) =>
        Assert.Equal(fires, StateTriggerMath.Fires(kind, StateValue.Of(before), StateValue.Of(after), when, level: 50));

    [Fact]
    public void Trigger_maths_never_fires_from_an_unknown_value_or_for_the_same_value()
    {
        Assert.False(StateTriggerMath.Fires(StateKind.OnOff, null, StateValue.Of(true), StateTriggerMath.TurnsOn));
        Assert.False(StateTriggerMath.Fires(StateKind.OnOff, StateValue.Of(true), null, StateTriggerMath.TurnsOff));
        Assert.False(StateTriggerMath.Fires(StateKind.Level, StateValue.Of(60), StateValue.Of(60), StateTriggerMath.Above, 50));
        Assert.True(StateTriggerMath.Fires(StateKind.Choice, StateValue.Of("Speakers"), StateValue.Of("Headphones"), StateTriggerMath.Becomes, choice: "Headphones"));
        Assert.False(StateTriggerMath.Fires(StateKind.Choice, StateValue.Of("Speakers"), StateValue.Of("TV"), StateTriggerMath.Becomes, choice: "Headphones"));
    }

    [Fact]
    public void A_rule_never_starts_from_its_own_chain_and_chains_are_cut_at_the_limit()
    {
        Assert.True(RunChain.MayStart(null, "a", 3));
        var chain = RunChain.Start("a");
        Assert.False(RunChain.MayStart(chain, "a", 3)); // itself
        Assert.True(RunChain.MayStart(chain, "b", 3));
        chain = RunChain.After(chain, "b");
        Assert.False(RunChain.MayStart(chain, "a", 3)); // a loop back to the first
        chain = RunChain.After(chain, "c");
        Assert.Equal(new[] { "a", "b", "c" }, chain.Rules);
        Assert.False(RunChain.MayStart(chain, "d", 3)); // the chain is full
        Assert.Equal(new[] { "x" }, RunChain.After(null, "x").Rules);
    }

    private sealed class FakeSources : ITriggerSources
    {
        public StateRegistry Registry { get; } = NewRegistry();
        public List<HotkeyAction> List { get; } = new();
        public IReadOnlyList<StateEntry> States => Registry.States;
        public StateEntry? State(string id) => Registry.Find(id);
        public StateValue? Now(string stateId) => Registry.Now(stateId);
        public DateTime? ChangedAt(string stateId) => Registry.ChangedAt(stateId);
        public IEnumerable<HotkeyAction> Actions => List;
        public HotkeyAction? Action(string id) => List.FirstOrDefault(a => a.Id == id);
    }

    private static HotkeyAction NewAction(string id, string title) => new(id, title, "Volume", string.Empty, _ => { });

    [Fact]
    public void Trigger_list_is_read_live_from_both_master_lists()
    {
        var sources = new FakeSources();
        Assert.DoesNotContain(AutomationTriggers.Choices(Array.Empty<string>(), sources), c => c.Spec == "state:lamp");
        sources.Registry.Register(OnOff("lamp", new Box()));
        sources.List.Add(NewAction("volume.up", "Volume up"));
        var choices = AutomationTriggers.Choices(Array.Empty<string>(), sources);
        Assert.Contains(choices, c => c.Spec == "state:lamp" && c.Name == "Lamp");
        Assert.Contains(choices, c => c.Spec == "action.runs:volume.up" && c.Name == "Volume up");
        Assert.Contains(choices, c => c.Spec == AutomationTriggers.Hotkey); // the fixed table is still there
    }

    [Fact]
    public void Stored_triggers_read_with_the_names_now_fire_on_their_change_and_show_missing()
    {
        var sources = new FakeSources();
        var box = new Box { Value = StateValue.Of(false) };
        sources.Registry.Register(OnOff("lamp", box, "L530 bulb"));
        var volumeUp = NewAction("volume.up", "Volume up");
        sources.List.Add(volumeUp);

        const string turnsOn = "state:lamp|when=turns%20on";
        Assert.Equal("when L530 bulb turns on", AutomationTriggers.PhraseOf(turnsOn, sources));
        Assert.Equal("when Volume up runs", AutomationTriggers.PhraseOf("action.runs:volume.up", sources));

        StateChange? change = null;
        sources.Registry.ValueChanged += c => change = c;
        box.Value = StateValue.Of(true);
        sources.Registry.Notify("lamp");
        Assert.True(AutomationTriggers.FiresOn(turnsOn, change!));
        Assert.False(AutomationTriggers.FiresOn("state:lamp|when=turns%20off", change!));
        Assert.True(AutomationTriggers.FiresOn("action.runs:volume.up", volumeUp, sources));

        sources.Registry.Unregister("lamp");
        sources.List.Clear();
        Assert.True(AutomationTriggers.IsMissing(turnsOn, sources));
        Assert.True(AutomationTriggers.IsMissing("action.runs:volume.up", sources));
        Assert.False(AutomationTriggers.IsMissing("login", sources));
        Assert.Contains("no longer available", AutomationTriggers.PhraseOf(turnsOn, sources));
    }

    [Fact]
    public void Moved_ids_are_rewritten_keeping_the_settings()
    {
        var moved = new Dictionary<string, string> { ["devices.a.on"] = "devices.method.m.on" };
        Assert.Equal("omni:devices.method.m.on|times=2", MovedActions.RewriteAction("omni:devices.a.on|times=2", moved));
        Assert.Equal("omni:devices.b.on", MovedActions.RewriteAction("omni:devices.b.on", moved));
        Assert.Equal("action.runs:devices.method.m.on", MovedActions.Rewrite("action.runs:devices.a.on", AutomationTriggers.ActionRuns, moved));
        var list = new List<string> { "omni:devices.a.on", "click.left" };
        Assert.True(MovedActions.RewriteAll(list, moved));
        Assert.Equal("omni:devices.method.m.on", list[0]);
    }
}
