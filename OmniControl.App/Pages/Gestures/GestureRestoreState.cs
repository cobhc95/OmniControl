using System.Text.Json;
using OmniControl.App.Gestures;
using OmniControl.App.Rules;

namespace OmniControl.App.Pages.Gestures;

/// <summary>
/// What "Restore defaults" on the Gestures page covers (pure): the built-in gestures (<see cref="GestureSettings"/>: every gesture off with its
/// default action, the switch on, the edge strip at its default, every app) and the custom rules with their approved commands (<see cref="RuleSettings"/>:
/// deleted; the undo brings them back).
/// </summary>
public sealed class GestureRestoreState
{
    public GestureSettings Gestures { get; set; } = new();
    public RuleSettings Rules { get; set; } = new();

    public static GestureRestoreState Defaults() => new();

    /// <summary>The line the question adds when custom rules exist (they are deleted), else null.</summary>
    public static string? WarningOf(int ruleCount) => ruleCount switch
    {
        0 => null,
        1 => "Deletes your custom rule",
        _ => $"Deletes your {ruleCount} custom rules",
    };

    public const string Scope = "Turns Enable gestures on, puts the edge strip width back and switches every built-in gesture off with its default action, every app.";

    /// <summary>True when both states hold the same: a gesture with no entry counts as off with its default action.</summary>
    public static bool Same(GestureRestoreState a, GestureRestoreState b) =>
        a.Gestures.Enabled == b.Gestures.Enabled && a.Gestures.EdgeStrip == b.Gestures.EdgeStrip && a.Gestures.AreasScope == b.Gestures.AreasScope
        && GestureCatalog.Triggers.All(t => Json(RuleOf(a.Gestures, t)) == Json(RuleOf(b.Gestures, t)))
        && Json(a.Rules.Rules) == Json(b.Rules.Rules) && a.Rules.Approved.SetEquals(b.Rules.Approved);

    private static GestureRule RuleOf(GestureSettings settings, GestureTrigger trigger) =>
        settings.Rules.TryGetValue(trigger.Id, out var rule) ? rule : new GestureRule { Action = trigger.DefaultAction };

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
