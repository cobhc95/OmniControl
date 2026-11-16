namespace OmniControl.App.Hotkeys;

/// <summary>Which settings of its own an action takes when an input (a mouse button, gesture or rule) runs it.</summary>
[Flags]
public enum ActionSettings
{
    None = 0,

    /// <summary>The program, arguments and folder to start (<see cref="LaunchSettings"/>).</summary>
    Launch = 1,

    /// <summary>How far a level action moves, in percent (set for every level action by <see cref="HotkeyAction.Takes"/>).</summary>
    Step = 2,

    /// <summary>How long the cancellable countdown before a power action lasts.</summary>
    Countdown = 4,

    /// <summary>Whether a shut down, restart or sign out closes programs that object.</summary>
    Force = 8,
}

/// <summary>What an input chose for the action it runs; an action uses only the settings it <see cref="HotkeyAction.Takes"/> and ignores the rest.</summary>
public sealed record RunSettings(LaunchSettings? Launch = null, int? Step = null, int? CountdownSeconds = null, bool Force = false);
