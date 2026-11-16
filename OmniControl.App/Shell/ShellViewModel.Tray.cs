using OmniControl.App.Hotkeys;
using OmniControl.App.Pages.AppSettingsPage;
using OmniControl.App.Picker;
using OmniControl.App.Services;
using OmniControl.App.Tray;

namespace OmniControl.App.Shell;

// The tray's share of the shell: the flyout's model over the master lists the shell owns, the master actions the tray menu opens windows
// with (filed under App settings, like the Actions and hotkeys window's restore), and the one way the tray runs an action.
public sealed partial class ShellViewModel
{
    /// <summary>What the tray flyout and menu show.</summary>
    public TrayFlyoutModel TrayFlyout { get; private set; }

    /// <summary>Runs a master action by id through the one run path (the tray menu). False when no such action is registered.</summary>
    public bool RunAction(string id) => _hotkeys.Run(id);

    private TrayFlyoutModel MakeTray()
    {
        var home = _allFooter.OfType<AppSettingsPageViewModel>().First().Title;
        _hotkeys.Register(new HotkeyAction("app.settings.open", $"Open {home}", home, "settings preferences options open window",
            _ => ShowPage(home)) { NeverBlock = true });
        _hotkeys.Register(new HotkeyAction("app.actions.open", $"Open {ActionsWindowService.Title}", home, "actions hotkeys shortcuts keys list open window",
            _ => _actionsWindow.Open()) { NeverBlock = true });
        return new TrayFlyoutModel(_hotkeys, _states, AppLifecycle.RequestShow);
    }

    /// <summary>Shows the main window at a page.</summary>
    private void ShowPage(string title)
    {
        AppLifecycle.RequestShow();
        OpenPage(title);
    }
}
