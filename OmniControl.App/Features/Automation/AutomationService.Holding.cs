namespace OmniControl.App.Features.Automation;

// Rules that last: a rule holding a feature's own rule action (HotkeyAction.FeatureRule) with a trigger that can hold (a state, a time window)
// starts, through the one run path, when its triggers and conditions begin to hold, and is released when they stop, it is switched off,
// deleted or the safe start is on (pausing leaves it). The feature ends its effect on the release.
public sealed partial class AutomationService
{
    private readonly Dictionary<string, AutomationRule> _held = new(); // rule id -> a lasting rule that holds now

    /// A lasting rule started holding (UI thread).
    internal event Action<AutomationRule>? Held;

    /// A lasting rule stopped holding (UI thread).
    internal event Action<AutomationRule>? Released;

    internal bool IsHeld(AutomationRule rule) => _held.ContainsKey(rule.Id);

    /// True for a lasting rule: it holds a feature's rule action and a trigger that can hold (or is owned by a feature with its own actions).
    internal bool IsWhile(AutomationRule rule) =>
        rule.Kind == AutomationKind.Do
        && (rule.AllTriggers.Any(t => AutomationTriggers.CanHold(t, _sources)) || rule.Feature.Length > 0)
        && IsFeatureRule(rule);

    /// True for a rule holding a feature's own rule action (or owned by a feature): it keeps running while rules are paused (only the user's own and preset rules stop).
    internal bool IsFeatureRule(AutomationRule rule) =>
        rule.Feature.Length > 0
        || rule.Actions.Any(a => AutomationSteps.ActionIdOf(a) is { } id && _hotkeys.Find(id) is { FeatureRule: true });

    /// True when pausing, the feature switch or the safe start keeps this rule from running.
    internal bool Stopped(AutomationRule rule) => AutomationPause.Stops(SessionOff, Paused, UserRules, IsFeatureRule(rule));

    /// True for a Do rule that something must watch for now: switched on and not stopped.
    private bool Needs(AutomationRule rule) => IsActive(rule) && !Stopped(rule);

    /// True for any rule (Do or Block) whose triggers/states must be kept fresh now.
    private bool Watches(AutomationRule rule) => rule is { On: true, IsComplete: true } && !Stopped(rule) && Watched(rule).Any();

    /// Starts the lasting rules that began to hold and releases those that stopped (a look, a state change, an edit, a pause).
    private void LookAtHeld()
    {
        AutomationContext? now = null;
        foreach (var rule in Settings.Rules.ToList())
        {
            var holds = Needs(rule) && IsWhile(rule) && TriggersHold(rule, -1, assumeOneShot: rule.Feature.Length > 0)
                && AutomationJoins.Holds(rule, now ??= NewContext());
            if (holds && _held.TryAdd(rule.Id, rule))
            {
                if (rule.Feature.Length == 0) TryStart(rule);
                else Held?.Invoke(rule);
            }
            else if (!holds) Release(rule.Id);
        }
        foreach (var gone in _held.Keys.Where(id => Settings.Rules.All(r => r.Id != id)).ToList()) Release(gone);
    }

    private void Release(string ruleId)
    {
        if (_held.Remove(ruleId, out var rule)) Released?.Invoke(rule);
    }
}
