using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.States;

/// The master list of states: everything OmniControl knows the value of and can watch change (a smart device's power, the master volume,
/// a monitor's brightness, the mouse layer...). Each owner registers its own states once (<see cref="Register"/>: again with the same id to
/// rename one, <see cref="Unregister"/> when it goes) and calls <see cref="Notify"/> whenever a value may have changed; the registry reads
/// the value, compares it with the last one and raises <see cref="ValueChanged"/> only for a real change. It knows nothing about the
/// owners. Consumers read <see cref="States"/> and follow <see cref="ListChanged"/>; the automation rules' "When a state changes" trigger
/// is one. Everything runs on the UI thread; <see cref="Notify"/> may be called from any thread.
public sealed class StateRegistry : IStateRegistry, IStateReader
{
    private readonly Action<Action> _post;
    private readonly Func<RunChain?> _chainNow;
    private readonly List<StateEntry> _states = new();
    private readonly Dictionary<string, StateValue?> _last = new();

    /// <param name="post">Runs work on the UI thread (a test passes one that runs it at once).</param>
    /// <param name="chainNow">The chain of rules whose run is going on in the calling code, read when a change is notified (null: none).</param>
    public StateRegistry(Action<Action>? post = null, Func<RunChain?>? chainNow = null)
    {
        _post = post ?? (work => Dispatcher.UIThread.Post(work));
        _chainNow = chainNow ?? (() => null);
    }

    /// Every state now, in registration order.
    public IReadOnlyList<StateEntry> States => _states.ToList();

    /// A state was added, renamed or removed.
    public event Action? ListChanged;

    /// A state's value really changed.
    public event Action<StateChange>? ValueChanged;

    /// States whose ids changed (old id to new id: a merged smart device's power is now the device it was merged into).
    public event Action<IReadOnlyDictionary<string, string>>? Moved;

    public StateEntry? Find(string id) => _states.FirstOrDefault(s => s.Id == id);

    private readonly Dictionary<string, HashSet<string>> _watched = new();

    public event Action? WatchedChanged;

    public void Watch(string watcher, IEnumerable<string> ids)
    {
        var now = ids.ToHashSet();
        if (_watched.TryGetValue(watcher, out var before) && before.SetEquals(now)) return;
        _watched[watcher] = now;
        WatchedChanged?.Invoke();
    }

    public bool IsWatched(string id) => _watched.Values.Any(ids => ids.Contains(id));

    /// The value last read (null: not known, or no such state).
    public StateValue? Now(string id) => _last.GetValueOrDefault(id);

    /// The owner's current value of the state (null: not known, or no such state): what an On or Off action checks before it runs.
    public StateValue? Read(string id) => Find(id) is { } entry ? Safe(entry) : null;

    /// Has the owner read the state again (its <see cref="StateEntry.Recheck"/>), then calls <paramref name="done"/> on the UI thread; false when it cannot.
    public bool Refresh(string id, Action done)
    {
        if (Find(id)?.Recheck is not { } recheck || !recheck.CanCheck()) return false;
        _ = RefreshAsync(id, recheck, done);
        return true;
    }

    private async Task RefreshAsync(string id, StateRecheck recheck, Action done)
    {
        await Shared.Services.Guard.RunAsync($"Read state {id}", recheck.CheckAsync);
        _post(done);
    }

    /// Adds a state, or replaces the one with the same id (a rename): its value is read now and kept as the last one.
    public void Register(StateEntry entry)
    {
        Put(entry);
        ListChanged?.Invoke();
    }

    /// Adds or replaces several states with one <see cref="ListChanged"/>.
    public void Register(IEnumerable<StateEntry> entries)
    {
        foreach (var entry in entries) Put(entry);
        ListChanged?.Invoke();
    }

    private void Put(StateEntry entry)
    {
        var at = _states.FindIndex(s => s.Id == entry.Id);
        if (at >= 0) _states[at] = entry;
        else _states.Add(entry);
        _last[entry.Id] = Safe(entry);
    }

    public void Unregister(string id) => Unregister(new[] { id });

    public void Unregister(IEnumerable<string> ids)
    {
        var removed = ids.Count(id => _states.RemoveAll(s => s.Id == id) > 0 | _last.Remove(id));
        if (removed > 0) ListChanged?.Invoke();
    }

    /// Says that the states with these old ids now live under the new ones (whoever stored an old id follows).
    public void Move(IReadOnlyDictionary<string, string> moved)
    {
        if (moved.Count > 0) Moved?.Invoke(moved);
    }

    /// The state's value may have changed: it is read on the UI thread and <see cref="ValueChanged"/> is raised when it did.
    public void Notify(string id)
    {
        var chain = _chainNow(); // read here: the code that changed the value is the one calling
        _post(() => Compare(id, chain));
    }

    public void Notify(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return;
        var chain = _chainNow();
        _post(() =>
        {
            foreach (var id in ids) Compare(id, chain);
        });
    }

    /// What the owner is doing with the state, or how far it can be trusted, changed (its <see cref="StateEntry.Liveness"/>); the value itself may not have.
    public event Action<StateEntry>? LivenessChanged;

    public void NotifyLiveness(string id) => _post(() =>
    {
        if (Find(id) is { } entry) LivenessChanged?.Invoke(entry);
    });

    private void Compare(string id, RunChain? chain)
    {
        if (Find(id) is not { } entry) return;
        var before = _last.GetValueOrDefault(id);
        var after = Safe(entry);
        _last[id] = after;
        if (after is null ? before is null : after.SameValue(before)) return;
        ValueChanged?.Invoke(new StateChange(entry, before, after, chain));
    }

    // A state's getter reads its owner's cached value; an owner that is being torn down may throw, and that must not stop the others.
    private static StateValue? Safe(StateEntry entry)
    {
        try
        {
            return entry.Read();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
