using OmniControl.App.Pages;
using OmniControl.App.Pages.Gestures;
using OmniControl.App.Picker;
using OmniControl.App.Services;
using OmniControl.App.Showcase;
using OmniControl.App.Tray;

namespace OmniControl.App.Shell;

/// <summary>Developer builds only: the shell's answer to the screenshot export (<see cref="IShowcaseSource"/>), from the lists it already keeps.</summary>
public sealed partial class ShellViewModel : IShowcaseSource
{
    IReadOnlyList<PageViewModel> IShowcaseSource.ShowcasePages => _allPages.Concat(_allFooter).ToList();

    PageViewModel? IShowcaseSource.ShowcaseCurrentPage => CurrentPage;

    void IShowcaseSource.ShowcaseShow(PageViewModel page) => OpenPage(page.Title);

    // The registration hook for windows: a window that opens from a service is listed here once, where the shell holds that service.
    IReadOnlyList<ShowcaseWindow> IShowcaseSource.ShowcaseWindows => new[]
    {
        new ShowcaseWindow(ActionsWindowService.Title, () => _actionsWindow.Open()),
        new ShowcaseWindow("About", _about.Open),
        new ShowcaseWindow("Presets", () => _presetManager.Open(null, null)),
        new ShowcaseWindow("App lists", _appListManager.Open),
        new ShowcaseWindow("Trigger editor", OpenRuleEditor, Isolated: true), // makes a new rule
        new ShowcaseWindow("Effects", () => _fxWindows.Open(null), Isolated: true),
        new ShowcaseWindow("Tray quick panel", OpenTrayPanel, Finish: window => { ((TrayFlyoutWindow)window).CloseForGood(); TrayFlyout.Hidden(); }),
        ShowcaseAreaEditor(),
    };

    private static ShowcaseWindow ShowcaseAreaEditor()
    {
        var capture = new ShowcaseCapture(); // the real area editor over a made-up picture
        return new ShowcaseWindow("Screenshot area", capture.Open, Finish: capture.Finish);
    }

    private void OpenTrayPanel()
    {
        TrayFlyout.Showing();
        new TrayFlyoutWindow { DataContext = TrayFlyout, ShowActivated = false }.Show(); // not given the focus: losing it would hide it
    }

    private void OpenRuleEditor()
    {
        if (OwnedWindow.MainWindow is { } owner && _allPages.OfType<GesturesPageViewModel>().FirstOrDefault() is { } page)
            _ = RuleEditorWindow.ShowAsync(owner, page.AddRule());
    }

    IEnumerable<string> IShowcaseSource.ShowcasePrivateNames() =>
        _mixer.Devices.Concat(_mixer.RecordingDevices).Select(d => d.Name)
            .Concat(_engine.Monitors.Select(m => m.Info.Name));

    void IShowcaseSource.ShowcaseSay(string message) => Report(message);
}
