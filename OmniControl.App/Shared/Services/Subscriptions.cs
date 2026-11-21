namespace OmniControl.App.Shared.Services;

/// THE way a view-model subscribes to a longer-lived service's events and gives them all back at once: <see cref="Follow{T}"/> adds the
/// handler and remembers how to take it away; <see cref="Dispose"/> takes every one away.
public sealed class Subscriptions : IDisposable
{
    private readonly List<Action> _leave = new();

    public void Follow<T>(Action<T> add, Action<T> remove, T handler) where T : Delegate
    {
        add(handler);
        _leave.Add(() => remove(handler));
    }

    public void Dispose()
    {
        foreach (var leave in _leave) leave();
        _leave.Clear();
    }
}
