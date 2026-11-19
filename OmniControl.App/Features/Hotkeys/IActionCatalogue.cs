namespace OmniControl.App.Features.Hotkeys;

/// What a feature that only reads the master list of actions needs of it (<see cref="HotkeyRegistry"/> is the one).
public interface IActionCatalogue
{
    /// Every registered action; consumers follow its changes, so registration order never matters.
    ObservableCollection<HotkeyAction> Actions { get; }

    /// What the action pickers list: every action, then the buttons of their lists (<see cref="HotkeyAction.Command"/>).
    IEnumerable<HotkeyAction> PickerActions { get; }

    HotkeyAction? Find(string id);

    /// The section (name, icon, place) of a page or an extra section; null when there is none by that name.
    ActionSection? SectionOf(string section);

    /// The action other than <paramref name="exceptId"/> that has this key in the set in use, or null.
    HotkeyAction? OwnerOf(string gesture, string exceptId);

    /// Every saved binding was written (a key given, cleared or imported).
    event Action? BindingsSaved;

    /// Actions whose ids changed (old id to new id); each owner of stored references rewrites its own.
    event Action<IReadOnlyDictionary<string, string>>? Moved;
}
