namespace OmniControl.App.Features.Hotkeys;

/// Key combinations no action can have: the emergency key that pauses all automation rules, and the keys Windows owns (they cannot be
/// taken from it, and an action on them would only look broken). Written the way <see cref="HotkeyCapture"/> writes a combination.
public static class ReservedKeys
{
    /// Pauses all automation rules, always, whatever any rule says. Registered by the Automation feature itself; not rebindable.
    public const string Emergency = "Ctrl+Alt+Shift+P";

    private static readonly string[] WindowsOwned = { "Ctrl+Alt+Delete", "Win+L", "Alt+Tab", "Escape", "Ctrl+Shift+Escape" };

    /// Why this combination can't be given to an action, or null when it can.
    public static string? ProblemWith(string gesture) =>
        gesture == Emergency ? $"{Emergency} is reserved: it pauses all automation rules."
        : WindowsOwned.Contains(gesture) ? $"{gesture} belongs to Windows."
        : null;
}
