namespace OmniControl.App.Hotkeys;

/// <summary>
/// What a feature whose actions come and go at run time (a preset's "Apply preset" action, made while it has a shortcut) needs of the
/// master list of actions. <see cref="HotkeyRegistry"/> is the one; the tests give a fake, so no shortcut is registered on the PC.
/// </summary>
public interface IActionRegistry
{
    void Register(HotkeyAction action);

    /// <summary>Takes the action out of the master list; its saved shortcut stays in hotkeys.json (a deleted preset that is put back gets it again).</summary>
    void Unregister(string id);

    HotkeyAction? Find(string id);

    /// <summary>The ids starting with <paramref name="prefix"/> that hotkeys.json keeps a shortcut for (registered or not).</summary>
    IReadOnlyList<string> SavedWithShortcut(string prefix);

    /// <summary>Why <paramref name="gesture"/> cannot be the shortcut of the action <paramref name="id"/> (unreadable, no Ctrl, Alt or Win, or another action has it); null when it can.</summary>
    string? ProblemWith(string gesture, string id);
}
