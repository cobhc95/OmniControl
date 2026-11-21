using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Settings.Pages;
using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.App.Shell;

/// "Restore defaults" for every page: each page that offers one (<see cref="PageInfo.RestoreKey"/>, <see cref="PageViewModel.Restorable"/>)
/// gets its button when it is made and its action ("Restore Displays defaults"...) at start-up, and App settings gets "Restore all", which
/// restores every page with one question and one copy (a page not made yet is made when the combined restore first looks at it).
public sealed partial class ShellViewModel
{
    private readonly DispatcherTimer _restoreCheck = new() { Interval = Timing.RestoreStateCheck };
    private readonly FileSnapshotStore _snapshots = new();
    private PageRestore? _restoreAll;

    private Task<bool> ConfirmRestore(RestoreQuestion q) => _dialogs.AskAsync(new DialogMessage(q.Title, q.Text, q.Button));

    private void WireRestore()
    {
        var parts = new List<IRestorable>();
        foreach (var slot in AllSlots)
        {
            if (slot.Info.RestoreKey is not { } key) continue;
            if (slot.IfMade?.Restorable?.HasOwnHotkey != true)
                _hotkeys.Register(new HotkeyAction($"{key}.restore", $"Restore {slot.Title} defaults", slot.Title, "reset default restore",
                    devices => { if (slot.Page?.Restorer is { } restorer) _ = restorer.RestoreAsync(); }));
            parts.Add(new SlotRestorable(slot, key));
        }
        // The Actions and hotkeys window is not a page; its "Restore defaults" is wired the same way.
        var keys = _actionsWindow.Restorable;
        // It also puts back Windows' own screenshot keys, which that window's screenshot settings can turn off.
        var windowsKeys = _screenshot.KeysRestore(keys.Name);
        var keysRestorer = new PageRestore(new RestoreRunner(keys.Key, keys.Name, new IRestorable[] { keys, windowsKeys }, false, ConfirmRestore, _snapshots));
        _actionsWindow.Restorer = keysRestorer;
        // Filed under App settings (its "Restore all" lives there): the window is not a section of its own in the pickers.
        _hotkeys.Register(new HotkeyAction($"{keys.Key}.restore", $"Restore {keys.Name} defaults", AppSettingsPageViewModel.Name, "reset default restore shortcuts keys",
            devices => { _ = keysRestorer.RestoreAsync(); }));
        parts.Add(keys);
        parts.Add(windowsKeys);
        _restoreAll = new PageRestore(new RestoreRunner("all", "All", parts, true, ConfirmRestore, _snapshots), all: true);
        _restoreCheck.Tick += (_, _) => RefreshRestore();
    }

    /// A page just made gets its own Restore defaults button.
    private void AttachRestore(PageViewModel page)
    {
        if (page.Restorable is { } part)
            page.AttachRestorer(new PageRestore(new RestoreRunner(part.Key, page.Title, new[] { part }, false, ConfirmRestore, _snapshots)));
    }

    /// The restore check runs only while the window can be seen (<see cref="SetWindowShown"/>); showing it looks at once.
    private void FollowRestoreCheck(bool shown)
    {
        _restoreCheck.IsEnabled = shown;
        if (shown) RefreshRestore();
    }

    /// The open page's button looks again at whether the page is at its defaults.
    private void RefreshRestore()
    {
        CurrentPage?.Restorer?.Refresh();
        if (CurrentPage is AppSettingsPageViewModel) _restoreAll?.Refresh();
    }

    private void DisposeRestore()
    {
        _restoreCheck.Stop();
        foreach (var slot in AllSlots) slot.IfMade?.Restorer?.Dispose();
        _actionsWindow.Restorer?.Dispose();
        _restoreAll?.Dispose();
    }

    /// A page's part of "Restore all", standing in for the page's own until it is made: the page is made when the combined restore first looks at it.
    private sealed class SlotRestorable(PageSlot slot, string key) : IRestorable
    {
        private IRestorable? Part => slot.Page?.Restorable;

        public string Key => key;

        public string Name => slot.Title;

        public string Scope => Part?.Scope ?? string.Empty;

        public string? Warning => Part?.Warning;

        public string? Unavailable => Part is { } part ? part.Unavailable : $"The {slot.Title} page couldn't start";

        public bool IsAtDefaults => Part?.IsAtDefaults ?? true;

        public bool HasOwnHotkey => Part?.HasOwnHotkey ?? false;

        public string CaptureSnapshot() => Part?.CaptureSnapshot() ?? string.Empty;

        public Task<bool> RestoreAsync() => Part?.RestoreAsync() ?? Task.FromResult(false);

        public Task<bool> UndoAsync(string snapshot) => Part?.UndoAsync(snapshot) ?? Task.FromResult(false);
    }
}
