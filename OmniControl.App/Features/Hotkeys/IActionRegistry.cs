namespace OmniControl.App.Features.Hotkeys;

/// What a feature that adds its own actions to the master list needs of it (registering reads the list as well, so this includes
/// <see cref="IActionCatalogue"/>). <see cref="HotkeyRegistry"/> is the one; the tests give a fake, so no shortcut is registered on the PC.
public interface IActionRegistry : IActionCatalogue
{
    void Register(HotkeyAction action);

    /// Registers several actions at once (one rebinding of the hotkeys).
    void Register(IEnumerable<HotkeyAction> actions);

    /// Adds a button to a picker list (see <see cref="IActionCatalogue.PickerActions"/>); it is not an action and never gets a key.
    void RegisterPickerCommand(HotkeyAction command);

    /// Takes the action (or a picker button) out of the master list; its saved shortcut stays in hotkeys.json (a deleted preset that is put back gets it again).
    void Unregister(string id);

    /// Why <paramref name="gesture"/> cannot be the shortcut of the action <paramref name="id"/> (unreadable, no Ctrl, Alt or Win, or another action has it); null when it can.
    string? ProblemWith(string gesture, string id);

    /// Says that the actions with these old ids are now the ones with the new ids (a merged smart device's actions); raises <see cref="IActionCatalogue.Moved"/>.
    void Move(IReadOnlyDictionary<string, string> moved);
}
