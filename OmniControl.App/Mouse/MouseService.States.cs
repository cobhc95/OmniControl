using OmniControl.App.Hotkeys;
using OmniControl.App.States;

namespace OmniControl.App.Mouse;

// The layer in force of each input family in the master list of states ("Mouse layer becomes Gaming"): its choices are the layers the
// family lists now, by their names; notified whenever a layer changes or is renamed.
public sealed partial class MouseService
{
    private readonly StateRegistry _states;

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
        LayerChanged += NotifyLayer;
        LayersChanged += NotifyLayers;
    }

    /// <summary>The id of the "Toggle app autoscroll" action and of the state it flips.</summary>
    public const string AutoscrollId = "mouse.autoscroll";

    /// <summary>Turns "Block apps' middle-click autoscroll" on or off for every button (the one global setting).</summary>
    public void SetBlockAutoscroll(bool on)
    {
        if (Settings.BlockAutoscroll == on) return;
        Settings.BlockAutoscroll = on;
        Apply();
        _states.Notify(AutoscrollId);
    }

    public void ToggleBlockAutoscroll() => SetBlockAutoscroll(!Settings.BlockAutoscroll);

    // The action row "Let the app's own autoscroll start" shows the same global setting (on = not blocked).
    string ISharedOptions.Read(ActionOption option) => Settings.BlockAutoscroll ? "0" : "1";

    void ISharedOptions.Write(ActionOption option, string value) => SetBlockAutoscroll(value != "1");

    private string LabelOf(InputFamily family, int layer) => Settings.LayersOf(family).Labels().GetValueOrDefault(layer) ?? layer.ToString();

    private void NotifyLayer(InputFamily family, int layer) => _states.Notify(StateId(family));

    private void NotifyLayers()
    {
        NotifyLayer(InputFamily.Mouse, 0);
        NotifyLayer(InputFamily.Trackpad, 0);
    }

    private void ForgetStates()
    {
        LayerChanged -= NotifyLayer;
        LayersChanged -= NotifyLayers;
        _states.Unregister(new[] { StateId(InputFamily.Mouse), StateId(InputFamily.Trackpad), AutoscrollId });
    }
}
