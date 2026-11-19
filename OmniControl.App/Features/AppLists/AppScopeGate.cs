namespace OmniControl.App.Features.AppLists;

/// The one place that decides whether something limited to apps may act now: it looks at the program the user is working in
/// (<see cref="ForegroundProgram"/>, looked up by a worker, never by the caller) and the scope's list (<see cref="AppListStore.Members"/>). Cheap enough for the mouse
/// hook: it only reads what the worker has found, and nothing is read for a scope that does not limit. A scope whose list is gone limits nothing.
public sealed class AppScopeGate
{
    private readonly AppListStore _store;
    private readonly ForegroundProgram _foreground = new();

    public AppScopeGate(AppListStore store) => _store = store;

    /// True when something with <paramref name="scope"/> works in the foreground app.
    public bool Allows(AppScopeSetting? scope) => scope is not { IsLimited: true } || Allows(scope, _foreground.Name);

    /// True when something with <paramref name="scope"/> covers <paramref name="program"/> (its identity): a list of programs filtered by an Apps chip.
    public bool Allows(AppScopeSetting? scope, string program)
    {
        if (scope is not { IsLimited: true }) return true;
        if (_store.Members(scope.ListId) is not { } members) return true;
        return scope.Allows(members.Contains(program));
    }
}
