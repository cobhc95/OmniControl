using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Automation;

/// Keep awake (the Power feature): a switch that stops the PC sleeping on its own while on, registered once as a state and a toggle action
/// so hotkeys, rules, the tray and another PC all use the same one. Off at every start and released at exit (<see cref="KeepAwake"/>).
public sealed class KeepAwakeEntries : IDisposable
{
    private const string StateId = "power.keepawake", ActionId = "automation.power.keepawake";
    private readonly KeepAwake _awake = new();
    private readonly IActionRegistry _hotkeys;
    private readonly IStateRegistry _states;

    public KeepAwakeEntries(IActionRegistry hotkeys, IStateRegistry states)
    {
        _hotkeys = hotkeys;
        _states = states;
        states.Register(new StateEntry(StateId, "Keep awake", ActionSections.Power, StateKind.OnOff, () => StateValue.Of(_awake.IsOn, DateTime.Now),
            "Set by OmniControl: the PC does not go to sleep by itself while this is on.", Keywords: "stay awake never sleep caffeine insomnia",
            Set: value => Apply(value.Number is { } on && on != 0), Short: "Keep awake", SuggestedIcons: new[] { "coffee", "sun" }, Common: CommonGroups.Power));
        var family = new OnOffFamily(ActionId, "Keep awake", StateId);
        var toggle = new HotkeyAction(ActionId, "Toggle keep awake", ActionSections.Automation, "stay awake never sleep caffeine insomnia",
            _ => Apply(!_awake.IsOn), group: ActionSections.Power)
            { PickerSection = ActionSections.Power, SuggestedIcons = new[] { "coffee", "sun" }, Family = family.Family, FamilyRole = OnOffFamily.ToggleRole };
        hotkeys.Register(family.Siblings(toggle, Apply).Prepend(toggle));
    }

    private void Apply(bool on)
    {
        _awake.Set(on);
        _states.Notify(StateId);
    }

    public void Dispose()
    {
        _hotkeys.Unregister(ActionId);
        foreach (var id in OnOffFamily.SiblingIds(ActionId)) _hotkeys.Unregister(id);
        _states.Unregister(StateId);
        _awake.Dispose();
    }
}
