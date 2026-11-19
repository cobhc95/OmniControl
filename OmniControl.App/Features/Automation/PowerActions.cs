using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Automation;

/// The built-in actions of the Automation feature, registered once in the master list so they appear in the Shortcuts page, the mouse
/// and gesture pickers, search and rules: Sleep, Hibernate, Shut down, Restart, Sign out (through <see cref="PowerGuard"/>, each with the
/// countdown an input may set, and Force close where programs may be closed), Lock, the three power modes and Pause all rules. They are
/// listed under the Power section of the Choose-the-action window; Pause all rules and Lock are <see cref="HotkeyAction.NeverBlock"/>.
public static class PowerActions
{
    /// The Choose-the-action section (and the states' heading) of the power actions.
    private const string Mode = "Power mode", Control = "Control";
    private const string Words = "automation schedule";

    /// THE list of power modes: each one's action id, its name, and extra search words (the actions and the power mode state read it).
    public static readonly IReadOnlyList<(PowerMode Mode, string Id, string Name, string Keywords)> Modes = new[]
    {
        (PowerMode.BestEfficiency, "automation.power.efficiency", "Efficiency", "best efficiency battery saver eco"),
        (PowerMode.Balanced, "automation.power.balanced", "Balanced", "normal default"),
        (PowerMode.BestPerformance, "automation.power.performance", "Performance", "best performance fast turbo"),
    };

    public static IEnumerable<HotkeyAction> Make(PowerGuard guard, Action togglePause)
    {
        // The countdown and Force close an input chose arrive as run settings; a shortcut runs with the defaults.
        HotkeyAction Guarded(string id, string title, string keywords, Func<bool, bool> doIt, ActionSettings takes, bool basic = false, bool confirm = false, string? icon = null) =>
            new(id, title, ActionSections.Automation, $"{Words} {keywords}", ids => _ = guard.RequestAsync(title, () => doIt(false)), group: ActionSections.Power)
            {
                PickerSection = ActionSections.Power,
                Common = basic ? CommonGroups.Power : null,
                ConfirmRun = confirm,
                SuggestedIcons = icon is null ? null : new[] { icon },
                Accepts = takes,
                RunConfigured = settings => _ = guard.RequestAsync(title, () => doIt(settings.Force),
                    settings.CountdownSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null),
            };

        const ActionSettings Closes = ActionSettings.Countdown | ActionSettings.Force;
        yield return Make("automation.power.lock", "Lock", ActionSections.Power, ActionSections.Power, "lock screen workstation win+l", () => PowerCommands.Lock(), neverBlock: true, basic: true, icon: "lock");
        yield return Guarded("automation.power.signout", "Sign out", "log off logoff", PowerCommands.SignOut, Closes, basic: true, icon: "logout");
        yield return Guarded("automation.power.sleep", "Sleep", "suspend standby", _ => PowerCommands.Sleep(), ActionSettings.Countdown, basic: true, icon: "moon");
        yield return Guarded("automation.power.hibernate", "Hibernate", "suspend hibernation", _ => PowerCommands.Hibernate(), ActionSettings.Countdown);
        yield return Guarded("automation.power.restart", "Restart", "reboot", PowerCommands.Restart, Closes, basic: true, confirm: true, icon: "refresh");
        yield return Guarded("automation.power.shutdown", "Shut down", "turn off power off", PowerCommands.ShutDown, Closes, basic: true, confirm: true, icon: "power");
        foreach (var (mode, id, name, keywords) in Modes) yield return Make(id, $"{name} mode", Mode, ActionSections.Power, keywords, () => PowerCommands.SetMode(mode));
        yield return Make("automation.pause", "Pause all rules", Control, null, "resume stop disable automation emergency", togglePause, neverBlock: true);
    }

    private static HotkeyAction Make(string id, string title, string group, string? section, string keywords, Action run, bool neverBlock = false, bool basic = false, string? icon = null) =>
        new(id, title, ActionSections.Automation, $"{Words} {keywords}", _ => run(), group: group) { NeverBlock = neverBlock, PickerSection = section, Common = basic ? CommonGroups.Power : null, SuggestedIcons = icon is null ? null : new[] { icon } };
}
