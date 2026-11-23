using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Automation;

/// The power states the Automation feature registers in the master list of states: on battery, the battery's charge and the power mode.
/// Read from Windows when asked (cheap calls); <see cref="AutomationService"/> notifies them at every look (<see cref="Shared.Services.Timing.AutomationTick"/>).
public static class PowerStates
{
    public const string OnBattery = "power.battery", Charge = "power.battery.level", Status = "power.battery.status", Mode = "power.mode";

    private const string Looked = "Windows' power status, looked at every few seconds while rules are on.";
    private const string OnBatteryText = "On battery", PluggedInText = "Plugged in", ChargingText = "Plugged in · charging";

    public static IReadOnlyList<string> Ids { get; } = new[] { OnBattery, Charge, Status, Mode };

    public static IEnumerable<StateEntry> Make()
    {
        yield return new StateEntry(OnBattery, OnBatteryText, ActionSections.Power, StateKind.OnOff,
            () => PowerState.IsOnBattery() is { } on ? StateValue.Of(on, DateTime.Now) : null, Looked, Keywords: "power source plugged unplugged laptop charger",
            Wording: new StateWords("the PC", "starts running on battery", "is plugged in", "changes power source"));
        yield return new StateEntry(Charge, "Battery charge", ActionSections.Power, StateKind.Level,
            () => PowerState.BatteryPercent() is { } percent ? StateValue.Of(percent, DateTime.Now) : null, Looked, Unit: "%", Keywords: "battery level percent low",
            Common: PowerState.HasBattery() ? CommonGroups.Power : null);
        yield return new StateEntry(Status, "Battery status", ActionSections.Power, StateKind.Choice,
            () => PowerState.Battery() is { } battery ? new StateValue(Number(battery), StatusOf(battery), DateTime.Now, TimeLeft(battery)) : null, Looked,
            Choices: () => new[] { OnBatteryText, PluggedInText, ChargingText }, Keywords: "battery charging plugged time left", Common: PowerState.HasBattery() ? CommonGroups.Power : null);
        yield return new StateEntry(Mode, "Power mode", ActionSections.Power, StateKind.Choice,
            () => PowerCommands.CurrentMode() is { } mode ? StateValue.Of(NameOf(mode), DateTime.Now) : null, Looked,
            Choices: () => PowerActions.Modes.Select(m => m.Name).ToList(), Keywords: "efficiency balanced performance",
            Set: value => { if (PowerActions.Modes.FirstOrDefault(m => m.Name == value.Text) is { Name: not null } chosen) PowerCommands.SetMode(chosen.Mode); },
            SuggestedIcons: new[] { "gauge", "bolt", "battery" });
    }

    /// The status as a number the header's icon follows: 0 on battery, 1 plugged in, 2 plugged in and charging.
    private static int Number(BatteryStatus battery) => battery.Plugged ? battery.Charging ? 2 : 1 : 0;

    private static string StatusOf(BatteryStatus battery) => battery.Plugged ? battery.Charging ? ChargingText : PluggedInText : OnBatteryText;

    private static string? TimeLeft(BatteryStatus battery) => !battery.Plugged && battery.Left is { } left
        ? $"about {(left.TotalHours >= 1 ? $"{(int)left.TotalHours} h {left.Minutes} min" : $"{Math.Max(1, left.Minutes)} min")} left" : null;

    private static string NameOf(PowerMode mode) => PowerActions.Modes.First(m => m.Mode == mode).Name;
}
