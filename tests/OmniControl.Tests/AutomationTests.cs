using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Automation;
using OmniControl.App.Features.Input.Mouse;

namespace OmniControl.Tests;

public class AutomationTests
{
    private static AutomationContext At(int hour, int minute, params string[] programs) =>
        new(() => programs.ToHashSet(), new DateTime(2026, 11, 16, hour, minute, 0)); // a Monday

    [Theory]
    [InlineData("between|fh=22;th=7", 23, 0, true)]
    [InlineData("between|fh=22;th=7", 3, 0, true)]
    [InlineData("between|fh=22;th=7", 12, 0, false)]
    [InlineData("between|fh=22;th=7", 7, 0, false)]
    [InlineData("between|fh=9;th=17", 9, 0, true)]
    [InlineData("between|fh=9;th=17", 17, 0, false)]
    public void Between_handles_a_span_past_midnight(string condition, int hour, int minute, bool holds) =>
        Assert.Equal(holds, AutomationConditions.Holds(condition, At(hour, minute)));

    [Fact]
    public void Days_and_programs_are_checked_and_unknown_conditions_never_hold()
    {
        Assert.True(AutomationConditions.Holds("days", At(10, 0)));                  // Monday is in the default days
        Assert.False(AutomationConditions.Holds("days|d0=0", At(10, 0)));            // Monday switched off
        Assert.True(AutomationConditions.Holds("running:chrome", At(10, 0, "chrome")));
        Assert.False(AutomationConditions.Holds("running:chrome", At(10, 0, "code")));
        Assert.False(AutomationConditions.Holds("nonsense", At(10, 0)));
    }

    [Fact]
    public void Days_read_as_weekdays_and_weekends()
    {
        Assert.Equal("every day", AutomationDays.PhraseOf(MouseAction.Parse("time").Options));
        Assert.Equal("on weekdays", AutomationDays.PhraseOf(MouseAction.Parse("time|d5=0;d6=0").Options));
        Assert.Equal("at weekends", AutomationDays.PhraseOf(MouseAction.Parse("time|d0=0;d1=0;d2=0;d3=0;d4=0").Options));
    }

    [Fact]
    public void A_time_is_due_once_it_is_passed_and_not_after_the_catch_up_time()
    {
        var catchUp = TimeSpan.FromMinutes(10);
        var trigger = "time|h=7;m=30";
        var from = new DateTime(2026, 11, 16, 7, 29, 55);
        Assert.True(AutomationTriggers.TimeIsDue(trigger, from, from.AddSeconds(10), catchUp));
        Assert.False(AutomationTriggers.TimeIsDue(trigger, from.AddSeconds(10), from.AddSeconds(15), catchUp));      // already passed
        Assert.True(AutomationTriggers.TimeIsDue(trigger, new DateTime(2026, 11, 16, 6, 0, 0), new DateTime(2026, 11, 16, 7, 35, 0), catchUp));  // woke from sleep soon after
        Assert.False(AutomationTriggers.TimeIsDue(trigger, new DateTime(2026, 11, 16, 6, 0, 0), new DateTime(2026, 11, 16, 9, 0, 0), catchUp));  // woke much later
    }

    [Fact]
    public void The_next_time_skips_days_that_are_off()
    {
        var friday = new DateTime(2026, 11, 20, 8, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 23, 7, 30, 0), AutomationTriggers.NextTime("time|h=7;m=30;d5=0;d6=0", friday));
    }

    [Theory]
    [InlineData("Ctrl+Alt+Shift+P")]
    [InlineData("Ctrl+Alt+Delete")]
    [InlineData("Win+L")]
    [InlineData("Alt+Tab")]
    [InlineData("Escape")]
    [InlineData("Ctrl+Shift+Escape")]
    public void Reserved_keys_cannot_be_given_to_an_action(string gesture) => Assert.NotNull(ReservedKeys.ProblemWith(gesture));

    [Fact]
    public void An_ordinary_key_is_not_reserved() => Assert.Null(ReservedKeys.ProblemWith("Ctrl+Alt+K"));

    [Fact]
    public void A_block_rule_cannot_pick_an_action_that_keeps_the_user_in_control()
    {
        var safe = new HotkeyAction("a", "Lock", "Automation", "", _ => { }) { NeverBlock = true };
        var other = new HotkeyAction("b", "Shut down", "Automation", "", _ => { });
        var choices = AutomationSteps.BlockChoices(new[] { safe, other });
        Assert.NotNull(choices.Single(c => c.Name == "Lock").Unavailable);
        Assert.Null(choices.Single(c => c.Name == "Shut down").Unavailable);
    }

    [Fact]
    public void A_do_rule_never_offers_another_rules_action_or_a_raw_command()
    {
        var rule = new HotkeyAction($"{AutomationRule.ActionIdPrefix}x", "Run: x", "Automation", "", _ => { });
        var choices = AutomationSteps.DoChoices(new[] { rule });
        Assert.DoesNotContain(choices, c => c.Name == "Run: x");
        Assert.DoesNotContain(choices, c => c.Spec == "script");
    }

    [Fact]
    public void A_rule_needs_what_its_kind_needs_to_be_complete()
    {
        Assert.False(new AutomationRule { Kind = AutomationKind.Do, Actions = { "omni:a" } }.IsComplete);
        Assert.True(new AutomationRule { Kind = AutomationKind.Do, Trigger = "start", Actions = { "omni:a" } }.IsComplete);
        Assert.False(new AutomationRule { Kind = AutomationKind.Block, Actions = { "omni:a" } }.IsComplete);
        Assert.True(new AutomationRule { Kind = AutomationKind.Block, Conditions = { "battery" }, Actions = { "omni:a" } }.IsComplete);
    }

    [Fact]
    public void Conditions_are_read_left_to_right_with_and_or_and_not()
    {
        var rule = new AutomationRule { Conditions = { "running:a", "running:b", "running:c" } };
        Assert.True(AutomationJoins.Holds(rule, At(10, 0, "a", "b", "c")));          // saved without joins: all "and"
        Assert.False(AutomationJoins.Holds(rule, At(10, 0, "a", "c")));
        rule.SetJoinBefore(1, AutomationJoins.Or);                                   // (a or b) and c
        Assert.True(AutomationJoins.Holds(rule, At(10, 0, "b", "c")));
        Assert.False(AutomationJoins.Holds(rule, At(10, 0, "a")));
        rule.SetJoinBefore(2, AutomationJoins.AndNot);                               // (a or b) and not c
        Assert.True(AutomationJoins.Holds(rule, At(10, 0, "a")));
        Assert.False(AutomationJoins.Holds(rule, At(10, 0, "a", "c")));
        Assert.True(AutomationJoins.Holds(new AutomationRule(), At(10, 0)));         // no conditions: nothing stands in the way
    }

    [Fact]
    public void Joins_and_waits_stay_lined_up_with_their_conditions_and_actions()
    {
        var rule = new AutomationRule { Actions = { "omni:a", "omni:b" } };          // an old rule: no waits saved
        Assert.Equal(0, rule.WaitBefore(1));
        rule.SetWaitBefore(1, 30);
        rule.AddAction("omni:c");
        Assert.Equal(new[] { 0, 30, 0 }, rule.StepWaits);
        rule.RemoveAction(1);
        Assert.Equal(0, rule.WaitBefore(1));
        rule.AddCondition("battery");
        rule.AddCondition("days");
        rule.SetJoinBefore(1, AutomationJoins.Or);
        rule.RemoveCondition(0);
        Assert.Single(rule.Conditions);
        Assert.Single(rule.ConditionJoins);
    }

    [Fact]
    public void Any_trigger_of_a_rule_is_one_of_its_triggers()
    {
        var rule = new AutomationRule { Trigger = "start", MoreTriggers = { "login" } };
        Assert.Equal(new[] { "start", "login" }, rule.AllTriggers);
        Assert.Equal(new[] { "login" }, new AutomationRule { MoreTriggers = { "login" } }.AllTriggers);
    }

    [Theory]
    [InlineData(false, false, false, false)] // running
    [InlineData(false, true, false, true)]   // paused: the user's own and preset rules stop
    [InlineData(false, true, true, false)]   // paused: a rule of another page keeps running
    [InlineData(true, false, true, true)]    // safe start stops all
    [InlineData(true, true, true, true)]
    public void Pausing_stops_only_the_users_rules(bool safeStart, bool paused, bool featureRule, bool stops) =>
        Assert.Equal(stops, AutomationPause.Stops(safeStart, paused, featureRule));

    [Theory]
    [InlineData(false, false, false, false)] // blocks apply
    [InlineData(false, true, false, true)]   // paused: the user's blocks are off for ordinary actions
    [InlineData(false, true, true, false)]   // paused: a block still stops a feature's rule action
    [InlineData(true, false, true, true)]    // safe start: no blocks at all
    public void Blocks_still_reach_a_features_action_while_paused(bool safeStart, bool paused, bool featureAction, bool off) =>
        Assert.Equal(off, AutomationPause.BlocksOff(safeStart, paused, featureAction));
}
