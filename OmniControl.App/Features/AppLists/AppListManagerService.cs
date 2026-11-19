namespace OmniControl.App.Features.AppLists;

/// The one way the app lists manager is opened (every Apps chip's "Manage app lists..."): one non-modal window above the main window;
/// opened again, it is brought to the front.
public sealed class AppListManagerService
{
    private readonly AppListStore _store;
    private readonly Func<IReadOnlyList<InstalledApp>> _installed;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly SingleWindow<AppListManagerWindow> _window;

    /// <param name="installed">The installed apps the "+ Installed app" pop-down offers.</param>
    /// <param name="ask">Shows a short message with a confirm button (deleting a list that is in use).</param>
    /// <param name="report">Says on the footer when the window cannot be opened.</param>
    public AppListManagerService(AppListStore store, Func<IReadOnlyList<InstalledApp>> installed, Func<DialogMessage, Task<bool>> ask, Action<string> report) =>
        (_store, _installed, _ask, _window) = (store, installed, ask, new SingleWindow<AppListManagerWindow>(report));

    public void Open() => _window.Show(
        () => new AppListManagerWindow { DataContext = new AppListManagerViewModel(_store, _installed, _ask) },
        window => (window.DataContext as AppListManagerViewModel)?.Dispose(),
        "Couldn't open the app lists");

    /// Closes the window (at exit).
    public void Close() => _window.Close();
}
