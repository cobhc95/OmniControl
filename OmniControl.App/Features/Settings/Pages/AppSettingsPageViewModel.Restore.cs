using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.App.Features.Settings.Pages;

/// "Restore defaults" on App settings (the theme, accent, background colour, tray icon look, start-up, close, daily update check, how groups start and plug-in folder choices, <see cref="AppRestoreState"/>) and the
/// "Restore all defaults" card, which holds the shell's combined restore of every page. Each value goes through its card, so the theme,
/// accent and Windows' start-up entry follow at once.
public sealed partial class AppSettingsPageViewModel
{
    private IRestorable? _restorable;

    /// The line and Undo link of the combined restore, under the cards; null until the shell has made it.
    [ObservableProperty] private PageRestore? _restoreAll;

    public override IRestorable? Restorable => _restorable ??= new SettingsRestore<AppRestoreState>("app", Title,
        () => AppRestoreState.Scope(DefaultTrayWords()),
        () => AppRestoreState.Of(_settings, StartupRegistration.State == StartupState.On, TrayLook), () => AppRestoreState.Defaults(DefaultTrayLook), ApplyState,
        unavailable: () => _scanning ? "A plug-in scan is running" : null);

    /// The shell gives the combined restore: the "Restore all defaults" card of Your data takes its button words.
    public void ShowRestoreAll(PageRestore all)
    {
        RestoreAll = all;
        _restoreAllCard.ButtonText = all.ButtonText;
    }

    private Task<bool> ApplyState(AppRestoreState target)
    {
        Theme.Index = (int)target.Theme;
        Accent.Index = Math.Max(0, AccentService.Names.ToList().IndexOf(target.Accent));
        Background.Index = SurfaceService.IndexOf(target.Background);
        SetTrayLook((target.TrayIcon, target.TrayIconColour));
        _startMinimised.IsOn = target.StartMinimised;
        _whenClosing.Selected = CloseBehaviors.LabelOf(target.CloseBehavior);
        GroupsOpen.Set(target.GroupsOpen);
        _hardwareCheck.Selected = HardwareChecks.LabelOf(target.HardwareCheck);
        _updateDaily.IsOn = target.UpdateCheckDaily;
        SetFolders(target.PluginFolders);

        ReadStartup(); // the card shows what Windows has now, so flipping it really changes Windows
        _startWithWindows.IsOn = target.StartWithWindows;
        return Task.FromResult((StartupRegistration.State == StartupState.On) == target.StartWithWindows);
    }

    /// Replaces the user's plug-in folders (and their rows) with <paramref name="folders"/>.
    private void SetFolders(IReadOnlyList<string> folders)
    {
        foreach (var row in PluginItems.OfType<ButtonItem>().Where(IsFolderRow).ToList()) PluginItems.Remove(row);
        _settings.PluginFolders = folders.ToList();
        _settings.Save();
        foreach (var folder in _settings.PluginFolders) PluginItems.Insert(PluginItems.IndexOf(_scan), FolderRow(folder));
    }
}
