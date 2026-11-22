using OmniControl.App.Shared.Contracts;
using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Automation;

/// The rules features act through (<see cref="IFeatureRules"/>): ordinary Do rules of <see cref="AutomationService"/>. Which feature a rule
/// belongs to is read from its actions: each that is a feature's own rule action (<see cref="HotkeyAction.FeatureRule"/>) puts it on that
/// action's page (<see cref="HotkeyAction.Category"/>) and on the "From other pages" tab; nothing is stored about it.
public sealed class AutomationFeatureRules : IFeatureRules, IDisposable
{
    /// The Automation page's tab of these rules, and of every other rule (the user's own, a preset's).
    public const string Tab = "From other pages", CustomTab = "Custom Rules";

    private readonly AutomationService _rules;
    private readonly IActionCatalogue _actions;
    private readonly FeatureRulesSlot _slot;
    private readonly Action<string, string?> _openPage;

    /// <param name="openPage">Opens a page by its title, at a tab (null: the tab it is on).</param>
    public AutomationFeatureRules(AutomationService rules, IActionCatalogue actions, FeatureRulesSlot slot, Action<string, string?> openPage)
    {
        (_rules, _actions, _slot, _openPage) = (rules, actions, slot, openPage);
        rules.Changed += OnChanged;
        rules.Released += OnReleased;
        slot.Attach(this);
    }

    public event Action? Changed;
    public event Action<string>? Ended;

    private void OnChanged() => Changed?.Invoke();

    private void OnReleased(AutomationRule rule)
    {
        foreach (var action in OwnersOf(rule)) Ended?.Invoke(action.Id);
    }

    /// The feature rule actions a rule holds: each names a page the rule shows on.
    public IReadOnlyList<HotkeyAction> OwnersOf(AutomationRule rule) => rule.Kind != AutomationKind.Do ? Array.Empty<HotkeyAction>()
        : rule.Actions.Select(AutomationSteps.ActionIdOf).OfType<string>().Select(_actions.Find).OfType<HotkeyAction>().Where(a => a.FeatureRule).ToList();

    /// The page a feature's rule was made on (the first owner's), or null for any other rule.
    public string? PageOf(AutomationRule rule) => OwnersOf(rule).FirstOrDefault()?.Category;

    /// Opens the page the rule was made on.
    public void Open(AutomationRule rule)
    {
        if (PageOf(rule) is { } page) _openPage(page, null);
    }

    private AutomationRule? RuleOf(string actionId) =>
        _rules.Settings.Rules.FirstOrDefault(r => r.Kind == AutomationKind.Do && r.Actions.Any(a => AutomationSteps.ActionIdOf(a) == actionId));

    public FeatureRuleView? Find(string actionId)
    {
        if (RuleOf(actionId) is not { } rule) return null;
        var known = rule.AllTriggers.Select(Feature).OfType<FeatureTrigger>().ToList();
        return new FeatureRuleView(rule.On, known, ExtrasOf(rule, Math.Max(known.Select(t => t.GetType()).Distinct().Count(), 1)), _rules.UserRules);
    }

    /// What was added in Automation beyond the triggers its origin shows (a feature: one of each kind it knows; a preset row: all) and its one
    /// action ("1 extra condition", "2 extra triggers, 1 extra action").
    public static string ExtrasOf(AutomationRule rule, int shownTriggers)
    {
        static string Part(int n, string noun) => n == 0 ? string.Empty : $"{n} extra {noun}{(n == 1 ? string.Empty : "s")}";
        return string.Join(", ", new[] { Part(rule.AllTriggers.Count() - shownTriggers, "trigger"), Part(rule.Conditions.Count, "condition"), Part(rule.Actions.Count - 1, "action") }
            .Where(p => p.Length > 0));
    }

    public void Ensure(string actionId, string name, bool on, params FeatureTrigger[] triggers)
    {
        if (RuleOf(actionId) is not null) return;
        var rule = new AutomationRule { Kind = AutomationKind.Do, Name = name, On = on };
        rule.AddAction($"{MouseActionCatalog.Omni}:{actionId}");
        foreach (var trigger in triggers) rule.AddTrigger(Stored(trigger));
        _rules.AddRule(rule);
        AutomationService.Log($"rule \"{name}\" made from its page's settings ({(on ? "on" : "off")})");
    }

    public void RemoveTrigger<T>(string actionId) where T : FeatureTrigger
    {
        if (RuleOf(actionId) is not { } rule) return;
        var places = rule.AllTriggers.Select((t, i) => (t, i)).Where(p => Feature(p.t) is T).Select(p => p.i).Reverse().ToList();
        if (places.Count == 0 || places.Count == rule.AllTriggers.Count()) return;
        foreach (var place in places) rule.RemoveTrigger(place);
        _rules.Edited(rule);
    }

    public void SetOn(string actionId, bool on)
    {
        if (RuleOf(actionId) is not { } rule || rule.On == on) return;
        rule.On = on;
        _rules.Edited(rule);
    }

    public void SetTrigger(string actionId, FeatureTrigger trigger)
    {
        if (RuleOf(actionId) is not { } rule) return;
        var stored = Stored(trigger);
        var place = rule.AllTriggers.Select((t, i) => (t, i)).FirstOrDefault(p => Feature(p.t)?.GetType() == trigger.GetType());
        if (place.t is null) rule.AddTrigger(stored);
        else if (place.t == stored) return;
        else if (place.i == 0) rule.Trigger = stored;
        else rule.MoreTriggers[place.i - 1] = stored;
        _rules.Edited(rule);
    }

    public void Edit(string actionId) => Show(RuleOf(actionId));

    /// A rule the Automation page brings into view and focuses (<see cref="TakeShown"/>); raised after the page was opened.
    public event Action? RuleShown;

    private (string Id, bool Trigger)? _shown;

    /// The rule to show (its id; whether its trigger slot takes the focus), once: the page made by the opening takes it, else the open page on <see cref="RuleShown"/>.
    public (string Id, bool Trigger)? TakeShown()
    {
        var shown = _shown;
        _shown = null;
        return shown;
    }

    /// Opens Automation on this rule's tab (a feature's rule: From other pages, any other: Custom Rules), or on From other pages without one.
    public void Show(AutomationRule? rule, bool trigger = false)
    {
        if (!_rules.UserRules) return;
        _shown = rule is null ? null : (rule.Id, trigger);
        _openPage(ActionSections.Automation, rule is not null && PageOf(rule) is null ? CustomTab : Tab);
        RuleShown?.Invoke();
    }

    private static string Stored(FeatureTrigger trigger) => trigger switch
    {
        FeatureTrigger.Between w => MouseAction.Compose(AutomationTriggers.Between, AutomationConditions.WindowOptions, new Dictionary<string, string>
        {
            [AutomationConditions.FromHour.Key] = $"{w.Start / 60}", [AutomationConditions.FromMinute.Key] = $"{w.Start % 60}",
            [AutomationConditions.ToHour.Key] = $"{w.End / 60}", [AutomationConditions.ToMinute.Key] = $"{w.End % 60}",
        }),
        FeatureTrigger.While s => AutomationTriggers.WhileOn(s.StateId),
        FeatureTrigger.Starts => AutomationTriggers.Start,
        FeatureTrigger.Every e => MouseAction.Compose(AutomationTriggers.Timer, new[] { AutomationTriggers.Minutes, AutomationTriggers.Repeat },
            new Dictionary<string, string> { [AutomationTriggers.Minutes.Key] = $"{e.Minutes}", [AutomationTriggers.Repeat.Key] = "1" }),
        _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
    };

    /// A stored trigger in a feature's words, or null when it is none a feature knows.
    private static FeatureTrigger? Feature(string stored)
    {
        var trigger = MouseAction.Parse(stored);
        return trigger.Id switch
        {
            AutomationTriggers.Between when AutomationConditions.MinutesOf(trigger.Options) is var (from, until) => new FeatureTrigger.Between(from, until),
            AutomationTriggers.Start => new FeatureTrigger.Starts(),
            AutomationTriggers.Timer when trigger.Options.Bool(AutomationTriggers.Repeat) => new FeatureTrigger.Every(trigger.Options.Int(AutomationTriggers.Minutes)),
            _ => AutomationTriggers.WhileOnState(stored) is { } state ? new FeatureTrigger.While(state) : null,
        };
    }

    public void Dispose()
    {
        _slot.Detach(this);
        _rules.Changed -= OnChanged;
        _rules.Released -= OnReleased;
    }
}
