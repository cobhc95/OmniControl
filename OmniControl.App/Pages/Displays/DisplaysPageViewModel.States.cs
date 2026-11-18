using OmniControl.App.States;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

// Each attached display's hardware levels (brightness, contrast... every level of DisplayControls.Hardware) in the master list of states,
// registered while the display is attached and read from its cards; notified whenever the display was read or a value changed. Also the
// warm-light schedule's on/off. Each can be set from outside the page (the tray flyout) the way its card does.
public sealed partial class DisplaysPageViewModel
{
    private const string LevelWords = "What the monitor reported when it was read, and every change made here since (a change on the monitor's own buttons is seen at the next read).";
    private const string WarmLightId = WarmLightItem.StateId, WarmWords = "The page's Warm light at night switch.";

    private static string StateId(HardwareDisplayItem display, HardwareLevel level) => $"displays.{display.Id}.{level.Spec.Key}";

    private void RegisterStates(HardwareDisplayItem display) =>
        _states.Register(DisplayControls.Hardware.Select(level => new StateEntry(StateId(display, level), $"{level.Spec.Label} ({display.Name})", Title,
            StateKind.Level, () => display.LevelOf(level) is { } value ? StateValue.Of(value) : null, LevelWords,
            Heading: Services.Heading.Join(Title, display.Name), Min: level.Spec.Min, Max: level.Spec.Max, Unit: "%", Keywords: $"monitor display screen {level.Spec.Keywords}",
            Set: value => { if (value.Number is { } number) display.SetLevel(level, (int)Math.Round(number)); }, Short: display.Name, SuggestedIcons: new[] { "sun", "sun-low", "brightness" }, Common: level.Code == VcpCode.Brightness ? CommonGroups.Screens : null)));

    private void ForgetStates(HardwareDisplayItem display)
    {
        _states.Unregister(DisplayControls.Hardware.Select(level => StateId(display, level)));
        if (_powerStates.Remove(display.Id)) _states.Unregister(PowerId(display));
    }

    private const string PowerWords = "What the monitor said when it was read, then the last power command sent from here (a press of the monitor's own button is not seen).";

    private readonly HashSet<string> _powerStates = new();

    private static string PowerId(HardwareDisplayItem display) => $"displays.{display.Id}.power";

    /// <summary>A display that has said it takes DDC power commands has its screen power in the list of states (on or off, settable); one that has not has none. Called after displays were read.</summary>
    private void SyncPowerStates()
    {
        foreach (var display in _hardware)
        {
            if (display.HasScreenPower && _powerStates.Add(display.Id))
                _states.Register(new StateEntry(PowerId(display), $"Screen power ({display.Name})", Title, StateKind.OnOff, () => display.ScreenOn is { } on ? StateValue.Of(on) : null,
                    PowerWords, Heading: Services.Heading.Join(Title, display.Name), Keywords: "monitor display screen power on off sleep standby ddc", Short: display.Name,
                    Set: value => display.SetScreenOn(value.Number is { } on && on != 0), SuggestedIcons: new[] { "device-desktop", "power", "moon" }, ScreenPower: true, Common: CommonGroups.Screens));
            else if (!display.HasScreenPower && _powerStates.Remove(display.Id)) _states.Unregister(PowerId(display));
        }
    }

    /// <summary>The Hardware group's master switch: every screen that can be switched on its own, and "Turn off displays" for the case that some cannot.</summary>
    private GroupSwitch ScreenMaster() => ScreenPowerSwitch.All(
        () => _hardware.Where(h => h.HasScreenPower).Select(h => new ScreenPowerSwitch.Screen(() => h.ScreenOn, h.SetScreenOn)).ToList(),
        () => _hardware.Count > 0 && _hardware.All(h => h.HasScreenPower) && _softwareItems.Count <= _hardware.Count,
        TurnOffDisplays);

    /// <summary>The page's states that live as long as the page: the warm-light schedule.</summary>
    private void RegisterPageStates()
    {
        _states.Register(new StateEntry(WarmLightId, "Warm light at night", Title, StateKind.OnOff, () => StateValue.Of(_warm.IsOn), WarmWords,
            Keywords: "warm light night schedule evening blue light filter", Set: value => _warm.SetEnabled(value.Number is { } on && on != 0),
            Short: "Night light", Icon: Icons.IconLibrary.Moon, SuggestedIcons: new[] { Icons.IconLibrary.Moon, "sun-moon", "eye" }));
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
        foreach (var display in _hardware.Where(h => h.HasScreenPower)) _states.Notify(PowerId(display));
        Hardware.RefreshMaster();
    }
}
