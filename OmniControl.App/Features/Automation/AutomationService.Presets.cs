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
    internal bool TriggersHold(AutomationRule rule, int fired)
    {
        var holds = false;
        var i = 0;
        foreach (var stored in rule.AllTriggers)
        {
            var one = i == fired || AutomationTriggers.HoldsNow(stored, _sources);
            holds = i == 0 ? one : rule.TriggerJoinBefore(i) == AutomationJoins.And ? holds && one : holds || one;
            i++;
        }
        return holds;
    }

    /// True when one of the rule's triggers fires and the triggers hold with it.
    private bool FiresWith(AutomationRule rule, Func<string, bool> fires) =>
        rule.AllTriggers.Select((stored, i) => (stored, i)).Any(t => fires(t.stored) && TriggersHold(rule, t.i));

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
