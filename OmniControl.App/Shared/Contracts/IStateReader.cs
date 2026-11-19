using OmniControl.App.Features.States;

namespace OmniControl.App.Shared.Contracts;

/// What <see cref="Features.Hotkeys.HotkeyRegistry"/> asks for the state an action is linked to (<see cref="Features.Hotkeys.HotkeyAction.StateId"/>); <see cref="Features.States.StateRegistry"/> answers.
public interface IStateReader
{
    /// The state's value now, from its owner's cache (null: not known, or no such state).
    StateValue? Read(string id);

    /// Asks the owner to read the real value again, then calls <paramref name="done"/> on the UI thread. False (and <paramref name="done"/> is never called) when the owner cannot.
    bool Refresh(string id, Action done);
}
