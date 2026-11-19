using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Gestures;

/// One gesture's trigger: scrolling while the pointer is over a screen area (<see cref="Area"/>) or while a set of keys is held
/// (<see cref="Keys"/>), or, with <see cref="MiddleClick"/>, pressing the middle button over a screen area.
/// <see cref="DefaultAction"/> is the action a new gesture starts with: a step action for the wheel, a mouse button action for a middle click.
/// <see cref="DefaultOn"/>: the gesture starts on (for a new user and after Restore defaults).
public sealed record GestureTrigger(string Id, string Group, string Label, string DefaultAction,
    PointerAreaKind Area = PointerAreaKind.Elsewhere, Modifiers Keys = Modifiers.None, bool MiddleClick = false, bool DefaultOn = false);

/// Every gesture trigger, and the "step" actions a wheel can drive (wheel up goes one way, wheel down the other).
/// The level steps (volume, brightness...) are not listed here: every level an OmniControl action declares
/// (<see cref="HotkeyAction.ForLevel"/>) is offered, so they behave and show the OSD exactly as the hotkeys do. Only the steps that are
/// not OmniControl actions are listed below. Middle-click gestures use the Mouse and Trackpad page's action list instead (<see cref="ClickChoices"/>),
/// run by <see cref="MouseService"/>.
public static class GestureCatalog
{
    public const string AreaGroup = "Scroll over a screen area", KeyGroup = "Scroll while holding a key", MiddleGroup = "Middle click over a screen area";
    public const string Opacity = "opacity";

    /// The stored action of a wheel gesture that has none: the scroll is not taken.
    public const string NoAction = "";

    // The master list of gesture triggers (read by the Mouse and Trackpad page, GestureService and the margin drawing). A wheel trigger's
    // default action is a level id declared by a page (HotkeyLevel.Id: "volume", "brightness") or a step listed below.
    public static readonly IReadOnlyList<GestureTrigger> Triggers = new GestureTrigger[]
    {
        new("area.taskbar", AreaGroup, "Taskbar", "volume", PointerAreaKind.Taskbar, DefaultOn: true),
        new("area.top", AreaGroup, "Top edge", "brightness", PointerAreaKind.TopEdge),
        new("area.left", AreaGroup, "Left edge", "volume", PointerAreaKind.LeftEdge),
        new("area.right", AreaGroup, "Right edge", "volume", PointerAreaKind.RightEdge),
        new("area.titlebar", AreaGroup, "Window title bar", NoAction, PointerAreaKind.TitleBar),
        new("area.desktop", AreaGroup, "Desktop", "volume", PointerAreaKind.Desktop),

        new("middle.taskbar", MiddleGroup, "Taskbar", "desktop.taskview", PointerAreaKind.Taskbar, MiddleClick: true),
        new("middle.titlebar", MiddleGroup, "Window title bar", "window.minimize", PointerAreaKind.TitleBar, MiddleClick: true),
        new("middle.desktop", MiddleGroup, "Desktop", "desktop.show", PointerAreaKind.Desktop, MiddleClick: true),

        new("key.ctrl+alt", KeyGroup, "Ctrl + Alt", "volume", Keys: Modifiers.Ctrl | Modifiers.Alt),
        new("key.ctrl+shift", KeyGroup, "Ctrl + Shift", "brightness", Keys: Modifiers.Ctrl | Modifiers.Shift),
        new("key.alt", KeyGroup, "Alt", "volume", Keys: Modifiers.Alt),
        new("key.win", KeyGroup, "Win", "volume", Keys: Modifiers.Win),
    };

    /// The name of a screen area, as its scroll gesture's card shows it.
    public static string AreaName(PointerAreaKind area) => Triggers.First(t => t.Group == AreaGroup && t.Area == area).Label;

    /// A step that is not an OmniControl level: a keystroke per wheel direction, or neither (the runner handles it by id).
    private sealed record Step(string Id, string Category, string Name, IReadOnlyList<ActionOption> Options, string Heading, string? UpKeys = null, string? DownKeys = null);

    private static readonly IReadOnlyList<ActionOption> Stepped = new[] { GestureOptions.Steps, GestureOptions.Reverse };
    private static readonly IReadOnlyList<ActionOption> LevelOptions = new[] { GestureOptions.Amount, GestureOptions.Reverse };

    private static readonly Step[] Steps =
    {
        new(Opacity, ActionSections.Keys, "Window opacity", new[] { GestureOptions.OpacityStep, GestureOptions.Reverse }, "Windows"),
        new("desktop", ActionSections.Keys, "Switch virtual desktop", Stepped, "Virtual desktops", UpKeys: "Win+Ctrl+Left", DownKeys: "Win+Ctrl+Right"),
    };

    /// The action list shown in the picker window for a wheel gesture: every level the master list's actions declare (in the order
    /// registered, under the heading of the action that declares it), then the other steps.
    public static List<PickChoice> Choices(IEnumerable<HotkeyAction> omniActions) =>
        new[] { new PickChoice(NoAction, ActionSections.Mouse, "Default behaviour", Keywords: "no action none unassigned don't intercept normal", Heading: "Basic", Place: ActionSections.Find(ActionSections.Mouse)) }
            .Concat(omniActions.Where(a => a.Level is not null).GroupBy(a => a.Level!.Id).Select(g => g.First())
            .Select(a => new PickChoice(a.Level!.Id, a.Section, a.Level.Name, Keywords: a.Keywords, Options: LevelOptions, Heading: a.PickerHeading, Place: a.SectionPlace)))
            .Concat(Steps.Select(s => new PickChoice(s.Id, s.Category, s.Name, Options: s.Options, Heading: s.Heading, Place: ActionSections.Find(s.Category))))
            .ToList();

    /// The action list for a middle-click gesture: the Mouse and Trackpad page's buttons' list without "Default behaviour", which would only mean
    /// the gesture is off.
    public static List<PickChoice> ClickChoices(IEnumerable<HotkeyAction> omniActions) =>
        MouseActionCatalog.Choices(omniActions).Where(c => c.Spec != MouseActionCatalog.Default).ToList();

    /// The keystroke a listed step sends for one wheel direction; null for a level or a step the runner handles by id.
    public static string? KeysOf(string stepId, bool up) =>
        Steps.FirstOrDefault(s => s.Id == stepId) is { } step ? up ? step.UpKeys : step.DownKeys : null;
}
