using OmniControl.App.Hotkeys;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// Makes the master-list entries (<see cref="HotkeyAction"/>) of the page's controls, so the id, title and heading rules are written once.
/// There is one action per control, not one per display: a display scope (all, or the ones the user picks) is chosen on the Shortcuts page.
/// </summary>
internal static class DisplayActions
{
    private const string Noun = "displays";

    /// <summary>
    /// The "up" and "down" actions of a level control: they appear on the Shortcuts page and in the pickers, and the wheel gestures can
    /// drive the level by id. <paramref name="idKey"/> names the control in the action ids and is the stored id of its gesture level
    /// ("brightness", "sw.temperature"), so it never changes. <paramref name="devices"/> is the display list of the scope chooser; null
    /// for a control that has no display (the desktop's saturation).
    /// </summary>
    public static IEnumerable<HotkeyAction> Level(LevelSpec spec, string idKey, string category, string group,
        Action<IReadOnlyList<string>?, int> step, Func<IReadOnlyList<HotkeyDevice>>? devices)
    {
        var level = new HotkeyLevel(idKey, spec.Label);
        var noun = devices is null ? null : Noun;
        yield return HotkeyAction.ForLevel($"displays.{idKey}.up", $"{spec.Label} up", category, spec.Keywords, level, +1, step, noun, devices, group);
        yield return HotkeyAction.ForLevel($"displays.{idKey}.down", $"{spec.Label} down", category, spec.Keywords, level, -1, step, noun, devices, group);
    }

    /// <summary>A one-off action (next colour preset, turn the screen off, reset...) that acts on the chosen displays.</summary>
    public static HotkeyAction Once(string id, string title, string keywords, string category, string group,
        Action<IReadOnlyList<string>?> run, Func<IReadOnlyList<HotkeyDevice>>? devices) =>
        new(id, title, category, "monitor display screen " + keywords, run, devices is null ? null : Noun, devices, group: group);
}
