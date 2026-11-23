using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.States;
using OmniControl.App.Shared.Contracts;
using OmniControl.Core.Engine.Mouse;
using OmniControl.Core.Engine.Trackpad;

namespace OmniControl.App.Features.Input.Mouse;

// Mapped buttons and trackpad gestures as automation rules ("From other pages"): each mapped (layer, input) owns a rule whose Actions list
// is the mapped actions in order, triggered while its layer state is active. Unmapped inputs (Default) have no rule; unmapping removes it.
// While the rule holds, the mapping is active in the hook; when switched off, deleted, or its conditions end, it passes through.
public sealed partial class MouseService
{
    private const string RuleActionPrefix = "mouse.button.";

    public static readonly (MouseButton Button, string Label)[] ButtonLabels =
    {
        (MouseButton.Left, "Left button"), (MouseButton.Right, "Right button"), (MouseButton.Middle, "Middle button"),
        (MouseButton.Back, "Back (button 4)"), (MouseButton.Forward, "Forward (button 5)"),
        (MouseButton.WheelUp, "Wheel up"), (MouseButton.WheelDown, "Wheel down"),
        (MouseButton.TiltLeft, "Tilt left"), (MouseButton.TiltRight, "Tilt right"),
    };

    private readonly FeatureRulesSlot _rules;
    private readonly HashSet<string> _layerStates = new();
    private readonly HashSet<string> _pressStates = new();
    private readonly HashSet<MouseButton> _pressing = new();

    /// A button mapping's automation rule changed (UI thread): the Mouse page refreshes its cards and rule lines.
    public event Action? RulesChanged;

    public static string ButtonLabel(MouseButton button) =>
        ButtonLabels.FirstOrDefault(b => b.Button == button).Label
        ?? TrackpadGestures.All.FirstOrDefault(g => g.Button == button)?.Name
        ?? button.ToString();

    public static string RuleActionId(int layer, MouseButton button) => $"{RuleActionPrefix}{layer}.{button}";

    private static string LayerStateId(InputFamily family, int layer) => $"{StateId(family)}.{layer}";

    public static string PressStateId(MouseButton button) => $"mouse.press.{button}";

    private void InitRules()
    {
        _rules.Changed += OnRulesChanged;
        _rules.Started += OnRuleStateChanged;
        _rules.Ended += OnRuleStateChanged;
        Applied += OnApplied;
        LayersChanged += OnApplied;
        SyncFromAutomation();
    }

    private void OnRuleStateChanged(string id)
    {
        if (ParseRuleKey(id) is not null) _snapshot = TakeSnapshot();
    }

    private void OnApplied()
    {
        SyncToAutomation();
        _snapshot = TakeSnapshot();
    }

    private void OnRulesChanged()
    {
        SyncFromAutomation();
        RulesChanged?.Invoke();
    }

    /// Pushes MouseSettings changes (from the Mouse page, import, restore, or clear layer) into Automation rules.
    private void SyncToAutomation()
    {
        var saved = false;
        var mapped = new HashSet<string>();
        foreach (var key in Settings.Bindings.Keys.ToList())
        {
            if (MouseSettings.ParseKey(key) is not var (layer, button)) continue;
            var actions = Settings.ActionsOf(layer, button);
            if (actions.Count == 0) continue;
            mapped.Add(key);
            EnsureLayerState(button.FamilyOf(), layer);
            EnsurePressState(button);
            if (_rules.Current is not { } rules) continue;
            var id = RuleActionId(layer, button);
            if (Settings.RulesInAutomation.Contains(key) && rules.Find(id) is null) continue;
            if (Settings.RulesInAutomation.Add(key)) saved = true; // set first: EnsureActions notifies Rules.Changed
            rules.EnsureActions(id, RuleName(layer, button), true, ActionSections.Mouse, actions,
                new FeatureTrigger.While(LayerStateId(button.FamilyOf(), layer)), new FeatureTrigger.Press(PressStateId(button)));
        }
        foreach (var key in Settings.RulesInAutomation.Where(k => !mapped.Contains(k)).ToList())
        {
            Settings.RulesInAutomation.Remove(key);
            saved = true;
            _rules.Current?.Remove(RuleActionPrefix + key);
        }
        PrunePressStates(mapped);
        if (saved) Settings.Save();
    }

    /// Ensures rules exist for newly mapped inputs and pulls Automation edits (the storage of truth) back into MouseSettings and the hook snapshot.
    private void SyncFromAutomation()
    {
        var saved = false;
        var mapped = new HashSet<string>();
        foreach (var key in Settings.Bindings.Keys.ToList())
        {
            if (MouseSettings.ParseKey(key) is not var (layer, button)) continue;
            var actions = Settings.ActionsOf(layer, button);
            if (actions.Count == 0) continue;
            mapped.Add(key);
            EnsureLayerState(button.FamilyOf(), layer);
            EnsurePressState(button);
            if (_rules.Current is not { } rules || Settings.RulesInAutomation.Contains(key)) continue;
            Settings.RulesInAutomation.Add(key);
            saved = true;
            rules.EnsureActions(RuleActionId(layer, button), RuleName(layer, button), true, ActionSections.Mouse, actions,
                new FeatureTrigger.While(LayerStateId(button.FamilyOf(), layer)), new FeatureTrigger.Press(PressStateId(button)));
        }
        if (_rules.Current is { } active)
        {
            foreach (var key in Settings.RulesInAutomation.ToList())
            {
                if (!mapped.Contains(key) || MouseSettings.ParseKey(key) is not var (layer, button))
                {
                    Settings.RulesInAutomation.Remove(key);
                    saved = true;
                    active.Remove(RuleActionPrefix + key);
                    continue;
                }
                var id = RuleActionId(layer, button);
                if (active.Find(id) is not { Actions: { Count: > 0 } ruleActions } view) continue;
                if (ruleActions.Count == 1 && ruleActions[0] == $"{MouseActionCatalog.Omni}:{id}")
                {
                    // Migrate legacy proxy rule ("Use <input>") to hold the actual mapped actions.
                    active.EnsureActions(id, RuleName(layer, button), view.On, ActionSections.Mouse, Settings.ActionsOf(layer, button),
                        new FeatureTrigger.While(LayerStateId(button.FamilyOf(), layer)), new FeatureTrigger.Press(PressStateId(button)));
                }
                else
                {
                    if (view.Triggers.Count == 1 && view.Triggers[0] is FeatureTrigger.While w && w.StateId == LayerStateId(button.FamilyOf(), layer) && view.Extras.Length == 0)
                        active.SetTrigger(id, new FeatureTrigger.Press(PressStateId(button)));
                    if (!ruleActions.SequenceEqual(Settings.ActionsOf(layer, button)))
                    {
                        Settings.SetActions(layer, button, ruleActions);
                        saved = true;
                    }
                }
            }
        }
        PrunePressStates(mapped);
        if (saved) Settings.Save();
        if (_rules.Current is not null) _snapshot = TakeSnapshot();
    }

    private void EnsureLayerState(InputFamily family, int layer)
    {
        var id = LayerStateId(family, layer);
        var name = $"{(family == InputFamily.Mouse ? "Mouse" : "Trackpad")} layer {LabelOf(family, layer)}";
        if (_states.Find(id) is { } existing && existing.Name == name) return;
        _states.Register(new StateEntry(id, name, ActionSections.Mouse, StateKind.OnOff, () => StateValue.Of(LayerOf(family) == layer),
            "On while this layer is the active one.", Keywords: $"{family.Noun()} layer {LabelOf(family, layer)} active",
            Wording: new StateWords(name, "is active", "is not active", "changes")));
        _layerStates.Add(id);
    }

    private void EnsurePressState(MouseButton button)
    {
        var id = PressStateId(button);
        if (_pressStates.Contains(id)) return;
        var label = ButtonLabel(button);
        _states.Register(new StateEntry(id, label, ActionSections.Mouse, StateKind.OnOff, () => StateValue.Of(_pressing.Contains(button)),
            "Fires when this input is pressed.", Keywords: $"{label} press click button",
            Wording: new StateWords(label, "is pressed", "is released", "changes"), OneShot: true));
        _pressStates.Add(id);
    }

    private void PrunePressStates(HashSet<string> mapped)
    {
        var needed = mapped.Select(k => MouseSettings.ParseKey(k)?.Button).OfType<MouseButton>().Select(PressStateId).ToHashSet();
        var stale = _pressStates.Where(id => !needed.Contains(id)).ToList();
        if (stale.Count == 0) return;
        _states.Unregister(stale);
        foreach (var id in stale) _pressStates.Remove(id);
    }

    private void NotifyPressState(MouseButton button)
    {
        var id = PressStateId(button);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_pressStates.Contains(id)) return;
            _pressing.Add(button);
            _states.Notify(id);
            Dispatcher.UIThread.Post(() =>
            {
                if (_pressing.Remove(button)) _states.Notify(id);
            });
        });
    }

    private string RuleName(int layer, MouseButton button) =>
        layer == 1 ? ButtonLabel(button) : $"{ButtonLabel(button)} ({LabelOf(button.FamilyOf(), layer)})";

    private static string? ParseRuleKey(string actionId) =>
        actionId.StartsWith(RuleActionPrefix, StringComparison.Ordinal) ? actionId[RuleActionPrefix.Length..] : null;

    private void NotifyLayerActiveStates(InputFamily family)
    {
        var prefix = $"{StateId(family)}.";
        foreach (var id in _layerStates.Where(id => id.StartsWith(prefix, StringComparison.Ordinal))) _states.Notify(id);
    }

    public FeatureRuleView? RuleOf(int layer, MouseButton button) => _rules.Current?.Find(RuleActionId(layer, button));

    public bool HasRule(MouseButton button) =>
        Enumerable.Range(1, MouseSettings.Layers).Any(layer => _rules.Current?.Find(RuleActionId(layer, button)) is not null);

    public void EditRule(int layer, MouseButton button) => _rules.Current?.Edit(RuleActionId(layer, button));

    private void ForgetRules()
    {
        _rules.Changed -= OnRulesChanged;
        _rules.Started -= OnRuleStateChanged;
        _rules.Ended -= OnRuleStateChanged;
        Applied -= OnApplied;
        LayersChanged -= OnApplied;
        _states.Unregister(_layerStates);
        _layerStates.Clear();
        _states.Unregister(_pressStates);
        _pressStates.Clear();
    }
}
