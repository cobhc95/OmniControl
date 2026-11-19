namespace OmniControl.App.Features.Hotkeys;

/// Which settings of its own an action takes when an input (a mouse button, gesture or rule) runs it.
[Flags]
public enum ActionSettings
{
    None = 0,

    /// The program, arguments and folder to start (<see cref="LaunchSettings"/>).
    Launch = 1,

    /// How far a level action moves, in percent (set for every level action by <see cref="HotkeyAction.Takes"/>).
    Step = 2,

    /// How long the cancellable countdown before a power action lasts.
    Countdown = 4,

    /// Whether a shut down, restart or sign out closes programs that object.
    Force = 8,

    /// The repeated-clicking settings (button, speed, when to stop...).
    Clicking = 16,
}

/// What an input chose for the action it runs; an action uses only the settings it <see cref="HotkeyAction.Takes"/> and ignores the rest.
public sealed record RunSettings(LaunchSettings? Launch = null, int? Step = null, int? CountdownSeconds = null, bool Force = false, OptionValues? Options = null);
