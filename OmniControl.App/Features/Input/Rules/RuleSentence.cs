using OmniControl.App.Features.Hotkeys.Actions;

namespace OmniControl.App.Features.Input.Rules;

/// A rule read as one sentence: "If right click over the taskbar and Ctrl held, then Web browser, then wait 500 ms".
public static class RuleSentence
{
    /// The word between conditions: "and" (all must hold) or "or" (any one is enough).
    public static string JoinWord(bool anyOf) => anyOf ? "or" : "and";

    /// <param name="steps">The step list as it is now (names of the actions).</param>
    public static string Of(Rule rule, IReadOnlyList<PickChoice> steps)
    {
        var when = RuleTriggers.PhraseOf(rule.Trigger);
        var conditions = rule.Conditions.Count == 0
            ? string.Empty
            : " " + string.Join($" {JoinWord(rule.AnyOf)} ", rule.Conditions.Select(RuleConditions.PhraseOf));
        var then = rule.Steps.Count == 0 ? "(choose what it does)" : string.Join(", then ", rule.Steps.Select(s => RuleSteps.PhraseOf(s, steps)));
        return $"If {when}{conditions}, then {then}";
    }
}
