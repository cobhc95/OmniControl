using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Shared.Presets.Manager;

/// The one way the preset manager is opened: every preset cogwheel, the FX windows' "Manage presets..." and App settings' Presets card
/// call <see cref="Open"/> with where they are. One non-modal window above the main window; opened again, it is brought to the front
/// and goes to the new place.
public sealed class PresetManagerService
{
    private readonly PresetGroups _groups;
    private readonly PresetShortcuts _shortcuts;
    private readonly PresetTriggersSlot _triggers;
    private IReadOnlyList<PresetSection> _sections = Array.Empty<PresetSection>();
    private readonly SingleWindow<PresetManagerWindow> _window;

    /// <param name="report">Says on the footer when the window cannot be opened.</param>
    /// <param name="triggers">The presets' trigger rules (state, action), empty while automation is off.</param>
    public PresetManagerService(PresetGroups groups, PresetShortcuts shortcuts, PresetTriggersSlot triggers, Action<string> report) =>
        (_groups, _shortcuts, _triggers, _window) = (groups, shortcuts, triggers, new SingleWindow<PresetManagerWindow>(report));

    /// Every group's words and every preset's name, for global search (the manager has its own search box, which finds the same).
    public IEnumerable<string> Words => _groups.All.SelectMany(g => g.Words.Concat(g.Provider.Entries.Select(e => e.Name))).Concat(PresetRowViewModel.Words);

    /// The app's sidebar pages, given by the shell once it has made them (the manager's left pane).
    public void UseSections(IReadOnlyList<PresetSection> sections) => _sections = sections;

    /// Opens the manager at <paramref name="groupId"/>'s card (its section chosen, the card outlined and scrolled to), or at <paramref name="section"/>, or at the first section with presets.
    public void Open(string? section, string? groupId)
    {
        if (_window.Current is { DataContext: PresetManagerViewModel open } shown)
        {
            open.Navigate(section, groupId);
            shown.Activate();
            return;
        }
        _window.Show(
            () => new PresetManagerWindow { DataContext = new PresetManagerViewModel(_groups, _shortcuts, _triggers, _sections) },
            window => (window.DataContext as PresetManagerViewModel)?.Dispose(),
            "Couldn't open the preset manager");
        if (_window.Current is { DataContext: PresetManagerViewModel vm }) vm.Navigate(section, groupId); // again once the window listens, so it scrolls to the card
    }

    /// Closes the window (at exit).
    public void Close() => _window.Close();
}
