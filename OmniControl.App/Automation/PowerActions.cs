using OmniControl.App.Hotkeys;
using OmniControl.Core.Engine;

namespace OmniControl.App.Automation;

/// <summary>
/// The built-in actions of the Automation feature, registered once in the master list so they appear in the Shortcuts page, the mouse
/// and gesture pickers, search and rules: Sleep, Hibernate, Shut down, Restart, Sign out (through <see cref="PowerGuard"/>, each with the
/// countdown an input may set, and Force close where programs may be closed), Lock, the three power modes and Pause all rules. They are
/// listed under the Power section of the Choose-the-action window; Pause all rules and Lock are <see cref="HotkeyAction.NeverBlock"/>.
/// </summary>
public static class PowerActions
{
    private const string Power = "Power", Mode = "Power mode", Control = "Control";
    private const string Words = "automation schedule";

    public static IEnumerable<HotkeyAction> Make(PowerGuard guard, Action togglePause)
    {
        // The countdown and Force close an input chose arrive as run settings; a shortcut runs with the defaults.
        HotkeyAction Guarded(string id, string title, string keywords, Func<bool, bool> doIt, ActionSettings takes) =>
            new(id, title, AutomationService.Category, $"{Words} {keywords}", ids => _ = guard.RequestAsync(title, () => doIt(false)), group: Power)
            {
                PickerSection = Power,
                Accepts = takes,
                RunConfigured = settings => _ = guard.RequestAsync(title, () => doIt(settings.Force),
                    settings.CountdownSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null),
            };

        const ActionSettings Closes = ActionSettings.Countdown | ActionSettings.Force;
        yield return Guarded("automation.power.sleep", "Sleep", "suspend standby", _ => PowerCommands.Sleep(), ActionSettings.Countdown);
        yield return Guarded("automation.power.hibernate", "Hibernate", "suspend hibernation", _ => PowerCommands.Hibernate(), ActionSettings.Countdown);
        yield return Guarded("automation.power.shutdown", "Shut down", "turn off power off", PowerCommands.ShutDown, Closes);
        yield return Guarded("automation.power.restart", "Restart", "reboot", PowerCommands.Restart, Closes);
        yield return Guarded("automation.power.signout", "Sign out", "log off logoff", PowerCommands.SignOut, Closes);
        yield return Make("automation.power.lock", "Lock", Power, Power, "lock screen workstation win+l", () => PowerCommands.Lock(), neverBlock: true);
        yield return Make("automation.power.efficiency", "Efficiency mode", Mode, Power, "best efficiency battery saver eco", () => PowerCommands.SetMode(PowerMode.BestEfficiency));
        yield return Make("automation.power.balanced", "Balanced mode", Mode, Power, "normal default", () => PowerCommands.SetMode(PowerMode.Balanced));
        yield return Make("automation.power.performance", "Performance mode", Mode, Power, "best performance fast turbo", () => PowerCommands.SetMode(PowerMode.BestPerformance));
        yield return Make("automation.pause", "Pause all rules", Control, null, "resume stop disable automation emergency", togglePause, neverBlock: true);
    }

    private static HotkeyAction Make(string id, string title, string group, string? section, string keywords, Action run, bool neverBlock = false) =>
        new(id, title, AutomationService.Category, $"{Words} {keywords}", _ => run(), group: group) { NeverBlock = neverBlock, PickerSection = section };
}
