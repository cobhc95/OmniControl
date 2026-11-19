using OmniControl.App.Features.AppLists.Apps;

namespace OmniControl.App.Features.AppLists;

/// The app actions (open a program) and the master list of app lists, with the scopes and the gate other features limit themselves by.
public sealed class AppsModule : IFeatureModule
{
    private readonly AppListStore _lists = new(AppListSettings.Load());
    private readonly AppListManagerService _manager;
    private AppActions? _actions;

    /// Made before the other modules, which are given <see cref="Scopes"/> and <see cref="Gate"/>.
    public AppsModule(Func<DialogMessage, Task<bool>> ask, Action<string> report)
    {
        _manager = new AppListManagerService(_lists, () => _actions!.InstalledPrograms(), ask, report);
        Scopes = new AppScopes(_lists, _manager.Open);
        Gate = new AppScopeGate(_lists);
    }

    public AppScopes Scopes { get; }

    public AppScopeGate Gate { get; }

    public void Register(FeatureContext context)
    {
        _actions = new AppActions(new AppLauncher(context.Report), context.Actions);
        context.Imports.OnReload(DataStores.AppLists, _lists.ReloadFromDisk);
        context.Imports.OnReload(DataStores.AppActions, _actions.ReloadFromDisk);
        context.Shutdown.Add(ShutdownStage.AppListManager, "app list manager", _manager.Close);
    }

    /// The main window was shown: the installed programs are looked at again.
    public void Refresh() => _actions?.Refresh();

    /// Every app in the user's app lists, as the programs a picker offers.
    public IReadOnlyList<RunningProgram> ListedPrograms() =>
        _lists.Lists.SelectMany(l => l.Apps).Select(a => new RunningProgram(a.Program, a.Name)).ToList();
}
