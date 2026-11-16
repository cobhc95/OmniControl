using OmniControl.Core.Engine;

namespace OmniControl.App.AppLists;

/// <summary>
/// The one place that decides whether something limited to apps may act now: it looks at the program the user is working in
/// (<see cref="ProgramIdentity.Foreground"/>) and the scope's list (<see cref="AppListStore.Members"/>). Cheap enough for the mouse
/// hook: nothing is looked up for a scope that does not limit. A scope whose list is gone limits nothing.
/// </summary>
public sealed class AppScopeGate
{
    private readonly AppListStore _store;
    private readonly ProgramIdentity _identity = new();

    public AppScopeGate(AppListStore store) => _store = store;

    /// <summary>True when something with <paramref name="scope"/> works in the foreground app.</summary>
    public bool Allows(AppScopeSetting? scope)
    {
        if (scope is not { IsLimited: true }) return true;
        if (_store.Members(scope.ListId) is not { } members) return true;
        return scope.Allows(members.Contains(_identity.Foreground()));
    }
}
