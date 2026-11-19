using System.Text.Json;
using OmniControl.App.Features.Input.Gestures;
using OmniControl.App.Features.Input.Rules;

namespace OmniControl.App.Features.Input.Mouse;

/// What "Restore defaults" on the Mouse and Trackpad page covers (pure): every mapping, layer and option (<see cref="MouseSettings"/>),
/// Windows' pointer settings and pointers, which go back to what the user had before OmniControl first changed one (the pointer backup),
/// the built-in gestures (every gesture at its default with its default action, the switch on, the edge strip at its default, every app)
/// and the custom gesture rules with their approved commands (deleted; the undo brings them back).
/// <see cref="Pointer"/> is null while OmniControl has never changed a pointer setting: there is then nothing to put back.
public sealed class MouseRestoreState
{
    public MouseSettings Mouse { get; set; } = new();
    public PointerOriginal? Pointer { get; set; }
    public GestureSettings Gestures { get; set; } = new();
    public RuleSettings Rules { get; set; } = new();

    /// The defaults: the new-install mouse settings, gestures and (no) rules, and the pointer backup as the pointer part (when there is one to put back).
    public static MouseRestoreState DefaultsOf(MouseRestoreState current, PointerOriginal? original) =>
        new() { Mouse = MouseSettings.Defaults(), Pointer = current.Pointer is null ? null : original };

    /// The line the question adds when custom rules exist (they are deleted), else null.
    public static string? WarningOf(int ruleCount) => ruleCount switch
    {
        0 => null,
        1 => "Deletes your custom rule",
        _ => $"Deletes your {ruleCount} custom rules",
    };

    public static bool Same(MouseRestoreState a, MouseRestoreState b) =>
        Json(a.Mouse) == Json(b.Mouse) && PointerSame(a.Pointer, b.Pointer) && GesturesSame(a.Gestures, b.Gestures)
        && Json(a.Rules.Rules) == Json(b.Rules.Rules) && a.Rules.Approved.SetEquals(b.Rules.Approved);

    /// A gesture with no entry counts as its trigger's default (on or off) with its default action.
    private static bool GesturesSame(GestureSettings a, GestureSettings b) =>
        a.Enabled == b.Enabled && a.EdgeStrip == b.EdgeStrip
        && a.AreasScope == b.AreasScope && a.MiddleScope == b.MiddleScope && a.KeysScope == b.KeysScope
        && GestureCatalog.Triggers.All(t => Json(RuleOf(a, t)) == Json(RuleOf(b, t)));

    private static GestureRule RuleOf(GestureSettings settings, GestureTrigger trigger) =>
        settings.Rules.TryGetValue(trigger.Id, out var rule) ? rule : new GestureRule { On = trigger.DefaultOn, Action = trigger.DefaultAction };

    /// Equal values and pointers (the day the copy was made does not count, nor the order of the entries).
    private static bool PointerSame(PointerOriginal? a, PointerOriginal? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.Values.Count == b.Values.Count && a.Values.All(v => b.Values.TryGetValue(v.Key, out var other) && other == v.Value)
            && Json(a.Cursors) == Json(b.Cursors);
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
