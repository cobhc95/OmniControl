using OmniControl.App.Gestures;
using OmniControl.App.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Rules;

/// <summary>A rule that takes an input a built-in gesture or a mouse or trackpad mapping would otherwise get: a short chip text and the detail.</summary>
public sealed record RuleConflict(string Chip, string Detail);

/// <summary>
/// Finds what a rule overrides. Rules are asked first in the mouse hook, then the built-in gestures, then the mouse and trackpad mappings,
/// so when a rule and one of those want the same input, the rule wins; this says so on the rule's line.
/// </summary>
public static class RuleConflicts
{
    public static RuleConflict? Of(Rule rule, GestureSettings gestures, MouseSettings mouse)
    {
        if (RuleTriggers.Of(rule.Trigger) is not { } trigger || trigger.Kind == TriggerKind.Shortcut) return null;
        return GestureConflict(rule, trigger, gestures) ?? MappingConflict(rule, trigger, mouse);
    }

    private static RuleConflict? GestureConflict(Rule rule, RuleTrigger trigger, GestureSettings gestures)
    {
        if (!gestures.Enabled) return null;
        var areas = rule.Conditions.Select(RuleConditions.AreaOf).OfType<PointerAreaKind>().ToList();
        var keys = rule.Conditions.Select(RuleConditions.KeysOf).OfType<Modifiers>().ToList();
        var wheel = trigger.Button is MouseButton.WheelUp or MouseButton.WheelDown;
        var middle = trigger.Button == MouseButton.Middle && trigger.Kind == TriggerKind.Press;

        foreach (var gesture in GestureCatalog.Triggers)
        {
            if (!gestures.Rules.TryGetValue(gesture.Id, out var stored) || !stored.On) continue;
            var clash = gesture.MiddleClick
                ? middle && keys.Count == 0 && areas.Contains(gesture.Area)
                : wheel && (gesture.Keys == Modifiers.None ? keys.Count == 0 && areas.Contains(gesture.Area) : keys.Contains(gesture.Keys));
            if (clash) return new RuleConflict("Overrides a gesture", $"This rule takes the input of the gesture \"{gesture.Group}: {gesture.Label}\". The rule wins.");
        }
        return null;
    }

    private static RuleConflict? MappingConflict(Rule rule, RuleTrigger trigger, MouseSettings mouse)
    {
        var layers = rule.Layer == Rule.AnyLayer ? Enumerable.Range(1, MouseSettings.Layers) : new[] { rule.Layer };
        var names = mouse.LayersOf(trigger.Family).Labels();
        foreach (var layer in layers)
            if (trigger.Buttons.Any(button => mouse.Get(layer, button) is { } spec && spec != MouseActionCatalog.Default))
                return new RuleConflict("Overrides a mouse mapping",
                    $"Your {trigger.Family.Noun()} mappings give this input an action on the {trigger.Family.Noun()} layer \"{names[layer]}\". Where both apply, the rule wins.");
        return null;
    }
}
