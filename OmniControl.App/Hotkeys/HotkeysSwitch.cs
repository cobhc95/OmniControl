using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Services;
using OmniControl.App.States;

namespace OmniControl.App.Hotkeys;

/// <summary>
/// The hotkeys master switch (docs/mockups/hotkeys-master-switch.html): off pauses every keyboard hotkey (the keys are kept; the registry stops
/// registering them, except those of actions flagged <see cref="HotkeyAction.NeverBlock"/>), on makes them all work again. Mouse buttons, gestures
/// and rules are not paused. Kept as an app setting, and registered once as a state and a toggle action so the tray, a mouse button or a rule can use it.
/// </summary>
public sealed partial class HotkeysSwitch : ObservableObject, IDisposable
{
    private const string StateId = "app.hotkeys.on", ActionId = "app.hotkeys.toggle";
    private readonly HotkeyRegistry _registry;
    private readonly StateRegistry _states;
    private readonly Action<string> _report;

    public string Label => "Hotkeys";

    [ObservableProperty] private bool _isOn;

    public HotkeysSwitch(HotkeyRegistry registry, StateRegistry states, string category, Action<string> report)
    {
        (_registry, _states, _report) = (registry, states, report);
        _isOn = AppSettings.Current.HotkeysOn;
        registry.PauseKeys(!_isOn);
        states.Register(new StateEntry(StateId, "Hotkeys on", category, StateKind.OnOff, () => StateValue.Of(IsOn),
            "Set by OmniControl: off pauses every keyboard hotkey (mouse buttons, gestures and rules keep working).", Keywords: "hotkeys keyboard shortcuts pause resume keys",
            Set: value => IsOn = value.Number is { } on && on != 0, Short: "Hotkeys", SuggestedIcons: new[] { "keyboard" }));
        var family = new OnOffFamily(ActionId, "Hotkeys", StateId);
        var toggle = new HotkeyAction(ActionId, "Hotkeys: on/off", category, "hotkeys keyboard shortcuts pause resume keys toggle",
            _ => IsOn = !IsOn) { NeverBlock = true, SuggestedIcons = new[] { "keyboard" }, Family = family.Family, FamilyRole = OnOffFamily.ToggleRole };
        registry.Register(family.Siblings(toggle, on => IsOn = on).Prepend(toggle));
    }

    partial void OnIsOnChanged(bool value)
    {
        _registry.PauseKeys(!value);
        var settings = AppSettings.Current;
        settings.HotkeysOn = value;
        settings.Save();
        _states.Notify(StateId);
        _report(value ? "Hotkeys are on again." : "Hotkeys are paused.");
    }

    public void Dispose()
    {
        _registry.Unregister(ActionId);
        foreach (var id in OnOffFamily.SiblingIds(ActionId)) _registry.Unregister(id);
        _states.Unregister(StateId);
    }
}
