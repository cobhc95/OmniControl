namespace OmniControl.App.Features.Automation;

/// The words that join a rule's conditions: "and", "or", "and not", read left to right (no precedence: "a or b and c" is "(a or b) and c").
/// Stored as the ids below. One place decides what they mean and how they read.
public static class AutomationJoins
{
    public const string And = "and", Or = "or", AndNot = "andnot";

    /// The joins a condition can have, in the order the pop-down lists them.
    public static readonly IReadOnlyList<string> All = new[] { And, Or, AndNot };

    public static string WordOf(string join) => join switch { Or => "or", AndNot => "and not", _ => "and" };

    /// True when the conditions hold. Left to right; no conditions at all hold.
    public static bool Holds(AutomationRule rule, AutomationContext now)
    {
        var holds = true;
        for (var i = 0; i < rule.Conditions.Count; i++)
        {
            var one = AutomationConditions.Holds(rule.Conditions[i], now);
            holds = i == 0 ? one : rule.JoinBefore(i) switch
            {
                Or => holds || one,
                AndNot => holds && !one,
                _ => holds && one,
            };
        }
        return holds;
    }
}
