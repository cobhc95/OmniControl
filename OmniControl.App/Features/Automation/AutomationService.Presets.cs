using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Automation;

// The preset manager's trigger chips: each preset with triggers is one ordinary Do rule ("Apply preset "Gaming""), its step the preset's
// apply action, its triggers the chips with their "or" / "and" joins, so it runs through the one run path and shows on the Automation page.
// A preset a state switched to is put back to the one before when its triggers stop holding.
public sealed partial class AutomationService : IPresetTriggers
{
    private readonly PresetTriggersSlot _presets;
    private readonly Dictionary<string, string> _presetBefore = new(); // rule id -> the preset in use before a state switched to its preset

    IEnumerable<string> IPresetTriggers.Presets => Settings.Rules.Where(r => r.Preset.Length > 0 && r.AllTriggers.Any()).Select(r => r.Preset);

    private AutomationRule? PresetRule(string preset) => Settings.Rules.FirstOrDefault(r => r.Kind == AutomationKind.Do && r.Preset == preset);

    IReadOnlyList<PresetTrigger> IPresetTriggers.Of(string preset) =>
        PresetRule(preset) is { } rule
            ? rule.AllTriggers.Select((t, i) => new PresetTrigger(AutomationTriggers.ChipOf(t, _sources), i == 0 ? string.Empty : AutomationJoins.WordOf(rule.TriggerJoinBefore(i)))).ToList()
            : Array.Empty<PresetTrigger>();

    IReadOnlyList<PickChoice> IPresetTriggers.StateChoices() => AutomationTriggers.StateChoices(_sources);

    IReadOnlyList<PickChoice> IPresetTriggers.ActionChoices() => AutomationTriggers.ActionChoices(_hotkeys.PickerActions);

    void IPresetTriggers.Add(string preset, string title, string stored)
    {
        var rule = PresetRule(preset);
        if (rule is null)
        {
            rule = new AutomationRule { Kind = AutomationKind.Do, Name = title, Preset = preset };
            rule.AddAction($"{MouseActionCatalog.Omni}:{preset}");
            Settings.Rules.Add(rule);
            _hotkeys.Register(MakeRuleAction(rule));
        }
        if (rule.AllTriggers.Contains(stored)) return;
        rule.AddTrigger(stored);
        rule.On = true;
        Edited(rule);
    }

    void IPresetTriggers.Remove(string preset, int index)
    {
        if (PresetRule(preset) is not { } rule || index >= rule.AllTriggers.Count()) return;
        rule.RemoveTrigger(index);
        if (rule.AllTriggers.Any()) Edited(rule);
        else Remove(rule);
    }

    void IPresetTriggers.FlipJoin(string preset, int index)
    {
        if (PresetRule(preset) is not { } rule || index < 1) return;
        rule.SetTriggerJoin(index, rule.TriggerJoinBefore(index) == AutomationJoins.And ? AutomationJoins.Or : AutomationJoins.And);
        Edited(rule);
    }

    /// The triggers hold with trigger <paramref name="fired"/> taken as true (−1: none, only the states as they are now), read left to
    /// right as conditions are. With only "or" joins any fired trigger holds, as for every other rule.
    private bool TriggersHold(AutomationRule rule, int fired)
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

    /// A state changed: a preset a state switched to goes back to the one before once its triggers stop holding (if it is still in use).
    private void PutPresetsBack()
    {
        foreach (var (ruleId, before) in _presetBefore.ToList())
        {
            var rule = Settings.Rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule is not null && IsActive(rule) && TriggersHold(rule, -1)) continue;
            _presetBefore.Remove(ruleId);
            if (rule is null || _presets.Presets.InUse(rule.Preset) != rule.Preset) continue;
            Log($"rule \"{rule.Name}\": its states ended, so the preset in use before is put back");
            _presets.Presets.Apply(before);
        }
    }

    /// Starts a preset rule from a state change, remembering the preset in use before so it can come back.
    private void StartFromState(AutomationRule rule, Shared.Contracts.RunChain? chain)
    {
        var before = rule.Preset.Length > 0 ? _presets.Presets.InUse(rule.Preset) : null;
        if (StartFrom(rule, chain) && before is not null && before != rule.Preset) _presetBefore[rule.Id] = before;
    }

    /// A preset rule is named as its preset's apply action: a renamed preset renames its rule.
    private void FollowPresetNames()
    {
        foreach (var rule in Settings.Rules.Where(r => r.Preset.Length > 0).ToList())
            if (_hotkeys.Find(rule.Preset)?.Title is { } title && title != rule.Name)
            {
                rule.Name = title;
                Renamed(rule);
            }
    }
}
