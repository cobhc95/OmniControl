using OmniControl.App.Shared.Contracts;
using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Automation;

/// The rules features act through (<see cref="IFeatureRules"/>): ordinary Do rules of <see cref="AutomationService"/>. Which feature a rule
/// belongs to is read from its actions: each that is a feature's own rule action (<see cref="HotkeyAction.FeatureRule"/>) puts it on that
/// action's page (<see cref="HotkeyAction.Category"/>) and on the "From other pages" tab; nothing is stored about it.
public sealed class AutomationFeatureRules : IFeatureRules, IDisposable
{
    /// The Automation page's tab of these rules.
    public const string Tab = "From other pages";

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

    public FeatureRuleView? Find(string actionId) =>
        RuleOf(actionId) is not { } rule ? null : new FeatureRuleView(rule.On, WindowOf(rule), ExtrasOf(rule));

    private static FeatureTrigger.Between? WindowOf(AutomationRule rule) =>
        rule.AllTriggers.Select(MouseAction.Parse).FirstOrDefault(t => t.Id == AutomationTriggers.Between) is { } window
        && AutomationConditions.MinutesOf(window.Options) is var (from, until) ? new FeatureTrigger.Between(from, until) : null;

    /// What was added in Automation beyond the feature's one trigger and one action ("1 extra condition", "2 extra triggers, 1 extra action").
    private static string ExtrasOf(AutomationRule rule)
    {
        static string Part(int n, string noun) => n == 0 ? string.Empty : $"{n} extra {noun}{(n == 1 ? string.Empty : "s")}";
        return string.Join(", ", new[] { Part(rule.AllTriggers.Count() - 1, "trigger"), Part(rule.Conditions.Count, "condition"), Part(rule.Actions.Count - 1, "action") }
            .Where(p => p.Length > 0));
    }

    public void Ensure(string actionId, string name, FeatureTrigger trigger, bool on)
    {
        if (RuleOf(actionId) is not null) return;
        var rule = new AutomationRule { Kind = AutomationKind.Do, Name = name, On = on };
        rule.AddAction($"{MouseActionCatalog.Omni}:{actionId}");
        rule.AddTrigger(Stored(trigger));
        _rules.AddRule(rule);
        AutomationService.Log($"rule \"{name}\" made from its page's settings ({(on ? "on" : "off")})");
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
        var kind = MouseAction.Parse(stored).Id;
        var place = rule.AllTriggers.Select((t, i) => (t, i)).FirstOrDefault(p => MouseAction.Parse(p.t).Id == kind);
        if (place.t is null) rule.AddTrigger(stored);
        else if (place.t == stored) return;
        else if (place.i == 0) rule.Trigger = stored;
        else rule.MoreTriggers[place.i - 1] = stored;
        _rules.Edited(rule);
    }

    public void Edit(string actionId) => _openPage(ActionSections.Automation, Tab);

    private static string Stored(FeatureTrigger trigger) => trigger switch
    {
        FeatureTrigger.Between w => MouseAction.Compose(AutomationTriggers.Between, AutomationConditions.WindowOptions, new Dictionary<string, string>
        {
            [AutomationConditions.FromHour.Key] = $"{w.Start / 60}", [AutomationConditions.FromMinute.Key] = $"{w.Start % 60}",
            [AutomationConditions.ToHour.Key] = $"{w.End / 60}", [AutomationConditions.ToMinute.Key] = $"{w.End % 60}",
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
    };

    public void Dispose()
    {
        _slot.Detach(this);
        _rules.Changed -= OnChanged;
        _rules.Released -= OnReleased;
    }
}
