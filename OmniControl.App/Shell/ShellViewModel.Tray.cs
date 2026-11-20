using OmniControl.App.Shared.Contracts;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Settings.Pages;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.Tray;

namespace OmniControl.App.Shell;

// The tray's share of the shell: the flyout's model over the master lists the shell owns, the master actions the tray menu opens windows
// with (filed under App settings, like the Actions and hotkeys window's restore), and the one way the tray runs an action.
public sealed partial class ShellViewModel
{
    /// What the tray flyout and menu show.
    public TrayFlyoutModel TrayFlyout { get; private set; }

    /// The master list of hardware watches, where the tray icon registers its own.
    public IHardwareWatches Hardware => _hardware;

    private WinAKey _winA = null!;

    private const string SearchActionId = "app.search.focus";

    /// The key the main window itself gives its search box (it works only inside the window, see MainWindow); named while no hotkey is bound to the search action.
    private const string SearchWindowKey = "Ctrl F";

    private ActionKeyTip? _searchKeyTip;

    /// Asks the main window to put the cursor in its search box.
    public event Action? SearchFocusRequested;

    /// The keys the search box's pill names: the search action's own key when one is bound, else the window's Ctrl+F.
    public string SearchShortcut => _searchKeyTip is { Keys.Length: > 0 } tip ? tip.Keys : SearchWindowKey;

    /// Runs a master action by id through the one run path (the tray menu). False when no such action is registered.
    public bool RunAction(string id) => _hotkeys.Run(id);

    /// Made before App settings, whose Tray icon cards read its look; its actions are registered later by <see cref="RegisterTray"/>.
    private TrayFlyoutModel MakeTray() =>
        new(_hotkeys, _hotkeys.Run, _states, _iconPicker, () => AppLifecycle.RequestShow("the quick panel's Open OmniControl"), ShowInApp, _volume.Mixer.WatchClosely);

    private void RegisterTray(TrayFlyoutModel flyout)
    {
        var home = _allFooter.OfType<AppSettingsPageViewModel>().First().Title;
        _hotkeys.Register(new HotkeyAction("app.settings.open", $"Open {home}", home, "settings preferences options open window",
            _ => ShowPage(home)) { NeverBlock = true });
        _hotkeys.Register(new HotkeyAction("app.actions.open", $"Open {ActionsWindowService.Title}", home, "actions hotkeys shortcuts keys list open window",
            _ => _actionsWindow.Open()) { NeverBlock = true });
        // No default key: hotkeys are system-wide, so a bound Ctrl+F would take it from every other program. The window has its own.
        _hotkeys.Register(new HotkeyAction(SearchActionId, "Search settings", home, "search find focus box",
            _ =>
            {
                AppLifecycle.RequestShow("a request to search");
                SearchFocusRequested?.Invoke();
            }) { NeverBlock = true, SuggestedIcons = new[] { "search" } });
        _searchKeyTip = new ActionKeyTip(_hotkeys, SearchActionId, "Search settings");
        _searchKeyTip.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ActionKeyTip.Keys)) OnPropertyChanged(nameof(SearchShortcut)); };
        const string toggleId = TrayFlyoutModel.ToggleActionId;
        _winA = new WinAKey(toggleId, _hotkeys.Run, Report, _settings.Slot(s => s.WinAForPanel, (s, on) => s.WinAForPanel = on));
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
    }

    /// "Show in OmniControl" of a tray slider or tile: an action opens the Actions and hotkeys window at it; a state alone opens the main
    /// window at the page that owns it, which brings the state's owner forward (<see cref="PageViewModel.Reveal"/>: a smart device is selected).
    private void ShowInApp(TrayTarget target)
    {
        if (target.Action is { } action) _actionsWindow.Open(action.Id);
        else if (target.State is { } state)
        {
            ShowPage(state.Group);
            _allPages.Concat(_allFooter).FirstOrDefault(p => p.Title == state.Group)?.Reveal(state.Id);
        }
    }

    /// Shows the main window at a page.
    private void ShowPage(string title)
    {
        AppLifecycle.RequestShow($"a request to open {title} ({RunChain.Describe(_hotkeys.Chain)})");
        OpenPage(title);
    }
}
