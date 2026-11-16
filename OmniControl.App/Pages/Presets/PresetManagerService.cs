using OmniControl.App.Presets;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Presets;

/// <summary>
/// The one way the preset manager is opened: every preset cogwheel, the FX windows' "Manage presets..." and App settings' Presets card
/// call <see cref="Open"/> with where they are. One non-modal window above the main window; opened again, it is brought to the front
/// and goes to the new place.
/// </summary>
public sealed class PresetManagerService
{
    private readonly PresetGroups _groups;
    private readonly PresetShortcuts _shortcuts;
    private IReadOnlyList<PresetSection> _sections = Array.Empty<PresetSection>();
    private readonly SingleWindow<PresetManagerWindow> _window;

    /// <param name="report">Says on the footer when the window cannot be opened.</param>
    public PresetManagerService(PresetGroups groups, PresetShortcuts shortcuts, Action<string> report) =>
        (_groups, _shortcuts, _window) = (groups, shortcuts, new SingleWindow<PresetManagerWindow>(report));

    /// <summary>The app's sidebar pages, given by the shell once it has made them (the manager's left pane).</summary>
    public void UseSections(IReadOnlyList<PresetSection> sections) => _sections = sections;

    /// <summary>Opens the manager at <paramref name="groupId"/>'s card (its section chosen, the card outlined and scrolled to), or at <paramref name="section"/>, or at the first section with presets.</summary>
    public void Open(string? section, string? groupId)
    {
        if (_window.Current is { DataContext: PresetManagerViewModel open } shown)
        {
            open.Navigate(section, groupId);
            shown.Activate();
            return;
        }
        _window.Show(
            () => new PresetManagerWindow { DataContext = new PresetManagerViewModel(_groups, _shortcuts, _sections) },
            window => (window.DataContext as PresetManagerViewModel)?.Dispose(),
            "Couldn't open the preset manager");
        if (_window.Current is { DataContext: PresetManagerViewModel vm }) vm.Navigate(section, groupId); // again once the window listens, so it scrolls to the card
    }

    /// <summary>Closes the window (at exit).</summary>
    public void Close() => _window.Close();
}
