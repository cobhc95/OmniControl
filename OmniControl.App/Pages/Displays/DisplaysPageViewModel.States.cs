using OmniControl.App.States;

namespace OmniControl.App.Pages.Displays;

// Each attached display's hardware levels (brightness, contrast... every level of DisplayControls.Hardware) in the master list of states,
// registered while the display is attached and read from its cards; notified whenever the display was read or a value changed. Also the
// warm-light schedule's on/off. Each can be set from outside the page (the tray flyout) the way its card does.
public sealed partial class DisplaysPageViewModel
{
    private const string LevelWords = "What the monitor reported when it was read, and every change made here since (a change on the monitor's own buttons is seen at the next read).";
    private const string WarmLightId = "displays.warmlight", WarmWords = "The page's Warm light at night switch.";

    private static string StateId(HardwareDisplayItem display, HardwareLevel level) => $"displays.{display.Id}.{level.Spec.Key}";

    private void RegisterStates(HardwareDisplayItem display) =>
        _states.Register(DisplayControls.Hardware.Select(level => new StateEntry(StateId(display, level), $"{level.Spec.Label} ({display.Name})", Title,
            StateKind.Level, () => display.LevelOf(level) is { } value ? StateValue.Of(value) : null, LevelWords,
            Heading: Services.Heading.Join(Title, display.Name), Min: level.Spec.Min, Max: level.Spec.Max, Unit: "%", Keywords: $"monitor display screen {level.Spec.Keywords}",
            Set: value => { if (value.Number is { } number) display.SetLevel(level, (int)Math.Round(number)); }, Short: display.Name)));

    private void ForgetStates(HardwareDisplayItem display) => _states.Unregister(DisplayControls.Hardware.Select(level => StateId(display, level)));

    /// <summary>The page's states that live as long as the page: the warm-light schedule.</summary>
    private void RegisterPageStates()
    {
        _states.Register(new StateEntry(WarmLightId, "Warm light at night", Title, StateKind.OnOff, () => StateValue.Of(_warm.IsOn), WarmWords,
            Keywords: "warm light night schedule evening blue light filter", Set: value => _warm.SetEnabled(value.Number is { } on && on != 0),
            Short: "Night light", Icon: "MoonIcon"));
        _scheduler.Changed += NotifyWarmLight;
    }

    private void NotifyWarmLight() => _states.Notify(WarmLightId);

    private void ForgetPageStates()
    {
        _scheduler.Changed -= NotifyWarmLight;
        _states.Unregister(WarmLightId);
    }

    /// <summary>A display was read or one of its values changed: the monitor presets look again, and its states are read again.</summary>
    private void OnDisplayValues()
    {
        _monitorPresets.Refresh();
        foreach (var display in _hardware)
        foreach (var level in DisplayControls.Hardware) _states.Notify(StateId(display, level));
    }
}
