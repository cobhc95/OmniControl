using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Rules;

namespace OmniControl.App.Automation;

/// <summary>
/// What a rule's actions can be. A Do rule uses the same list as a gesture rule's steps (<see cref="RuleSteps"/>, which is the mouse button
/// action list over the live master list) except the "run a command" step, which is a custom action here, plus the way to make one. A
/// Block rule forbids master actions only, since those are what the one gate in <see cref="HotkeyRegistry"/> guards.
/// </summary>
public static class AutomationSteps
{
    private const string KeepsControl = "This can't be blocked: it keeps you in control.";

    public static IReadOnlyList<PickChoice> DoChoices(IEnumerable<HotkeyAction> actions) =>
        RuleSteps.Choices(actions)
            .Where(c => MouseActionCatalog.Split(c.Spec).Id != RuleSteps.Command && !IsRuleAction(c.Spec)) // a rule never starts a rule: no loops
            .ToList();

    public static IReadOnlyList<PickChoice> BlockChoices(IEnumerable<HotkeyAction> actions) =>
        actions.Select(a => MouseActionCatalog.OmniEntry(a, unavailable: a.NeverBlock ? KeepsControl : null)).ToList();

    private static bool IsRuleAction(string spec) =>
        ActionIdOf(spec)?.StartsWith(AutomationRule.ActionIdPrefix, StringComparison.Ordinal) == true;

    /// <summary>The master action id a stored entry names, or null when it is not an OmniControl action.</summary>
    public static string? ActionIdOf(string stored) => MouseAction.Parse(stored) is { Id: MouseActionCatalog.Omni } action ? action.Param : null;

    /// <summary>How a stored action reads in a sentence; a Block rule's entries are looked up in the master list.</summary>
    public static string PhraseOf(AutomationRule rule, string stored, IReadOnlyList<PickChoice> choices) =>
        rule.Kind == AutomationKind.Block
            ? choices.FirstOrDefault(c => c.Spec == MouseActionCatalog.Migrated(stored))?.Name ?? "(an action that is no longer available)"
            : RuleSteps.PhraseOf(stored, choices);

    /// <summary>True for a step that opens a program: it needs the user's approval before it is saved or run.</summary>
    public static bool NeedsApproval(string stored) => RuleSteps.NeedsApproval(stored);
}
