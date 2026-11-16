using OmniControl.App.States;
using OmniControl.Core.Engine;

namespace OmniControl.App.Automation;

/// <summary>
/// The power states the Automation feature registers in the master list of states: on battery, the battery's charge and the power mode.
/// Read from Windows when asked (cheap calls); <see cref="AutomationService"/> notifies them at every look (<see cref="Services.Timing.AutomationTick"/>).
/// </summary>
public static class PowerStates
{
    public const string OnBattery = "power.battery", Charge = "power.battery.level", Mode = "power.mode";

    private const string Looked = "Windows' power status, looked at every few seconds while rules are on.";

    public static IReadOnlyList<string> Ids { get; } = new[] { OnBattery, Charge, Mode };

    public static IEnumerable<StateEntry> Make()
    {
        yield return new StateEntry(OnBattery, "On battery", PowerActions.Power, StateKind.OnOff,
            () => PowerState.IsOnBattery() is { } on ? StateValue.Of(on, DateTime.Now) : null, Looked, Keywords: "power source plugged unplugged laptop charger");
        yield return new StateEntry(Charge, "Battery charge", PowerActions.Power, StateKind.Level,
            () => PowerState.BatteryPercent() is { } percent ? StateValue.Of(percent, DateTime.Now) : null, Looked, Unit: "%", Keywords: "battery level percent low");
        yield return new StateEntry(Mode, "Power mode", PowerActions.Power, StateKind.Choice,
            () => PowerCommands.CurrentMode() is { } mode ? StateValue.Of(NameOf(mode), DateTime.Now) : null, Looked,
            Choices: () => PowerActions.Modes.Select(m => m.Name).ToList(), Keywords: "efficiency balanced performance");
    }

    private static string NameOf(PowerMode mode) => PowerActions.Modes.First(m => m.Mode == mode).Name;
}
