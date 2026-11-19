namespace OmniControl.App.Features.AppLists;

/// Makes the scope models a page needs (a group's, a card's) with the shared store and the opener of the manager window, so a page
/// never knows how either is reached.
public sealed class AppScopes
{
    private readonly AppListStore _store;
    private readonly Action _manage;

    /// <param name="manage">Opens the app lists manager window.</param>
    public AppScopes(AppListStore store, Action manage) => (_store, _manage) = (store, manage);

    /// A group's scope: <paramref name="read"/> gives the stored scope (null: all apps), <paramref name="write"/> stores and applies it.
    public AppScopeModel Group(string where, Func<AppScopeSetting?> read, Action<AppScopeSetting?> write) => new(_store, where, read, write, _manage);

    /// A list of apps something never works in, stored as "all except" a list (null: none): the chip shows "None" or the list's name and
    /// its menu offers None and the lists.
    public AppScopeModel Exclusions(string where, Func<AppScopeSetting?> read, Action<AppScopeSetting?> write) => new(_store, where, read, write, _manage, excludes: true);

    /// A card's own scope inside <paramref name="group"/>: the stored value is null while the card follows its group.
    public AppScopeModel Card(string where, AppScopeModel group, Func<AppScopeSetting?> read, Action<AppScopeSetting?> write) =>
        new(_store, where, read, write, _manage, group);
}
