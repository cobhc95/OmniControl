namespace OmniControl.App.Features.Automation;

// Triggers joined by "and" (the preset manager's chips, AutomationPresets): a rule starts when one trigger fires and, read left to right
// with the joins, the triggers hold. With only "or" joins any fired trigger holds, as for every rule before joins existed.
public sealed partial class AutomationService
{
    /// A state changed (before any rule starts from it).
    internal event Action? StateSeen;

    /// A rule is about to start from a state change.
    internal event Action<AutomationRule>? StartingFromState;

    internal ITriggerSources TriggerSources => _sources;

    /// The triggers hold with trigger <paramref name="fired"/> taken as true (−1: none, only the states as they are now).
    internal bool TriggersHold(AutomationRule rule, int fired, bool assumeOneShot = false)
    {
        var holds = false;
        var i = 0;
        foreach (var stored in rule.AllTriggers)
        {
            var one = i == fired
                || (assumeOneShot && AutomationTriggers.IsOneShot(stored, _sources))
                || AutomationTriggers.HoldsNow(stored, _sources);
            holds = i == 0 ? one : rule.TriggerJoinBefore(i) switch
            {
                AutomationJoins.And => holds && one,
                AutomationJoins.AndNot => holds && !one,
                _ => holds || one,
            };
            i++;
        }
        return holds;
    }

    /// True when one of the rule's triggers fires and the triggers hold with it.
    private bool FiresWith(AutomationRule rule, Func<string, bool> fires) =>
        rule.AllTriggers.Select((stored, i) => (stored, i)).Any(t => fires(t.stored) && TriggersHold(rule, t.i));

    /// True when any one of the rule's triggers fires, without asking whether the triggers hold.
    private static bool FiresAny(AutomationRule rule, Func<string, bool> fires) => rule.AllTriggers.Any(fires);

    /// True when a fired trigger's joined triggers hold and the rule's conditions hold: the rule's top line holds.
    private bool TopHoldsWhenFired(AutomationRule rule, Func<string, bool> fires, AutomationContext now) =>
        (rule.Conditions.Count == 0 || AutomationJoins.Holds(rule, now)) && rule.AllTriggers.Select((stored, i) => (stored, i)).Any(t => fires(t.stored) && TriggersHold(rule, t.i));

    /// True for a rule that has Otherwise steps (a Do rule only).
    private static bool HasOtherwise(AutomationRule rule) => rule.Kind == AutomationKind.Do && rule.OtherwiseActions.Count > 0;

    /// An "Only if" entry exists: a legacy condition, or a trigger joined "and"/"and not" after the first. Otherwise needs one to fail.
    internal static bool HasOnlyIf(AutomationRule rule) =>
        rule.Conditions.Count > 0 || rule.AllTriggers.Skip(1).Select((_, i) => rule.TriggerJoinBefore(i + 1)).Any(j => j is AutomationJoins.And or AutomationJoins.AndNot);

    private void StartFromState(AutomationRule rule, Shared.Contracts.RunChain? chain)
    {
        StartingFromState?.Invoke(rule);
        StartFrom(rule, chain);
    }

    /// Adds a rule made elsewhere (a preset's) with its own action, and saves.
    internal void AddRule(AutomationRule rule)
    {
        Settings.Rules.Add(rule);
        if (rule.Kind == AutomationKind.Do) _hotkeys.Register(MakeRuleAction(rule));
        Edited(rule);
    }
}
