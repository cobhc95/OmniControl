namespace OmniControl.App.Features.States;

/// What a feature that only reads and follows the master list of states needs of it (<see cref="StateRegistry"/> is the one).
public interface IStateCatalogue
{
    /// Every state now, in registration order.
    IReadOnlyList<StateEntry> States { get; }

    /// A state was added, renamed or removed.
    event Action? ListChanged;

    /// A state's value really changed.
    event Action<StateChange>? ValueChanged;

    /// States whose ids changed (old id to new id).
    event Action<IReadOnlyDictionary<string, string>>? Moved;

    /// What the owner is doing with the state, or how far it can be trusted, changed; the value itself may not have.
    event Action<StateEntry>? LivenessChanged;

    StateEntry? Find(string id);

    /// The value last read (null: not known, or no such state).
    StateValue? Now(string id);
}

/// What a feature that owns states needs: it registers its own (and reads the list, so this includes <see cref="IStateCatalogue"/>) and says when a value may have changed.
public interface IStateRegistry : IStateCatalogue
{
    /// Adds a state, or replaces the one with the same id (a rename).
    void Register(StateEntry entry);

    /// Adds or replaces several states with one <see cref="IStateCatalogue.ListChanged"/>.
    void Register(IEnumerable<StateEntry> entries);

    void Unregister(string id);

    void Unregister(IEnumerable<string> ids);

    /// Says that the states with these old ids now live under the new ones (whoever stored an old id follows).
    void Move(IReadOnlyDictionary<string, string> moved);

    /// The state's value may have changed (any thread).
    void Notify(string id);

    /// The state's <see cref="StateEntry.Liveness"/> changed.
    void NotifyLiveness(string id);

    /// Says which states <paramref name="watcher"/> follows now (the automation rules' triggers), replacing what it said before, so an owner
    /// that must work to read a value (the Resources poller) reads it only while someone follows it. UI thread.
    void Watch(string watcher, IEnumerable<string> ids);

    /// True while some watcher follows the state.
    bool IsWatched(string id);

    /// What is followed changed (UI thread).
    event Action? WatchedChanged;
}
