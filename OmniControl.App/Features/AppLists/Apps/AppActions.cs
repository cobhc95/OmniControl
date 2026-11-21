using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.AppLists.Apps;

/// Keeps the "Open ..." app actions of the master list (<see cref="HotkeyRegistry"/>) in step with the apps found on this PC
/// (<see cref="AppCatalogue"/>), the programs the user added and the built-in apps the user hid (<see cref="AppActionsSettings"/>, the one
/// record of both). The user edits the list inside the Choose-the-action window: the Launch section's "Apps" group (each app's remove or hide
/// button and, for a program the user added, its name, in the settings pane), "+ Add program" and "Show hidden apps" (only while an app is hidden).
/// Installed apps are looked for at start and whenever a picker listing them opens, at most every <see cref="Timing.AppScanGap"/>.
public sealed class AppActions
{
    public const string Category = "Apps";
    private static readonly DataStore Store = DataStores.AppActions;

    private readonly AppLauncher _launcher;
    private readonly IActionRegistry _hotkeys;
    private readonly AppActionsSettings _settings = AppDataStore.Load<AppActionsSettings>(Store) ?? new();
    private readonly Dictionary<string, HotkeyAction> _registered = new();

    public AppActions(AppLauncher launcher, IActionRegistry hotkeys)
    {
        _launcher = launcher;
        _hotkeys = hotkeys;
        launcher.Scanned += Sync;
        hotkeys.RegisterPickerCommand(new HotkeyAction("app.add", "+ Add program", Category, "add program app choose browse", _ => { })
            { PickerSection = ActionSections.Launch, Command = AddAsync, PickerOpened = () => Refresh() });
        hotkeys.RegisterPickerCommand(new HotkeyAction("app.showhidden", "Show hidden apps", Category, "hidden apps show restore", _ => { })
            { PickerSection = ActionSections.Launch, Command = ShowHiddenAsync, IsListed = () => _settings.Hidden.Count > 0 });
        Refresh(force: true);
    }

    /// Takes app-actions.json from disk as it is now (an import wrote it): the added and hidden apps follow and their actions are registered again.
    public bool ReloadFromDisk()
    {
        SettingsCopy.Into(_settings, AppDataStore.Load<AppActionsSettings>(Store) ?? new());
        Refresh(force: true);
        Sync();
        return true;
    }

    /// Looks for the installed apps again, unless that was done a moment ago.
    public void Refresh(bool force = false) => _launcher.Scan(Entries(), force);

    /// The installed apps that are programs (a Store app is not), by name and program file, for lists that name apps (the app lists manager).
    public IReadOnlyList<InstalledApp> InstalledPrograms()
    {
        Refresh();
        return Entries().Where(a => !_settings.Hidden.Contains(a.Id) && _launcher.ProgramOf(a.Id) is not null)
            .Select(a => new InstalledApp(a.Title, _launcher.ProgramOf(a.Id)!)).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private IEnumerable<AppEntry> Entries() =>
        AppCatalogue.All.Concat(_settings.Added.Select(a => new AppEntry(a.Id, a.Title, "added program",
            new AppLocator { KnownPaths = new[] { new KnownPath(a.Path) } })));

    /// Registers the action of each shown app that is installed and takes out the rest.
    private void Sync()
    {
        var shown = Entries().Where(a => !_settings.Hidden.Contains(a.Id) && _launcher.IsInstalled(a.Id)).ToList();
        foreach (var gone in _registered.Keys.Where(id => shown.All(a => a.ActionId != id)).ToList())
        {
            _hotkeys.Unregister(gone);
            _registered.Remove(gone);
        }
        var added = shown.Where(a => !_registered.ContainsKey(a.ActionId)).Select(Action).ToList();
        _hotkeys.Register(added);
        foreach (var action in added) _registered[action.Id] = action;
    }

    private HotkeyAction Action(AppEntry app)
    {
        var userMade = _settings.Added.Any(a => a.Id == app.Id);
        return new(app.ActionId, $"Open {app.Title}", Category, $"open launch start app program {app.Keywords}", _ => _launcher.Open(app))
        {
            PickerSection = ActionSections.Launch,
            Accepts = ActionSettings.Launch,
            RunConfigured = settings => _launcher.Open(app, settings.Launch),
            Remove = () => Remove(app),
            RemoveText = userMade ? "Remove program" : "Hide app",
            Renaming = userMade ? new Renaming(app.Title, name => Rename(app, name)) : null,
        };
    }

    /// A program the user added gets the name they typed (an empty name changes nothing); its action is made again under it.
    private string? Rename(AppEntry app, string name)
    {
        name = name.Trim();
        var at = _settings.Added.FindIndex(a => a.Id == app.Id);
        if (name.Length == 0 || at < 0 || _settings.Added[at].Title == name) return null;
        _settings.Added[at] = _settings.Added[at] with { Title = name };
        AppDataStore.SaveSoon(Store, _settings);
        if (_registered.Remove(app.ActionId)) _hotkeys.Unregister(app.ActionId);
        Sync();
        return _registered.TryGetValue(app.ActionId, out var action) ? action.Title : null;
    }

    /// A program the user added is forgotten; a built-in app is hidden (it can be shown again).
    private void Remove(AppEntry app)
    {
        if (_settings.Added.RemoveAll(a => a.Id == app.Id) == 0) _settings.Hidden.Add(app.Id);
        AppDataStore.SaveSoon(Store, _settings);
        Sync();
    }

    private async Task<IReadOnlyList<HotkeyAction>> AddAsync(Visual from)
    {
        var path = await PathPicker.PickProgramAsync(from);
        if (path is null || _settings.Added.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase))) return Array.Empty<HotkeyAction>();
        var app = new UserApp(Guid.NewGuid().ToString("N")[..8], Path.GetFileNameWithoutExtension(path), path);
        _settings.Added.Add(app);
        AppDataStore.SaveSoon(Store, _settings);
        _launcher.Include(new AppEntry(app.Id, app.Title, string.Empty, new AppLocator { KnownPaths = new[] { new KnownPath(path) } }));
        Sync();
        return _registered.TryGetValue(AppCatalogue.ActionPrefix + app.Id, out var action) ? new[] { action } : Array.Empty<HotkeyAction>();
    }

    private Task<IReadOnlyList<HotkeyAction>> ShowHiddenAsync(Visual from)
    {
        var restored = _settings.Hidden.ToList();
        _settings.Hidden.Clear();
        AppDataStore.SaveSoon(Store, _settings);
        Sync();
        IReadOnlyList<HotkeyAction> made = restored.Select(id => AppCatalogue.ActionPrefix + id).Where(_registered.ContainsKey).Select(id => _registered[id]).ToList();
        return Task.FromResult(made);
    }
}
