using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Mouse;
using OmniControl.Core.Engine.Trackpad;

namespace OmniControl.App.Rules;

/// <summary>
/// How the engine watches for a trigger. A trackpad gesture needs no kind of its own: it arrives through the mouse hook as a press of
/// its own <see cref="MouseButton"/> value (<see cref="TrackpadGestures"/>), so its triggers are Press entries.
/// </summary>
public enum TriggerKind { Press, DoublePress, Hold, Shortcut }

/// <summary>One trigger a rule can start with: its stored id, picker category and name, how it reads in the rule's sentence, and what the engine watches.</summary>
public sealed record RuleTrigger(string Id, string Category, string Name, string Phrase, TriggerKind Kind, MouseButton Button = MouseButton.Left, string Keywords = "");

/// <summary>
/// The master list of rule triggers: one registration per trigger, read by the rule editor's picker, the rule's sentence, the engine
/// and the conflict check. A plain left click is not offered: it is how everything is clicked, and a rule on it could lock the user out.
/// </summary>
public static class RuleTriggers
{
    private const string Clicks = "Mouse buttons", Wheel = "Wheel and tilt", Holds = "Press and hold", Trackpad = "Trackpad", Keyboard = "Keyboard";

    /// <summary>The trigger whose parameter is a shortcut ("key:Ctrl+Alt+K").</summary>
    public const string Shortcut = "key";

    /// <summary>How long a "press and hold" trigger's button must be held.</summary>
    public static readonly ActionOption HoldTime =
        ActionOption.Number("hold", "Hold for", (int)Timing.RuleHold.TotalMilliseconds, 200, 3000, 50, " ms");

    public static readonly IReadOnlyList<RuleTrigger> All = new RuleTrigger[]
    {
        new("click.right", Clicks, "Right click", "right click", TriggerKind.Press, MouseButton.Right),
        new("click.middle", Clicks, "Middle click", "middle click", TriggerKind.Press, MouseButton.Middle, "wheel button"),
        new("click.back", Clicks, "Back button (4)", "Back button", TriggerKind.Press, MouseButton.Back, "side button 4"),
        new("click.forward", Clicks, "Forward button (5)", "Forward button", TriggerKind.Press, MouseButton.Forward, "side button 5"),
        new("click.double", Clicks, "Double click", "double click", TriggerKind.DoublePress, MouseButton.Left, "left twice"),

        new("wheel.up", Wheel, "Scroll up", "scroll up", TriggerKind.Press, MouseButton.WheelUp, "mouse wheel"),
        new("wheel.down", Wheel, "Scroll down", "scroll down", TriggerKind.Press, MouseButton.WheelDown, "mouse wheel"),
        new("tilt.left", Wheel, "Tilt left", "tilt left", TriggerKind.Press, MouseButton.TiltLeft, "wheel sideways"),
        new("tilt.right", Wheel, "Tilt right", "tilt right", TriggerKind.Press, MouseButton.TiltRight, "wheel sideways"),

        new("hold.right", Holds, "Press and hold right button", "hold right button", TriggerKind.Hold, MouseButton.Right, "long press"),
        new("hold.middle", Holds, "Press and hold middle button", "hold middle button", TriggerKind.Hold, MouseButton.Middle, "long press"),
        new("hold.back", Holds, "Press and hold Back button", "hold Back button", TriggerKind.Hold, MouseButton.Back, "long press side"),
        new("hold.forward", Holds, "Press and hold Forward button", "hold Forward button", TriggerKind.Hold, MouseButton.Forward, "long press side"),
    }
    // One trigger per trackpad gesture, from their master list.
    .Concat(TrackpadGestures.All.Select(g =>
        new RuleTrigger($"trackpad.{g.Id}", Trackpad, g.Name, g.Phrase, TriggerKind.Press, g.Button, "touchpad gesture swipe fingers")))
    .Append(new RuleTrigger(Shortcut, Keyboard, "Key or shortcut", "press", TriggerKind.Shortcut, Keywords: "hotkey keyboard"))
    .ToArray();

    /// <summary>The picker's list.</summary>
    public static IReadOnlyList<PickChoice> Choices() => All.Select(t => t.Kind switch
    {
        TriggerKind.Shortcut => new PickChoice(t.Id + ":", t.Category, t.Name, "Ctrl+Alt+K", t.Keywords, ParamLabel: "Shortcut"),
        TriggerKind.Hold => new PickChoice(t.Id, t.Category, t.Name, Keywords: t.Keywords, Options: new[] { HoldTime }),
        _ => new PickChoice(t.Id, t.Category, t.Name, Keywords: t.Keywords),
    }).ToList();

    /// <summary>The trigger a stored text names, or null (empty, or from a newer or edited file).</summary>
    public static RuleTrigger? Of(string stored) => stored.Length == 0 ? null : All.FirstOrDefault(t => t.Id == MouseAction.Parse(stored).Id);

    /// <summary>How the stored trigger reads in the rule's sentence ("right click", "press Ctrl+Alt+K").</summary>
    public static string PhraseOf(string stored)
    {
        if (Of(stored) is not { } trigger) return "(choose a trigger)";
        return trigger.Kind == TriggerKind.Shortcut ? $"{trigger.Phrase} {MouseAction.Parse(stored).Param}" : trigger.Phrase;
    }

    /// <summary>Why the stored trigger can't work as it is (an unreadable shortcut), or null.</summary>
    public static string? ProblemOf(string stored) =>
        Of(stored) is { Kind: TriggerKind.Shortcut } ? HotkeyService.ProblemWith(MouseAction.Parse(stored).Param) : null;
}
