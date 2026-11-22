using OmniControl.App.Shared.Services.Updates;

namespace OmniControl.App.Features.Settings.Pages;

/// The update check on App settings ("Updates and about"): the "Check once a day" switch, this version with when it was checked, one line
/// for the newest release (or the calm result), and "Check now" on the group's header. It only checks; the link opens the release page.
public sealed partial class AppSettingsPageViewModel
{
    private UpdateService _updates = null!;
    private ToggleItem _updateDaily = null!;
    private FactItem _version = null!;
    private FactItem _latest = null!;

    /// The "Check now" pill on the group's header.
    public IRelayCommand CheckNowCommand { get; private set; } = null!;

    private void MakeUpdateCards(UpdateService updates, Action<string> report)
    {
        _updates = updates;
        _updateDaily = new ToggleItem(UpdateDailyCard.Label, updates.Daily, on => updates.Daily = on)
        {
            Half = true,
            Tip = "Looks at GitHub's release list for a newer version. It only checks; nothing is downloaded or installed.",
        };
        _version = new FactItem(VersionCard.Label) { Half = true, Value = AppInfo.Version };
        _latest = new FactItem(LatestCard.Label, () => OpenLatest(report));
        CheckNowCommand = new RelayCommand(CheckNow, () => !_updates.Checking);
        Follow(h => updates.Changed += h, h => updates.Changed -= h, () => Dispatcher.UIThread.Post(RefreshUpdates)); // the check runs on a worker thread
        RefreshUpdates();
    }

    /// The sidebar's "Update available" notice was clicked: the Updates and about group opens.
    public void ShowUpdates() => UpdatesAndAbout.IsExpanded = true;

    private void CheckNow() => _ = _updates.CheckAsync();

    private void OpenLatest(Action<string> report)
    {
        if (_updates.Available is { } release && WebPage.Open(UpdateFeed.Trusted(release.Url)) is { } error) report(error);
    }

    /// Shows what the service knows: the time of the last check, and the one line about the newest release.
    private void RefreshUpdates()
    {
        _updateDaily.Load(_updates.Daily);
        _version.Note = _updates.Checking ? "checking…" : _updates.LastCheck is { } at ? $"checked {CheckedAt(at)}" : "not checked yet";
        if (_updates.Available is { } release)
        {
            _latest.Tags = new[] { new TagItem($"{release.Version} available", TagTone.Selected) };
            _latest.Note = (release.Prerelease ? "pre-release" : "release") + (release.Published is { } date ? $" · {date:d MMM}" : string.Empty);
            _latest.ActionText = "Open on GitHub";
        }
        else
        {
            _latest.Tags = Array.Empty<TagItem>();
            _latest.ActionText = string.Empty;
            _latest.Note = _updates.Checking ? "Checking…"
                : _updates.LastFailed ? "Couldn't check. Try again later."
                : _updates.LastCheck is null ? "Not checked yet."
                : "You have the latest version.";
        }
        CheckNowCommand.NotifyCanExecuteChanged();
    }

    private static string CheckedAt(DateTime at) => at.Date == DateTime.Today ? at.ToString("HH:mm") : at.ToString("d MMM HH:mm");
}
