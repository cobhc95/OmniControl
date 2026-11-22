using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Automation;

/// The preset rules (<see cref="IPresetTriggers"/>): a Do rule of <see cref="AutomationService"/> whose step is a preset's apply action, made
/// and changed in Automation, runs through the one run path; the Presets window tells of it and opens it. A preset a state switched to is put
/// back to the one before, through the run path, when its triggers stop holding. A deleted preset's rule is parked (off the list) until an
/// Undo brings the preset back.
public sealed class AutomationPresets : IPresetTriggers, IDisposable
{
    private readonly AutomationService _rules;
    private readonly IActionRegistry _actions;
    private readonly IActionRunner _run;
    private readonly PresetTriggersSlot _slot;
    private readonly AutomationFeatureRules _features;
    private readonly Dictionary<string, string> _before = new();   // rule id -> the preset in use before a state switched to its preset
    private readonly List<AutomationRule> _parked = new();          // rules of deleted presets, back on an Undo (this run only)

    private readonly Action<string?, string?> _open;

    /// <param name="features">Opens Automation on a rule.</param>
    /// <param name="open">Opens Presets Manager at a page and group (both null: where it opens by itself).</param>
    public AutomationPresets(AutomationService rules, IActionRegistry actions, IActionRunner run, PresetTriggersSlot slot, AutomationFeatureRules features,
        Action<string?, string?> open)
    {
        (_rules, _actions, _run, _slot, _features, _open) = (rules, actions, run, slot, features, open);
        rules.Changed += OnRulesChanged;
        rules.TriggersChanged += FollowNames;
        rules.StateSeen += PutBack;
        rules.StartingFromState += Remember;
        slot.Presets.Changed += OnPresetsChanged;
        TakeOldPresetField();
        slot.Attach(this);
    }

    /// Rules saved with the preset in their own field: the preset's action is what makes them preset rules now, so the field is emptied once.
    private void TakeOldPresetField()
    {
        var old = _rules.Settings.Rules.Where(r => r.Preset.Length > 0).ToList();
        foreach (var rule in old)
        {
            if (PresetOf(rule) is null) rule.AddAction($"{MouseActionCatalog.Omni}:{rule.Preset}");
            rule.Preset = string.Empty;
        }
        if (old.Count == 0) return;
        _rules.Settings.Save();
        AutomationService.Log($"{old.Count} preset rule(s) now known by their preset's action");
    }

    /// The preset a rule applies (its first preset apply action), or null: what makes it a preset rule.
    public static string? PresetOf(AutomationRule rule) => rule.Kind != AutomationKind.Do ? null
        : rule.Actions.Select(AutomationSteps.ActionIdOf).FirstOrDefault(id => id is not null && PresetShortcuts.IsApplyId(id));

    public event Action? Changed;

    private void OnRulesChanged() => Changed?.Invoke();

    private IEnumerable<AutomationRule> PresetRules => _rules.Settings.Rules.Where(r => PresetOf(r) is not null);

    private AutomationRule? RuleOf(string preset) => PresetRules.FirstOrDefault(r => PresetOf(r) == preset);

    /// The rule's triggers in words, joined as the rule joins them: "When Steam running or 22:00".
    public PresetRule? Of(string preset) =>
        RuleOf(preset) is not { } rule ? null
        : !rule.AllTriggers.Any() ? new PresetRule("No trigger yet") // a rule made by "More options" that has not been given one
        : new PresetRule("When " + string.Concat(rule.AllTriggers.Select((t, i) =>
            (i == 0 ? string.Empty : $" {AutomationJoins.WordOf(rule.TriggerJoinBefore(i))} ") + AutomationTriggers.ChipOf(t, _rules.TriggerSources))));

    /// Opens Custom Rules on the preset's rule; without one, a new rule that switches to it (no trigger yet, named as its action) with its trigger slot focused.
    public void Edit(string preset)
    {
        if (!_rules.UserRules) return;
        if (RuleOf(preset) is { } rule)
        {
            _features.Show(rule);
            return;
        }
        var made = new AutomationRule { Kind = AutomationKind.Do, Name = _actions.Find(preset)?.Title ?? preset };
        made.AddAction($"{MouseActionCatalog.Omni}:{preset}");
        _rules.AddRule(made);
        _features.Show(made, trigger: true);
    }

    /// The tag a preset rule shows ("Presets · Audio and Volume"): its group's page title; empty while its group is not registered.
    public string KindOf(AutomationRule rule) => PresetOf(rule) is { } preset && _slot.Presets.GroupOf(preset) is { } group ? $"Presets · {group.Section}" : string.Empty;

    /// Opens Presets Manager on the rule's preset group, or (null) where it opens by itself.
    public void Open(AutomationRule? rule)
    {
        var group = rule is null || PresetOf(rule) is not { } preset ? null : _slot.Presets.GroupOf(preset);
        _open(group?.Section, group?.Id);
    }

    private void Remember(AutomationRule rule)
    {
        if (PresetOf(rule) is not { } preset || _slot.Presets.InUse(preset) is not { } before || before == preset) return;
        _before[rule.Id] = before;
        Changed?.Invoke(); // its apply action is kept, so the put back can run it
    }

    /// A state changed: a preset a state switched to goes back to the one before once its triggers stop holding (if it is still in use).
    private void PutBack()
    {
        foreach (var (ruleId, before) in _before.ToList())
        {
            var rule = PresetRules.FirstOrDefault(r => r.Id == ruleId);
            if (rule is not null && AutomationService.IsActive(rule) && _rules.TriggersHold(rule, -1)) continue;
            _before.Remove(ruleId);
            if (rule is not null && PresetOf(rule) is { } preset && _slot.Presets.InUse(preset) == preset && _run.Run(before))
                AutomationService.Log($"rule \"{rule.Name}\": its states ended, so the preset in use before was put back");
            Changed?.Invoke();
        }
    }

    /// A preset was deleted (its rule is parked) or came back with an Undo (its rule returns).
    private void OnPresetsChanged(string groupId)
    {
        foreach (var rule in PresetRules.Where(r => _slot.Presets.Exists(PresetOf(r)!) == false).ToList())
        {
            _parked.Add(rule);
            _rules.Remove(rule);
        }
        foreach (var rule in _parked.Where(r => _slot.Presets.Exists(PresetOf(r)!) == true).ToList())
        {
            _parked.Remove(rule);
            _rules.AddRule(rule);
        }
    }

    /// A preset rule that only applies its preset is named as its apply action: a renamed preset renames it.
    private void FollowNames()
    {
        foreach (var rule in PresetRules.Where(r => r.Actions.Count == 1).ToList())
            if (_actions.Find(PresetOf(rule)!)?.Title is { } title && title != rule.Name)
            {
                rule.Name = title;
                _rules.Renamed(rule);
            }
    }

    public void Dispose()
    {
        _slot.Detach(this);
        _slot.Presets.Changed -= OnPresetsChanged;
        _rules.Changed -= OnRulesChanged;
        _rules.TriggersChanged -= FollowNames;
        _rules.StateSeen -= PutBack;
        _rules.StartingFromState -= Remember;
    }
}
