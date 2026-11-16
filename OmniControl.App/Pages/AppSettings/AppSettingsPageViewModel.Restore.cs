using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Pages.Restore;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.AppSettingsPage;

/// <summary>
/// "Restore defaults" on App settings (the theme, accent, start-up, close and plug-in folder choices, <see cref="AppRestoreState"/>) and the
/// "Restore all defaults" card, which holds the shell's combined restore of every page. Each value goes through its card, so the theme,
/// accent and Windows' start-up entry follow at once.
/// </summary>
public sealed partial class AppSettingsPageViewModel
{
    private const string AllLabel = "Restore all defaults";
    private IRestorable? _restorable;

    /// <summary>The line and Undo link of the combined restore, under the cards; null until the shell has made it.</summary>
    [ObservableProperty] private PageRestore? _restoreAll;

    public override IRestorable? Restorable => _restorable ??= new SettingsRestore<AppRestoreState>("app", Title,
        AppRestoreState.Scope,
        () => AppRestoreState.Of(_settings, StartupRegistration.State == StartupState.On), AppRestoreState.Defaults, ApplyState,
        unavailable: () => _scanning ? "A plug-in scan is running" : null);

    /// <summary>The shell gives the combined restore: its card ("Restore all defaults" with a "Restore all" button) joins the list before About.</summary>
    public void ShowRestoreAll(PageRestore all)
    {
        RestoreAll = all;
        var card = new ButtonItem(AllLabel, all.ButtonText, "Puts every page back to its defaults after one question; you can undo it", () => { _ = all.RestoreAsync(); });
        Items.Insert(Items.IndexOf(_about), card);
        _restoreAllCard = card;
    }

    private ButtonItem? _restoreAllCard;

    private IEnumerable<SearchEntry> RestoreSearchEntries() => _restoreAllCard is null
        ? Enumerable.Empty<SearchEntry>()
        : new[] { Entry(_restoreAllCard, AllLabel, "reset default restore every page all settings") };

    private Task<bool> ApplyState(AppRestoreState target)
    {
        Theme.Index = (int)target.Theme;
        Accent.Index = Math.Max(0, AccentService.Names.ToList().IndexOf(target.Accent));
        _startMinimised.IsOn = target.StartMinimised;
        _whenClosing.Selected = CloseBehaviors.LabelOf(target.CloseBehavior);
        SetFolders(target.PluginFolders);

        ReadStartup(); // the card shows what Windows has now, so flipping it really changes Windows
        _startWithWindows.IsOn = target.StartWithWindows;
        return Task.FromResult((StartupRegistration.State == StartupState.On) == target.StartWithWindows);
    }

    /// <summary>Replaces the user's plug-in folders (and their rows) with <paramref name="folders"/>.</summary>
    private void SetFolders(IReadOnlyList<string> folders)
    {
        foreach (var row in Items.OfType<ButtonItem>().Where(IsFolderRow).ToList()) Items.Remove(row);
        _settings.PluginFolders = folders.ToList();
        _settings.Save();
        foreach (var folder in _settings.PluginFolders) Items.Insert(Items.IndexOf(_scan), FolderRow(folder));
    }
}
