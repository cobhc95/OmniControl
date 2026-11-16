namespace OmniControl.App.AppLists;

/// <summary>
/// Makes the scope models a page needs (a group's, a card's) with the shared store and the opener of the manager window, so a page
/// never knows how either is reached.
/// </summary>
public sealed class AppScopes
{
    private readonly AppListStore _store;
    private readonly Action _manage;

    /// <param name="manage">Opens the app lists manager window.</param>
    public AppScopes(AppListStore store, Action manage) => (_store, _manage) = (store, manage);

    /// <summary>A group's scope: <paramref name="read"/> gives the stored scope (null: all apps), <paramref name="write"/> stores and applies it.</summary>
    public AppScopeModel Group(string where, Func<AppScopeSetting?> read, Action<AppScopeSetting?> write) => new(_store, where, read, write, _manage);

    /// <summary>A card's own scope inside <paramref name="group"/>: the stored value is null while the card follows its group.</summary>
    public AppScopeModel Card(string where, AppScopeModel group, Func<AppScopeSetting?> read, Action<AppScopeSetting?> write) =>
        new(_store, where, read, write, _manage, group);
}
