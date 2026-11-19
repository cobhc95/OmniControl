using OmniControl.App.Features.Settings.Pages;

namespace OmniControl.App.Shell;

/// What an import from another PC may reload live of the stores the shell itself owns (each feature module registers its own): how each takes
/// the file again (read it, replace what it holds, apply it, tell its pages). A store with no registration cannot be applied live, and the import says so.
public sealed partial class ShellViewModel
{
    private void RegisterReloads()
    {
        void Reload(DataStore store, Func<bool> reload) => _importHooks.OnReload(store, reload);
        Reload(DataStores.App, ReloadAppSettings);
        Reload(DataStores.Hotkeys, _hotkeys.ReloadFromDisk);
        Reload(DataStores.HotkeySets, _actionsWindow.ReloadShortcutSets);
        Reload(DataStores.RecentActions, () => true); // read from the file every time it is used
        Reload(DataStores.Tray, () => TrayFlyout.ReloadFromDisk());
        Reload(DataStores.Icons, _icons.ReloadFromDisk);
        Reload(DataStores.Confirmations, _dialogs.Confirmations.Reload);
    }

    /// Settings that live in other owners than the page (the hotkeys switch, the Win+A key) follow what the page took.
    private bool ReloadAppSettings()
    {
        if (_allFooter.OfType<AppSettingsPageViewModel>().FirstOrDefault() is not { } page || !page.ReloadFromDisk()) return false;
        _hotkeysSwitch.IsOn = _settings.HotkeysOn;
        _winA.Set(_settings.WinAForPanel);
        return true;
    }
}
