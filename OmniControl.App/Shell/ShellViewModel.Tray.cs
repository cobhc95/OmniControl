using OmniControl.App.Hotkeys;
using OmniControl.App.Pages;
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

    private WinAKey _winA = null!;

    /// <summary>Runs a master action by id through the one run path (the tray menu). False when no such action is registered.</summary>
    public bool RunAction(string id) => _hotkeys.Run(id);

    private TrayFlyoutModel MakeTray()
    {
        var home = _allFooter.OfType<AppSettingsPageViewModel>().First().Title;
        _hotkeys.Register(new HotkeyAction("app.settings.open", $"Open {home}", home, "settings preferences options open window",
            _ => ShowPage(home)) { NeverBlock = true });
        _hotkeys.Register(new HotkeyAction("app.actions.open", $"Open {ActionsWindowService.Title}", home, "actions hotkeys shortcuts keys list open window",
            _ => _actionsWindow.Open()) { NeverBlock = true });
        var flyout = new TrayFlyoutModel(_hotkeys, _states, _iconPicker, () => AppLifecycle.RequestShow("the quick panel's Open OmniControl"), ShowInApp);
        const string toggleId = "app.tray.toggle";
        _winA = new WinAKey(toggleId, _hotkeys.Run, Report);
        _hotkeys.Register(new HotkeyAction(toggleId, "Show or hide quick panel", home, "tray panel flyout quick settings show hide toggle win a",
            _ => flyout.RequestToggle())
        {
            NeverBlock = true, DefaultKey = "Alt+Z", SuggestedIcons = new[] { "app", "home" },
            Editor = new ActionEditor(
                new[] { ActionOption.Toggle(WinAKey.OptionKey, "Use for Win+A (replaces Windows' quick settings)", note: "Win+A opens this panel instead of Windows' quick settings") },
                () => new Dictionary<string, string> { [WinAKey.OptionKey] = _winA.IsOn ? "1" : "0" },
                (key, value) =>
                {
                    if (key == WinAKey.OptionKey) _winA.Set(value == "1");
                    return null;
                }),
        });
        return flyout;
    }

    /// <summary>
    /// "Show in OmniControl" of a tray slider or tile: an action opens the Actions and hotkeys window at it; a state alone opens the main
    /// window at the page that owns it, which brings the state's owner forward (<see cref="PageViewModel.Reveal"/>: a smart device is selected).
    /// </summary>
    private void ShowInApp(TrayTarget target)
    {
        if (target.Action is { } action) _actionsWindow.Open(action.Id);
        else if (target.State is { } state)
        {
            ShowPage(state.Group);
            _allPages.Concat(_allFooter).FirstOrDefault(p => p.Title == state.Group)?.Reveal(state.Id);
        }
    }

    /// <summary>Shows the main window at a page.</summary>
    private void ShowPage(string title)
    {
        AppLifecycle.RequestShow($"a request to open {title} ({RunChain.Describe(_hotkeys.Chain)})");
        OpenPage(title);
    }
}
