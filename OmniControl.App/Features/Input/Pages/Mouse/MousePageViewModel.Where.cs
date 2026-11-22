using OmniControl.App.Features.AppLists;
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

    private void BuildWhereGroup(AppScopes scopes)
    {
        _pauseCard = new ToggleItem(FullScreenCard, Settings.PauseInFullScreen, _mouse.SetPauseInFullScreen) { Half = true };
        _neverScope = scopes.Exclusions($"{Title} › {WhereGroupName}", () => Settings.NeverActIn, scope =>
        {
            Settings.NeverActIn = scope;
            _mouse.Apply();
        });
        var never = new ScopeItem(NeverCard, _neverScope) { Half = true };
        _administratorCard = new ToggleItem(AdministratorCard, Settings.RunAsAdministrator, OnAdministratorPicked) { Half = true };
        _remoteCard = new ToggleItem(RemoteCard, Settings.SendToRemoteAsKeys, _mouse.SetSendToRemoteAsKeys) { Half = true };

        WhereGroup = new ItemGroup("Mouse/Where", WhereGroupName, new object[] { Gestures.Enabled, _pauseCard, never, _administratorCard, _remoteCard })
        {
            Restore = () =>
            {
                _mouse.RestoreWhereDefaults();
                Gestures.RestoreEnabled();
            },
            RestoreTip = WhereRestoreTip,
        };

        _followed.Follow<Action>(h => _mouse.Applied += h, h => _mouse.Applied -= h, ShowWhereSettings);
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

}
