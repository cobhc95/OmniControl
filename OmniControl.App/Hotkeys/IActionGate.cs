namespace OmniControl.App.Hotkeys;

/// <summary>
/// The one question asked before any master action runs, whatever started it (a hotkey, a mouse button, a gesture, a rule):
/// "is this action allowed right now?". <see cref="HotkeyRegistry"/> asks it at its single run path; the Automation feature answers.
/// </summary>
public interface IActionGate
{
    /// <summary>Why the action must not run now (one plain sentence), or null when it may.</summary>
    string? BlockReason(HotkeyAction action);
}
