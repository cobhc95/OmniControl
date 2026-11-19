using OmniControl.Core.Engine.Pointer;

namespace OmniControl.App.Features.Input.Pages.Mouse;

/// How a pointer setting is shown and driven: a number (slider, wheel, typing; up/down actions), a switch (toggle action), or a choice (next/previous actions).
internal enum PointerKind { Number, Switch, Choice }

/// A card that only applies while another setting is on (or off), and the sentence that says so while it does not.
internal sealed record PointerNeeds(PointerValue Value, bool On, string Reason);

/// One pointer setting, described once: its stored id (part of the action ids in hotkeys.json, so it never changes), label, kind, the
/// Windows value behind it (none for the pointer scheme), the step of one action press, its unit, search words, the words its actions
/// are named with, and optionally the wheel level id and OSD element it declares and the setting it needs. The card, its search entry,
/// its actions and its OSD element are all made from this entry; the range and default are Windows' (<see cref="PointerValues"/>).
internal sealed record PointerControl(string Id, string Label, PointerKind Kind, PointerValue? Value, string Keywords, string ActionName,
    int Step = 1, string Unit = "", string? LevelId = null, string? Osd = null, PointerNeeds? Needs = null)
{
    /// Windows' range and default; only for an entry with a <see cref="Value"/>.
    public PointerValueInfo Range => PointerValues.Info(Value!.Value);

    /// A level's change in percent (a wheel gesture's "Change per wheel notch", an action press's default) as a whole number of the
    /// setting's own steps: a share of its range, never less than one step. Pointer speed: 5 % of Windows' 20 speeds = 1.
    public int UnitsOf(int percent)
    {
        var units = Math.Max(Step, (int)Math.Round(Math.Abs(percent) * (Range.Max - Range.Min + 1) / 100.0));
        return Math.Sign(percent) * units;
    }
}

/// Every card of the Pointer settings group, in the order they appear (two per row).
internal static class PointerControls
{
    /// The group's name; it also heads the actions and the search results.
    public const string GroupName = "Pointer settings";

    /// The pointer scheme entry: a choice of Windows' schemes and the user's own.
    public static readonly PointerControl Scheme = new("scheme", "Pointer scheme", PointerKind.Choice, null,
        "pointer cursor scheme theme look black inverted large aero", "pointer scheme");

    public static readonly IReadOnlyList<PointerControl> All = new PointerControl[]
    {
        new("speed", "Pointer speed", PointerKind.Number, PointerValue.Speed, "pointer cursor mouse speed sensitivity fast slow",
            "pointer speed", LevelId: "pointer", Osd: "Pointer speed"),
        new("precision", "Pointer precision", PointerKind.Switch, PointerValue.Precision, "pointer acceleration enhance precision ballistics",
            "enhance pointer precision"),
        new("swap", "Swap buttons", PointerKind.Switch, PointerValue.Swap, "swap left right buttons left-handed primary secondary",
            "swapped left and right buttons"),
        new("doubleclick", "Double-click time", PointerKind.Number, PointerValue.DoubleClickTime, "double click speed time delay",
            "double-click time", Step: 10, Unit: " ms"),
        new("lines", "Lines per wheel notch", PointerKind.Number, PointerValue.ScrollLines, "scroll wheel lines vertical notch",
            "scroll lines", Needs: new PointerNeeds(PointerValue.ScrollScreen, false, "Turn off one screen at a time first.")),
        new("screen", "Scroll one screen at a time", PointerKind.Switch, PointerValue.ScrollScreen, "scroll wheel page screen at a time",
            "scrolling one screen at a time"),
        new("chars", "Characters per tilt notch", PointerKind.Number, PointerValue.ScrollChars, "horizontal sideways scroll tilt characters wheel",
            "sideways scroll characters"),
        new("clicklock", "ClickLock", PointerKind.Switch, PointerValue.ClickLock, "click lock drag select without holding button",
            "ClickLock"),
        new("trails", "Pointer trails", PointerKind.Switch, PointerValue.Trails, "pointer cursor trails visibility",
            "pointer trails"),
        new("traillength", "Trail length", PointerKind.Number, PointerValue.TrailLength, "pointer cursor trails length short long",
            "pointer trail length", Needs: new PointerNeeds(PointerValue.Trails, true, "Turn on trails first.")),
        new("vanish", "Hide while typing", PointerKind.Switch, PointerValue.HideWhileTyping, "hide pointer cursor while typing vanish",
            "hiding the pointer while typing"),
        new("sonar", "Locate pointer on Ctrl", PointerKind.Switch, PointerValue.Sonar, "find locate show pointer cursor location when ctrl is pressed sonar circle",
            "pointer location on Ctrl"),
        new("snap", "Snap to default button", PointerKind.Switch, PointerValue.SnapToDefault, "snap pointer cursor to the default button dialog",
            "snapping to the default button"),
        Scheme,
    };
}
