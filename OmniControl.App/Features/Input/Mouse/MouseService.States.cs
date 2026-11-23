using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Input.Mouse;

// The layer in force of each input family in the master list of states ("Mouse layer becomes Gaming"): its choices are the layers the
// family lists now, by their names; notified whenever a layer changes or is renamed.
public sealed partial class MouseService
{
    private readonly IStateRegistry _states;

    private static string StateId(InputFamily family) => $"mouse.layer.{family.Noun()}";

    private void RegisterStates()
    {
        _states.Register(new[] { InputFamily.Mouse, InputFamily.Trackpad }.Select(family => new StateEntry(StateId(family),
            family == InputFamily.Mouse ? "Mouse layer" : "Trackpad layer", ActionSections.Mouse, StateKind.Choice,
            () => StateValue.Of(LabelOf(family, LayerOf(family))), "The layer in force now (a held \"hold for layer\" button counts).",
            Choices: () => Settings.LayersOf(family).Listed(LayerOf(family)).Select(layer => LabelOf(family, layer)).ToList(),
            Keywords: $"{family.Noun()} layer profile")));
        _states.Register(new StateEntry(AutoscrollId, "Block apps' middle-click autoscroll", ActionSections.Mouse, StateKind.OnOff,
            () => StateValue.Of(Settings.BlockAutoscroll), "Read from the setting: on swallows a middle press that is not over a link, tab or button.",
            Set: value => SetBlockAutoscroll(value.Number == 1), Keywords: "middle click autoscroll scroll browser block"));
        _states.Register(new StateEntry(PauseFullScreenId, "Pause mouse features in full-screen games", ActionSections.Mouse, StateKind.OnOff,
            () => StateValue.Of(Settings.PauseInFullScreen), "Read from the setting: on makes every mouse input pass untouched while a full-screen app is in front.",
            Set: value => SetPauseInFullScreen(value.Number == 1), Keywords: "full screen fullscreen game pause mouse buttons gestures"));
        _states.Register(new StateEntry(ClickingId, "Repeated clicking running", ActionSections.Mouse, StateKind.OnOff,
            () => StateValue.Of(Clicking.IsRunning), "Read from the clicker itself: on while it is clicking.", Keywords: "repeated clicking auto clicker running"));
        Clicking.RunningChanged += () => _states.Notify(ClickingId);
        LayerChanged += NotifyLayer;
        LayersChanged += NotifyLayers;
    }

    /// The id of the state "Repeated clicking running".
    public const string ClickingId = "mouse.repeatclick.running";

    /// The id of the "Toggle app autoscroll" action and of the state it flips.
    public const string AutoscrollId = "mouse.autoscroll";

    /// Turns "Block apps' middle-click autoscroll" on or off for every button (the one global setting).
    public void SetBlockAutoscroll(bool on)
    {
        if (Settings.BlockAutoscroll == on) return;
        Settings.BlockAutoscroll = on;
        Apply();
        _states.Notify(AutoscrollId);
    }

    /// The id of the "Toggle pause in full-screen games" action and of the state it flips.
    public const string PauseFullScreenId = "mouse.pause.fullscreen";

    /// Turns "Pause in full-screen games" on or off.
    public void SetPauseInFullScreen(bool on)
    {
        if (Settings.PauseInFullScreen == on) return;
        Settings.PauseInFullScreen = on;
        Apply();
        _states.Notify(PauseFullScreenId);
    }

    public void TogglePauseInFullScreen() => SetPauseInFullScreen(!Settings.PauseInFullScreen);

    /// Turns "Send actions to remote PCs as keys" on or off.
    public void SetSendToRemoteAsKeys(bool on)
    {
        if (Settings.SendToRemoteAsKeys == on) return;
        Settings.SendToRemoteAsKeys = on;
        Apply();
    }

    /// Turns "Run as administrator" on or off (off removes the sign-in task). Turning it on while not elevated starts OmniControl again as
    /// administrator (the window then closes); the plain sentence it returns says why that did not happen (the setting is then put back to
    /// off), null otherwise. UI thread.
    public async Task<string?> SetRunAsAdministratorAsync(bool on)
    {
        if (Settings.RunAsAdministrator == on) return null;
        Settings.RunAsAdministrator = on;
        Apply();
        if (!on || await AdministratorMode.RestartElevatedAsync() is not { } problem) return null;
        Settings.RunAsAdministrator = false;
        Apply();
        return problem;
    }

    /// "Restore defaults" of the Where mouse features work group.
    public void RestoreWhereDefaults()
    {
        Settings.RestoreWhereDefaults();
        Apply();
        _states.Notify(PauseFullScreenId);
    }

    public void ToggleBlockAutoscroll() => SetBlockAutoscroll(!Settings.BlockAutoscroll);

    // The action row "Let the app's own autoscroll start" shows the same global setting (on = not blocked).
    string ISharedOptions.Read(ActionOption option) => Settings.BlockAutoscroll ? "0" : "1";

    void ISharedOptions.Write(ActionOption option, string value) => SetBlockAutoscroll(value != "1");

    private string LabelOf(InputFamily family, int layer) => Settings.LayersOf(family).Labels().GetValueOrDefault(layer) ?? layer.ToString();

    private void NotifyLayer(InputFamily family, int layer)
    {
        _states.Notify(StateId(family));
        NotifyLayerActiveStates(family);
    }

    private void NotifyLayers()
    {
        NotifyLayer(InputFamily.Mouse, 0);
        NotifyLayer(InputFamily.Trackpad, 0);
    }

    private void ForgetStates()
    {
        LayerChanged -= NotifyLayer;
        LayersChanged -= NotifyLayers;
        _states.Unregister(new[] { StateId(InputFamily.Mouse), StateId(InputFamily.Trackpad), AutoscrollId, PauseFullScreenId });
    }
}
