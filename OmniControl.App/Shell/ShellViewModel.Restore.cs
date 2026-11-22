using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Settings.Pages;
using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.App.Shell;

/// "Restore defaults" for every page: each page that offers one (<see cref="PageInfo.RestoreKey"/>, <see cref="PageSlot.Restorable"/>, registered
/// by its module) gets its action ("Restore Displays defaults"...) at start-up and its button when it is made, and App settings gets "Restore all",
/// which restores every page with one question and one copy. None of them makes a page.
public sealed partial class ShellViewModel
{
    private readonly DispatcherTimer _restoreCheck = new() { Interval = Timing.RestoreStateCheck };
    private readonly FileSnapshotStore _snapshots = new();
    private readonly Dictionary<PageSlot, PageRestore> _restorers = new();
    private PageRestore? _restoreAll;
    private readonly List<IRestorable> _restoreParts = new(); // what "Restore all" restores: the pages there are now, then the Actions and hotkeys window's parts
    private IReadOnlyList<IRestorable> _windowParts = Array.Empty<IRestorable>();

    private Task<bool> ConfirmRestore(RestoreQuestion q) => _dialogs.AskAsync(new DialogMessage(q.Title, q.Text, q.Button));

    private void WireRestore()
    {
        foreach (var slot in AllSlots) AddRestoreAction(slot);
        // The Actions and hotkeys window is not a page; its "Restore defaults" is wired the same way.
        var keys = _actionsWindow.Restorable;
        // It also puts back Windows' own screenshot keys, which that window's screenshot settings can turn off.
        var windowsKeys = _screenshot.KeysRestore(keys.Name);
        var keysRestorer = new PageRestore(new RestoreRunner(keys.Key, keys.Name, new IRestorable[] { keys, windowsKeys }, false, ConfirmRestore, _snapshots));
        _actionsWindow.Restorer = keysRestorer;
        // Filed under App settings (its "Restore all" lives there): the window is not a section of its own in the pickers.
        _hotkeys.Register(new HotkeyAction($"{keys.Key}.restore", $"Restore {keys.Name} defaults", AppSettingsPageViewModel.Name, "reset default restore shortcuts keys",
            devices => { _ = keysRestorer.RestoreAsync(); }));
        _windowParts = new IRestorable[] { keys, windowsKeys };
        RefreshRestoreParts();
        _restoreCheck.Tick += (_, _) => RefreshRestore();
    }

    /// "Restore all", made with App settings, its only user.
    private PageRestore RestoreAll => _restoreAll ??= new PageRestore(new RestoreRunner("all", "All", _restoreParts, true, ConfirmRestore, _snapshots), all: true);

    /// App settings' own part: its cards hold what it restores, so it is read from the page, which is made whenever Restore all is shown.
    private IRestorable AppSettingsRestore() =>
        new PageBacked(AppSettingsPageViewModel.Info, () => (_appSettings.Page as AppSettingsPageViewModel)?.Restorable);

    /// A page's button and line, made at the first use of either the page or its action and kept while the page is there.
    private PageRestore? RestorerOf(PageSlot slot)
    {
        if (slot.Restorable is not { } part) return null;
        if (!_restorers.TryGetValue(slot, out var restorer))
            _restorers[slot] = restorer = new PageRestore(new RestoreRunner(part.Key, slot.Title, new[] { part }, false, ConfirmRestore, _snapshots));
        return restorer;
    }

    /// A page's "Restore <page> defaults" action.
    private void AddRestoreAction(PageSlot slot)
    {
        if (slot.Info.RestoreKey is not { } key) return;
        _hotkeys.Register(new HotkeyAction($"{key}.restore", $"Restore {slot.Title} defaults", slot.Title, "reset default restore",
            devices => { if (RestorerOf(slot) is { } restorer) _ = restorer.RestoreAsync(); }));
    }

    /// A page went with its feature: its restore action and button go too.
    private void DropRestore(PageSlot slot)
    {
        if (slot.Info.RestoreKey is { } key) _hotkeys.Unregister($"{key}.restore");
        if (_restorers.Remove(slot, out var restorer)) restorer.Dispose();
    }

    /// "Restore all" follows the pages there are now.
    private void RefreshRestoreParts()
    {
        _restoreParts.Clear();
        _restoreParts.AddRange(AllSlots.Select(s => s.Restorable).OfType<IRestorable>());
        _restoreParts.AddRange(_windowParts);
    }

    /// A page just made shows its Restore defaults button.
    private void AttachRestore(PageSlot slot)
    {
        if (slot.IfMade is { } page && RestorerOf(slot) is { } restorer) page.AttachRestorer(restorer);
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
        foreach (var restorer in _restorers.Values) restorer.Dispose();
        _actionsWindow.Restorer?.Dispose();
        _restoreAll?.Dispose();
    }

    /// A part read from its page, standing in until the page is made (it is made when the part is first looked at).
    private sealed class PageBacked(PageInfo info, Func<IRestorable?> part) : IRestorable
    {
        private IRestorable? Part => part();

        public string Key => info.RestoreKey!;

        public string Name => info.Title;

        public string Scope => Part?.Scope ?? string.Empty;

        public string? Warning => Part?.Warning;

        public string? Unavailable => Part is { } p ? p.Unavailable : $"The {info.Title} page couldn't start";

        public bool IsAtDefaults => Part?.IsAtDefaults ?? true;

        public string CaptureSnapshot() => Part?.CaptureSnapshot() ?? string.Empty;

        public Task<bool> RestoreAsync() => Part?.RestoreAsync() ?? Task.FromResult(false);

        public Task<bool> UndoAsync(string snapshot) => Part?.UndoAsync(snapshot) ?? Task.FromResult(false);
    }
}
