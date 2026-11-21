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
        Reload(DataStores.FavouriteActions, _actionsWindow.ReloadFavourites);
        Reload(DataStores.Tray, () => TrayFlyout.ReloadFromDisk());
        Reload(DataStores.Icons, _icons.ReloadFromDisk);
        Reload(DataStores.Confirmations, _dialogs.Confirmations.Reload);
    }

    /// Takes settings.json from disk as it is now (an import wrote it): the theme, background colour and accent are applied, the hardware
    /// check runs as chosen, the owners that keep a setting of their own (the hotkeys switch, the Win+A key) follow, and App settings' cards
    /// show the values when that page exists. The features' setting slots read the same settings, so they follow.
    private bool ReloadAppSettings()
    {
        SettingsCopy.Into(_settings, AppSettings.Load());
        ThemeChoices.Apply(_settings.Theme);
        SurfaceService.Set(_settings.Background);
        AccentService.Load(_settings.Accent, _settings.AccentCustom, _settings.AccentIntensity);
        _hardware.ApplyCheck();
        (_appSettings.IfMade as AppSettingsPageViewModel)?.ShowReloaded();
        _hotkeysSwitch.IsOn = _settings.HotkeysOn;
        _winA.Set(_settings.WinAForPanel);
        return true;
    }
}
