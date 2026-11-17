using Avalonia.Threading;
using OmniControl.App.Services;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Pages.AppSettingsPage;

namespace OmniControl.App.Shell;

// The update check's share of the shell: the sidebar's "Update available" notice (shown only while a newer version exists) and its click,
// which opens App settings at the Updates group. The check itself is the UpdateService (Services/Updates); the page owns its cards.
public sealed partial class ShellViewModel
{
    /// <summary>The line at the sidebar's foot, empty when this is the newest version.</summary>
    public string UpdateNotice => _updates.Available is { } release ? $"Update available: {release.Version}" : string.Empty;

    public bool HasUpdate => UpdateNotice.Length > 0;

    /// <summary>Starts the daily check (never in a screenshot export) and follows what it finds.</summary>
    private void WatchUpdates()
    {
        _updates.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(UpdateNotice));
            OnPropertyChanged(nameof(HasUpdate));
        });
        if (!StartupMode.Inert) _updates.StartDaily();
    }

    [RelayCommand]
    private void OpenUpdates()
    {
        if (_allFooter.OfType<AppSettingsPageViewModel>().FirstOrDefault() is not { } settings) return;
        ShowPage(settings.Title);
        settings.ShowUpdates();
    }
}
