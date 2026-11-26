using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.Hotkeys;

/// What a feature that runs master actions by id needs (<see cref="HotkeyRegistry"/> is the one; every run passes its single run path).
public interface IActionRunner
{
    /// Runs an action by id. False when no such action is registered.
    bool Run(string id);

    /// Runs an action by id with the settings the input chose for it.
    bool Run(string id, RunSettings? settings);

    /// Starts an action that lasts while its input is held and returns what ends it; null when it does not last with these settings.
    Action? RunHeld(string id, RunSettings settings);

    /// Moves a level action up or down by <paramref name="percent"/>, or with <paramref name="snap"/> to the next round stop.
    bool RunLevel(string levelId, bool up, int percent, bool snap);

    /// The chain of rules that caused the run in progress; null = a person or Windows started it.
    RunChain? Chain { get; }

    /// Puts <paramref name="chain"/> in force for the rest of the calling async flow (a rule's worker, before it runs its steps).
    void RunWithin(RunChain chain);

    /// Raised on the UI thread after an action ran, with the chain of rules that caused it (null: none).
    event Action<HotkeyAction, RunChain?>? Ran;
}
