using OmniControl.App.Features.Settings.Pages;

namespace OmniControl.App.Shell;

// The update check's share of the shell: the sidebar's "Update available" notice (shown only while a newer version exists) and its click,
// which opens App settings at the Updates group. The check itself is the UpdateService (Services/Updates); the page owns its cards.
public sealed partial class ShellViewModel
{
    /// The line at the sidebar's foot, empty when this is the newest version.
    public string UpdateNotice => _updates.Available is { } release ? $"Update available: {release.Version}" : string.Empty;

    public bool HasUpdate => UpdateNotice.Length > 0;

    /// Starts the daily check and follows what it finds.
    private void WatchUpdates()
    {
        _updates.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(UpdateNotice));
            OnPropertyChanged(nameof(HasUpdate));
        });
        _updates.StartDaily();
    }

    [RelayCommand]
    private void OpenUpdates()
    {
        ShowPage(AppSettingsPageViewModel.Name);
        (_appSettings.Page as AppSettingsPageViewModel)?.ShowUpdates();
    }
}
