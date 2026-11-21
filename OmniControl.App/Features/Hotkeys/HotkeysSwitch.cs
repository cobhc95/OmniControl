using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Hotkeys;

/// The hotkeys master switch: off pauses every keyboard hotkey (the keys are kept; the registry stops
/// registering them, except those of actions flagged <see cref="HotkeyAction.NeverBlock"/>), on makes them all work again. Mouse buttons, gestures
/// and rules are not paused. Kept as an app setting, and registered as a state and a toggle action so the tray, a mouse button or a rule can use it.
/// No key works until the "Hotkeys and automation" feature runs (<see cref="Use"/>): its module turns them on and off with it.
public sealed partial class HotkeysSwitch : ObservableObject, IDisposable
{
    private const string StateId = "app.hotkeys.on", ActionId = "app.hotkeys.toggle";
    private readonly HotkeyRegistry _registry;
    private readonly IStateRegistry _states;
    private readonly string _category;
    private readonly Action<string> _report;
    private readonly ISettingSlot<bool> _on;

    public string Label => "Hotkeys";

    [ObservableProperty] private bool _isOn;

    /// The "Hotkeys and automation" feature runs: the switch, its state and its action exist (the Actions and hotkeys window shows the switch).
    [ObservableProperty] private bool _isAvailable;

    /// <param name="on">The setting that keeps the switch (on by default).</param>
    public HotkeysSwitch(HotkeyRegistry registry, IStateRegistry states, string category, Action<string> report, ISettingSlot<bool> on)
    {
        (_registry, _states, _category, _report, _on) = (registry, states, category, report, on);
        _isOn = on.Value;
        registry.PauseKeys(!_isOn);
        registry.StopKeys(true);
    }

    /// The "Hotkeys and automation" feature started (true) or stopped: the keys are registered with Windows, or none is.
    public void Use(bool on)
    {
        if (on == IsAvailable) return;
        IsAvailable = on;
        _registry.StopKeys(!on);
        if (on) Register();
        else Unregister();
    }

    private void Register()
    {
        _states.Register(new StateEntry(StateId, "Hotkeys on", _category, StateKind.OnOff, () => StateValue.Of(IsOn),
            "Set by OmniControl: off pauses every keyboard hotkey (mouse buttons, gestures and rules keep working).", Keywords: "hotkeys keyboard shortcuts pause resume keys",
            Set: value => IsOn = value.Number is { } on && on != 0, Short: "Hotkeys", SuggestedIcons: new[] { "keyboard" }));
        var family = new OnOffFamily(ActionId, "Hotkeys", StateId);
        var toggle = new HotkeyAction(ActionId, "Hotkeys: on/off", _category, "hotkeys keyboard shortcuts pause resume keys toggle",
            _ => IsOn = !IsOn) { NeverBlock = true, SuggestedIcons = new[] { "keyboard" }, Family = family.Family, FamilyRole = OnOffFamily.ToggleRole };
        _registry.Register(family.Siblings(toggle, on => IsOn = on).Prepend(toggle));
    }

    private void Unregister()
    {
        _registry.Unregister(ActionId);
        foreach (var id in OnOffFamily.SiblingIds(ActionId)) _registry.Unregister(id);
        _states.Unregister(StateId);
    }

    partial void OnIsOnChanged(bool value)
    {
        _registry.PauseKeys(!value);
        _on.Value = value;
        _states.Notify(StateId);
        _report(value ? "Hotkeys are on again." : "Hotkeys are paused.");
    }

    public void Dispose()
    {
        if (IsAvailable) Unregister();
    }
}
