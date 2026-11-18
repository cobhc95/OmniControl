using OmniControl.App.Pages.AppSettingsPage;
using OmniControl.App.Pages.Displays;
using OmniControl.App.Services;

namespace OmniControl.App.Shell;

/// <summary>
/// What an import from another PC may reload live: each owner of a <see cref="DataStores"/> entry says once, here, how it takes the file again
/// (read it, replace what it holds, apply it, tell its pages). A store with no registration here cannot be applied live, and the import says so.
/// </summary>
public sealed partial class ShellViewModel
{
    private void RegisterReloads()
    {
        void Reload(DataStore store, Func<bool> reload) => _importHooks.OnReload(store, reload);
        Reload(DataStores.App, ReloadAppSettings);
        Reload(DataStores.Mixer, _mixer.ReloadFromDisk);
        Reload(DataStores.EffectPresets, _presets.ReloadFromDisk);
        Reload(DataStores.VolumePresets, _volumePresets.ReloadFromDisk);
        Reload(DataStores.Automation, _automation.ReloadFromDisk);
        Reload(DataStores.GestureRules, _rules.ReloadFromDisk);
        Reload(DataStores.Gestures, _gestures.ReloadFromDisk);
        Reload(DataStores.Hotkeys, _hotkeys.ReloadFromDisk);
        Reload(DataStores.HotkeySets, _actionsWindow.ReloadShortcutSets);
        Reload(DataStores.Mouse, _mouse.ReloadFromDisk);
        Reload(DataStores.Displays, () => _allPages.OfType<DisplaysPageViewModel>().FirstOrDefault()?.ReloadSettings() ?? false);
        Reload(DataStores.Osd, _osd.ReloadFromDisk);
        Reload(DataStores.AppLists, _appLists.ReloadFromDisk);
        Reload(DataStores.AppActions, _appActions.ReloadFromDisk);
        Reload(DataStores.PowerPlans, _powerPlanActions.ReloadFromDisk);
        Reload(DataStores.RecentActions, () => true); // read from the file every time it is used
        Reload(DataStores.Devices, _devices.ReloadFromDisk);
        Reload(DataStores.Screenshots, () => _windowsKeys?.ReloadFromDisk() ?? false);
        Reload(DataStores.Tray, () => TrayFlyout.ReloadFromDisk());
        Reload(DataStores.Icons, _icons.ReloadFromDisk);
        Reload(DataStores.Confirmations, _dialogs.Confirmations.Reload);
        _importHooks.OnFact(DataStores.Devices.Import!.Area, _devices.PasswordsNeededIn);
    }

    /// <summary>Settings that live in other owners than the page (the hotkeys switch, the Win+A key) follow what the page took.</summary>
    private bool ReloadAppSettings()
    {
        if (_allFooter.OfType<AppSettingsPageViewModel>().FirstOrDefault() is not { } page || !page.ReloadFromDisk()) return false;
        var settings = AppSettings.Current;
        _hotkeysSwitch.IsOn = settings.HotkeysOn;
        _winA.Set(settings.WinAForPanel);
        return true;
    }
}
