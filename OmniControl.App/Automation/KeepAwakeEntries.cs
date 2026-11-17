using OmniControl.App.Hotkeys;
using OmniControl.App.States;
using OmniControl.Core.Engine;

namespace OmniControl.App.Automation;

/// <summary>
/// Keep awake (the Power feature): a switch that stops the PC sleeping on its own while on, registered once as a state and a toggle action
/// so hotkeys, rules, the tray and another PC all use the same one. Off at every start and released at exit (<see cref="KeepAwake"/>).
/// </summary>
public sealed class KeepAwakeEntries : IDisposable
{
    private const string StateId = "power.keepawake", ActionId = "automation.power.keepawake";
    private readonly KeepAwake _awake = new();
    private readonly HotkeyRegistry _hotkeys;
    private readonly StateRegistry _states;

    public KeepAwakeEntries(HotkeyRegistry hotkeys, StateRegistry states)
    {
        _hotkeys = hotkeys;
        _states = states;
        states.Register(new StateEntry(StateId, "Keep awake", PowerActions.Power, StateKind.OnOff, () => StateValue.Of(_awake.IsOn, DateTime.Now),
            "Set by OmniControl: the PC does not go to sleep by itself while this is on.", Keywords: "stay awake never sleep caffeine insomnia",
            Set: value => Apply(value.Number is { } on && on != 0), Short: "Keep awake", SuggestedIcons: new[] { "coffee", "sun" }, Common: CommonGroups.Power));
        hotkeys.Register(new HotkeyAction(ActionId, "Toggle keep awake", AutomationService.Category, "stay awake never sleep caffeine insomnia",
            _ => Apply(!_awake.IsOn), group: PowerActions.Power) { PickerSection = PowerActions.Power, SuggestedIcons = new[] { "coffee", "sun" } });
    }

    private void Apply(bool on)
    {
        _awake.Set(on);
        _states.Notify(StateId);
    }

    public void Dispose()
    {
        _hotkeys.Unregister(ActionId);
        _states.Unregister(StateId);
        _awake.Dispose();
    }
}
