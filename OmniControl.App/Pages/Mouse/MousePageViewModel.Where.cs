using OmniControl.App.AppLists;
using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;

namespace OmniControl.App.Pages.Mouse;

/// <summary>
/// The "Where mouse features work" group: when OmniControl's mouse features stand aside (a full-screen game in front, an app on the "never
/// act in" list) and whether it runs as administrator so they also work over administrator programs. The service decides and acts; this only
/// shows the three settings and keeps them in step with changes made elsewhere (a hotkey, Restore defaults).
/// </summary>
public sealed partial class MousePageViewModel
{
    private const string WhereKeywords = "full screen fullscreen game pause never act exclude apps administrator admin elevated uac where mouse features work";
    private const string WhereGroupName = "Where mouse features work";

    private ToggleItem _pauseCard = null!;      // made by BuildWhereGroup, from the constructor
    private ToggleItem _administratorCard = null!;
    private AppScopeModel _neverScope = null!;

    /// <summary>The group: pause in full-screen games and the apps to never act in side by side, then run as administrator.</summary>
    public ItemGroup WhereGroup { get; private set; } = null!;

    /// <summary>The muted line under the group's cards.</summary>
    public string WhereNote => "Over a program run as administrator, mouse features do nothing unless this is on.";

    private void BuildWhereGroup(AppScopes scopes, HotkeyRegistry hotkeys)
    {
        _pauseCard = new ToggleItem("Pause in full-screen games", Settings.PauseInFullScreen, _mouse.SetPauseInFullScreen)
        {
            Half = true,
            Tip = "While a full-screen app is in front, every mouse button, wheel step and gesture goes to it untouched.",
        };
        _neverScope = scopes.Group($"{Title} › {WhereGroupName}", () => Settings.NeverActIn, scope =>
        {
            Settings.NeverActIn = scope;
            _mouse.Apply();
        });
        var never = new ScopeItem("Never act in", _neverScope)
        {
            Half = true,
            Tip = "In these apps buttons, gestures and the autoscroll block do nothing. Pick \"All except...\" and a list of apps.",
        };
        _administratorCard = new ToggleItem("Run as administrator", Settings.RunAsAdministrator, OnAdministratorPicked)
        {
            Note = "Works over admin windows too. Windows asks once at each start.",
            Tip = "Turning it on starts OmniControl again with Windows' administrator prompt.",
        };

        WhereGroup = new ItemGroup("Mouse/Where", WhereGroupName, new object[] { _pauseCard, never, _administratorCard })
        {
            Restore = _mouse.RestoreWhereDefaults,
            RestoreTip = "Puts pause in full-screen games, the apps to never act in and run as administrator back to their defaults.",
        };

        _mouse.Applied += ShowWhereSettings;
        hotkeys.Register(new HotkeyAction(MouseService.PauseFullScreenId, "Toggle pause in full-screen games", Title, "full screen fullscreen game pause mouse buttons gestures",
            _ => _mouse.TogglePauseInFullScreen()));
    }

    private void OnAdministratorPicked(bool on)
    {
        if (_mouse.SetRunAsAdministrator(on) is { } problem) _report(problem);
        ShowWhereSettings();
    }

    /// <summary>Shows the settings as the service has them now (a hotkey, Restore defaults or a refused prompt may have changed them).</summary>
    private void ShowWhereSettings()
    {
        _pauseCard.Load(Settings.PauseInFullScreen);
        _administratorCard.Load(Settings.RunAsAdministrator);
        _neverScope.Refresh();
    }

    private IEnumerable<SearchEntry> WhereSearchEntries() =>
        WhereGroup.Items.Select(i => Entry(i, Title, WhereKeywords, WhereGroupName));
}
