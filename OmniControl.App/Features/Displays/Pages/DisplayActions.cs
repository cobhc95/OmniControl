using OmniControl.App.Features.States;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Displays.Pages;

/// Makes the master-list entries (<see cref="HotkeyAction"/>) of the page's controls, so the id, title and heading rules are written once.
/// There is one action per control, not one per display: a display scope (all, or the ones the user picks) is chosen on the Shortcuts page.
internal static class DisplayActions
{
    private const string Noun = "displays";

    /// The "up" and "down" actions of a level control: they appear on the Shortcuts page and in the pickers, and the wheel gestures can
    /// drive the level by id. <paramref name="idKey"/> names the control in the action ids and is the stored id of its gesture level
    /// ("brightness", "sw.temperature"), so it never changes. <paramref name="devices"/> is the display list of the scope chooser; null
    /// for a control that has no display (the desktop's saturation).
    public static IEnumerable<HotkeyAction> Level(LevelSpec spec, string idKey, string category, string group,
        Action<IReadOnlyList<string>?, int> step, Func<IReadOnlyList<HotkeyDevice>>? devices)
    {
        var level = new HotkeyLevel(idKey, spec.Label);
        var noun = devices is null ? null : Noun;
        yield return HotkeyAction.ForLevel($"displays.{idKey}.up", $"{spec.Label} up", category, spec.Keywords, level, +1, step, noun, devices, group, spec.Section, suggestedKey: spec.SuggestedUp);
        yield return HotkeyAction.ForLevel($"displays.{idKey}.down", $"{spec.Label} down", category, spec.Keywords, level, -1, step, noun, devices, group, spec.Section, suggestedKey: spec.SuggestedDown);
    }

    /// A one-off action (next colour preset, turn the screen off, reset...) that acts on the chosen displays.
    public static HotkeyAction Once(string id, string title, string keywords, string category, string group, string subgroup,
        Action<IReadOnlyList<string>?> run, Func<IReadOnlyList<HotkeyDevice>>? devices, string? suggestedKey = null, string? shortName = null, string? icon = null,
        IReadOnlyList<string>? suggestedIcons = null, IReadOnlyList<ActionOption>? manageSettings = null, bool screensOff = false, string? common = null, string? suggestedKeySet = null, ActionFamily? family = null, string? familyRole = null, string? highlightState = null) =>
        new(id, title, category, "monitor display screen " + keywords, run, devices is null ? null : Noun, devices, group: group, subgroup: subgroup)
        {
            SuggestedIcons = suggestedIcons,
            ManageSettings = manageSettings,
            SuggestedKey = suggestedKey,
            SuggestedKeySet = suggestedKeySet,
            Short = shortName,
            Icon = icon,
            ScreensOff = screensOff,
            Common = common,
            Family = family,
            FamilyRole = familyRole,
            HighlightStateId = highlightState,
        };
}
