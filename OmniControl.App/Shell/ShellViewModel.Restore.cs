using OmniControl.App.Dialogs;
using Avalonia.Threading;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Hotkeys;
using OmniControl.App.Pages;
using OmniControl.App.Pages.AppSettingsPage;
using OmniControl.App.Pages.Restore;
using OmniControl.App.Services;

namespace OmniControl.App.Shell;

/// <summary>
/// "Restore defaults" for every page: each page that offers one (<see cref="PageViewModel.Restorable"/>) gets its button and its action
/// ("Restore Displays defaults"...), and App settings gets "Restore all", which restores every page with one question and one copy.
/// </summary>
public sealed partial class ShellViewModel
{
    private readonly DispatcherTimer _restoreCheck = new() { Interval = Timing.RestoreStateCheck };
    private PageRestore? _restoreAll;

    private void WireRestore()
    {
        var store = new FileSnapshotStore();
        Task<bool> Confirm(RestoreQuestion q) => _dialogs.AskAsync(new DialogMessage(q.Title, q.Text, q.Button));
        var parts = new List<IRestorable>();
        foreach (var page in _allPages.Concat(_allFooter))
        {
            if (page.Restorable is not { } part) continue;
            var restorer = new PageRestore(new RestoreRunner(part.Key, page.Title, new[] { part }, false, Confirm, store));
            page.AttachRestorer(restorer);
            if (!part.HasOwnHotkey)
                _hotkeys.Register(new HotkeyAction($"{part.Key}.restore", $"Restore {page.Title} defaults", page.Title, "reset default restore",
                    devices => { _ = restorer.RestoreAsync(); }));
            parts.Add(part);
        }
        _restoreAll = new PageRestore(new RestoreRunner("all", "All", parts, true, Confirm, store), all: true);
        _allFooter.OfType<AppSettingsPageViewModel>().FirstOrDefault()?.ShowRestoreAll(_restoreAll);
        _restoreCheck.Tick += (_, _) => RefreshRestore();
        _restoreCheck.Start();
    }

    /// <summary>The open page's button looks again at whether the page is at its defaults (only while the window can be seen).</summary>
    private void RefreshRestore()
    {
        if (!_windowShown) return;
        CurrentPage?.Restorer?.Refresh();
        if (CurrentPage is AppSettingsPageViewModel) _restoreAll?.Refresh();
    }

    private void DisposeRestore()
    {
        _restoreCheck.Stop();
        foreach (var page in _allPages.Concat(_allFooter)) page.Restorer?.Dispose();
        _restoreAll?.Dispose();
    }
}
