using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Pages.Mouse;

/// The "All mouse and trackpad features" group: gestures on or off (owned by <see cref="GestureGroups"/>), when OmniControl's mouse
/// features stand aside (a full-screen game in front, an app on the "never in these apps" list), whether it runs as administrator so they
/// also work over administrator programs, and whether actions go to a remote desktop in front as keys. The services decide and act; this
/// only shows the settings and keeps them in step with changes made elsewhere (a hotkey, Restore defaults).
public sealed partial class MousePageViewModel
{
    private const string WhereKeywords = "enable gestures on off full screen fullscreen game pause never exclude apps administrator admin elevated uac all mouse trackpad features remote desktop rdp virtual machine vm keys";
    private const string WhereGroupName = "All mouse and trackpad features";

    private ToggleItem _pauseCard = null!;      // made by BuildWhereGroup, from the constructor
    private ToggleItem _administratorCard = null!;
    private ToggleItem _remoteCard = null!;
    private AppScopeModel _neverScope = null!;

    /// The group: Enable gestures and pause in full-screen games side by side, then the apps to never act in and run as administrator, then sending actions to remote PCs as keys.
    public ItemGroup WhereGroup { get; private set; } = null!;

    private void BuildWhereGroup(AppScopes scopes, IActionRegistry hotkeys)
    {
        _pauseCard = new ToggleItem("Pause in full-screen games", Settings.PauseInFullScreen, _mouse.SetPauseInFullScreen)
        {
            Half = true,
            Note = "Any full-screen app counts, videos too",
            Tip = "While a full-screen app is in front, every mouse button, wheel step and gesture goes to it untouched.",
        };
        _neverScope = scopes.Exclusions($"{Title} › {WhereGroupName}", () => Settings.NeverActIn, scope =>
        {
            Settings.NeverActIn = scope;
            _mouse.Apply();
        });
        var never = new ScopeItem("Never in these apps", _neverScope)
        {
            Half = true,
            Note = "Overrides every group's Apps choice",
            Tip = "In these apps buttons, trackpad gestures, gestures and the autoscroll block do nothing.",
        };
        _administratorCard = new ToggleItem("Run as administrator", Settings.RunAsAdministrator, OnAdministratorPicked)
        {
            Half = true,
            Note = "Needed over admin windows",
            Tip = "Turning it on starts OmniControl again as administrator (Windows asks once) and adds its sign-in task, so later starts need no prompt.",
        };

        _remoteCard = new ToggleItem("Send actions to remote PCs as keys", Settings.SendToRemoteAsKeys, _mouse.SetSendToRemoteAsKeys)
        {
            Half = true,
            Note = "Remote Desktop, virtual machines",
            Tip = "While a remote desktop or virtual machine window is in front, a button's or trackpad gesture's action that has a standard shortcut "
                + "(volume, play or pause, next track, close window) is sent as that shortcut, so the other PC acts. In a Remote Desktop that is not "
                + "full screen, shortcuts it keeps on this PC go as its own replacements ("
                + string.Join(", ", RemoteInputApps.All.SelectMany(a => a.WindowedSwaps ?? Array.Empty<RemoteInputApps.KeySwap>()).Distinct().Select(s => $"{s.Keys} as {s.SentAs}"))
                + "); Windows-key shortcuts (Task view, Show desktop, snap, virtual desktops) have none and reach it only with its \"Apply Windows key "
                + "combinations: On the remote computer\". Works in: "
                + string.Join(", ", RemoteInputApps.All.Select(a => a.Name)) + ".",
        };

        WhereGroup = new ItemGroup("Mouse/Where", WhereGroupName, new object[] { Gestures.Enabled, _pauseCard, never, _administratorCard, _remoteCard })
        {
            Restore = () =>
            {
                _mouse.RestoreWhereDefaults();
                Gestures.RestoreEnabled();
            },
            RestoreTip = "Turns gestures on and puts pause in full-screen games, the apps to never act in, run as administrator and sending actions to remote PCs as keys back to their defaults.",
        };

        _mouse.Applied += ShowWhereSettings;
        var pauseFamily = new OnOffFamily(MouseService.PauseFullScreenId, "Pause in full-screen games", MouseService.PauseFullScreenId);
        var pause = new HotkeyAction(MouseService.PauseFullScreenId, "Toggle pause in full-screen games", Title, "full screen fullscreen game pause mouse buttons gestures",
            _ => _mouse.TogglePauseInFullScreen()) { Family = pauseFamily.Family, FamilyRole = OnOffFamily.ToggleRole };
        hotkeys.Register(pauseFamily.Siblings(pause, _mouse.SetPauseInFullScreen).Prepend(pause));
    }

    private void OnAdministratorPicked(bool on) => _ = Guard.RunAsync("Run as administrator", async () =>
    {
        if (await _mouse.SetRunAsAdministratorAsync(on) is { } problem) _report(problem);
        ShowWhereSettings();
    });

    /// Shows the settings as the service has them now (a hotkey, Restore defaults or a refused prompt may have changed them).
    private void ShowWhereSettings()
    {
        _pauseCard.Load(Settings.PauseInFullScreen);
        _administratorCard.Load(Settings.RunAsAdministrator);
        _remoteCard.Load(Settings.SendToRemoteAsKeys);
        _neverScope.Refresh();
    }

    private IEnumerable<SearchEntry> WhereSearchEntries() =>
        WhereGroup.Items.Select(i => Entry(i, Title, WhereKeywords, WhereGroupName));
}
