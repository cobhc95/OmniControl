using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Displays.Pages;

// Each attached display's hardware levels (brightness, contrast... every level of DisplayControls.Hardware) in the master list of states,
// registered while the display is attached and read from its model; notified whenever the display was read or a value changed. Each
// can be set from outside the page (the tray flyout) the way its card does, through the model.
public sealed partial class DisplaysService
{
    private const string LevelWords = "What the monitor reported when it was read, and every change made here since (a change on the monitor's own buttons is seen at the next read).";
    private const string LaptopNote = "Laptop panel", MonitorNote = "External monitor";

    private static string StateId(HardwareDisplayModel display, HardwareLevel level) => $"displays.{display.Id}.{level.Spec.Key}";

    private void RegisterStates(HardwareDisplayModel display) =>
        _states.Register(DisplayControls.Hardware.Select(level => new StateEntry(StateId(display, level), $"{level.Spec.Label} ({display.Name})", Title,
            StateKind.Level, () => display.LevelOf(level) is { } value ? StateValue.Of(value) : null, LevelWords,
            Heading: Shared.Services.Heading.Join(Title, display.Name), Min: level.Spec.Min, Max: level.Spec.Max, Unit: "%", Keywords: $"monitor display screen {level.Spec.Keywords}",
            Set: value => { if (value.Number is { } number) display.Set(level, (int)Math.Round(number)); }, Short: display.Name, SuggestedIcons: new[] { "sun", "sun-low", "brightness" }, Common: level.Code == VcpCode.Brightness ? CommonGroups.Screens : null,
            Note: display.IsLaptopPanel ? LaptopNote : MonitorNote)));

    private void ForgetStates(HardwareDisplayModel display)
    {
        _states.Unregister(DisplayControls.Hardware.Select(level => StateId(display, level)));
        if (_powerStates.Remove(display.Id)) _states.Unregister(PowerId(display));
    }

    private const string PowerWords = "What the monitor said when it was read, then the last power command sent from here (a press of the monitor's own button is not seen).";

    private readonly HashSet<string> _powerStates = new();

    private static string PowerId(HardwareDisplayModel display) => $"displays.{display.Id}.power";

    /// A display that has said it takes DDC power commands has its screen power in the list of states (on or off, settable); one that has not has none. Called after displays were read.
    private void SyncPowerStates()
    {
        foreach (var display in _hardware)
        {
            if (display.HasScreenPower && _powerStates.Add(display.Id))
                _states.Register(new StateEntry(PowerId(display), $"Screen power ({display.Name})", Title, StateKind.OnOff, () => display.ScreenOn is { } on ? StateValue.Of(on) : null,
                    PowerWords, Heading: Shared.Services.Heading.Join(Title, display.Name), Keywords: "monitor display screen power on off sleep standby ddc", Short: display.Name,
                    Set: value => display.SetScreenOn(value.Number is { } on && on != 0), SuggestedIcons: new[] { "device-desktop", "power", "moon" }, ScreenPower: true, Common: CommonGroups.Screens));
            else if (!display.HasScreenPower && _powerStates.Remove(display.Id)) _states.Unregister(PowerId(display));
        }
    }

    /// The Hardware group's master switch: every screen that can be switched on its own, and "Turn off displays" for the case that some cannot.
    internal GroupSwitch ScreenMaster() => ScreenPowerSwitch.All(
        () => _hardware.Where(h => h.HasScreenPower).Select(h => new ScreenPowerSwitch.Screen(() => h.ScreenOn, h.SetScreenOn)).ToList(),
        () => _hardware.Count > 0 && _hardware.All(h => h.HasScreenPower) && _softwareDisplays.Count <= _hardware.Count,
        TurnOffDisplays, knownOff: () => _systemScreens.IsOff);

    /// Windows said the screens went off, came on or dimmed (listener thread): every screen's switch and state follows on the UI thread, and so does every paired PC through the states.
    private void OnScreensChanged() => Dispatcher.UIThread.Post(() =>
    {
        foreach (var display in _hardware) display.ScreensChanged();
        NotifyScreenPower();
    });

    private void NotifyScreenPower()
    {
        foreach (var display in _hardware.Where(h => h.HasScreenPower)) _states.Notify(PowerId(display));
        HardwareValuesChanged?.Invoke();
    }

    /// A display was read or one of its values changed: its states are read again, and the page (its switches and monitor presets) looks again.
    private void OnDisplayValues()
    {
        foreach (var display in _hardware)
        foreach (var level in DisplayControls.Hardware) _states.Notify(StateId(display, level));
        NotifyScreenPower();
    }
}
