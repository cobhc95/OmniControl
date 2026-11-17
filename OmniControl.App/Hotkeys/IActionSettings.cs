namespace OmniControl.App.Hotkeys;

/// <summary>
/// What the master list needs to know about an action's settings without knowing where they are defined: which settings the action takes
/// from whatever runs it (a program to start, how far a level moves, a power countdown), and what running it with a set of values means.
/// Given to <see cref="HotkeyRegistry"/> once at start-up (the option definitions live above it, with the mouse actions).
/// </summary>
public interface IActionSettings
{
    /// <summary>The settings of the action itself, the ones its hotkeys and rules use as <see cref="HotkeyAction.Defaults"/>; empty when it takes none.</summary>
    IReadOnlyList<ActionOption> OptionsOf(HotkeyAction action);

    /// <summary>What running the action with these values means.</summary>
    RunSettings RunOf(OptionValues values);
}
