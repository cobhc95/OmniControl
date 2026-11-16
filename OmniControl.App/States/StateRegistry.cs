using Avalonia.Threading;
using OmniControl.App.Hotkeys;

namespace OmniControl.App.States;

/// <summary>
/// The master list of states: everything OmniControl knows the value of and can watch change (a smart device's power, the master volume,
/// a monitor's brightness, the mouse layer...). Each owner registers its own states once (<see cref="Register"/>: again with the same id to
/// rename one, <see cref="Unregister"/> when it goes) and calls <see cref="Notify"/> whenever a value may have changed; the registry reads
/// the value, compares it with the last one and raises <see cref="ValueChanged"/> only for a real change. It knows nothing about the
/// owners. Consumers read <see cref="States"/> and follow <see cref="ListChanged"/>; the automation rules' "When a state changes" trigger
/// is one. Everything runs on the UI thread; <see cref="Notify"/> may be called from any thread.
/// </summary>
public sealed class StateRegistry
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

    /// <summary>Every state now, in registration order.</summary>
    public IReadOnlyList<StateEntry> States => _states.ToList();

    /// <summary>A state was added, renamed or removed.</summary>
    public event Action? ListChanged;

    /// <summary>A state's value really changed.</summary>
    public event Action<StateChange>? ValueChanged;

    /// <summary>States whose ids changed (old id to new id: a merged smart device's power is now the device it was merged into).</summary>
    public event Action<IReadOnlyDictionary<string, string>>? Moved;

    public StateEntry? Find(string id) => _states.FirstOrDefault(s => s.Id == id);

    /// <summary>The value last read (null: not known, or no such state).</summary>
    public StateValue? Now(string id) => _last.GetValueOrDefault(id);

    /// <summary>Adds a state, or replaces the one with the same id (a rename): its value is read now and kept as the last one.</summary>
    public void Register(StateEntry entry)
    {
        Put(entry);
        ListChanged?.Invoke();
    }

    /// <summary>Adds or replaces several states with one <see cref="ListChanged"/>.</summary>
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

    /// <summary>Says that the states with these old ids now live under the new ones (whoever stored an old id follows).</summary>
    public void Move(IReadOnlyDictionary<string, string> moved)
    {
        if (moved.Count > 0) Moved?.Invoke(moved);
    }

    /// <summary>The state's value may have changed: it is read on the UI thread and <see cref="ValueChanged"/> is raised when it did.</summary>
    public void Notify(string id)
    {
        var chain = _chainNow(); // read here: the code that changed the value is the one calling
        _post(() => Compare(id, chain));
    }

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
