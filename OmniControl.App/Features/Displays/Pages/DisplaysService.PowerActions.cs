using StateTarget = OmniControl.App.Features.Hotkeys.StateTarget;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Displays.Pages;

// The On / Off / Toggle families of screen power: one for the screens picked in the action's scope, one for all screens. The Off actions keep
// their old ids ("displays.power.off", "displays.screens.off"). The set-to-state check of a scoped action is made per display here (the
// state is one per display, an action's scope is many), so a screen already in the asked state is left alone.
public sealed partial class DisplaysService
{
    private const string ScreenPowerId = "displays.power", AllScreensId = "displays.screens";

    private static readonly OnOffFamily ScreenPowerFamily = new(ScreenPowerId, "Screen power", string.Empty);
    private static readonly OnOffFamily AllScreensFamily = new(AllScreensId, "All screens", string.Empty);

    private static readonly string[] ScreenIcons = { "device-desktop", "power", "moon" };

    private IEnumerable<HotkeyAction> ScreenPowerActions(string hardware)
    {
        const string words = "power off on sleep standby wake turn screen";
        yield return DisplayActions.Once(ScreenPowerId, "Toggle screen power", words, Title, hardware, VcpSections.PowerMode,
            ids => EachHardware(ids, h => h.SetScreenOn(h.ScreenOn != true)), HardwareDevices, suggestedIcons: ScreenIcons, family: ScreenPowerFamily.Family, familyRole: OnOffFamily.ToggleRole);
        foreach (var on in new[] { true, false })
        {
            var wanted = on;
            yield return DisplayActions.Once(OnOffFamily.SiblingId(ScreenPowerId, on), on ? "Turn screen on" : "Turn screen off", words, Title, hardware, VcpSections.PowerMode,
                ids => EachHardware(ids, h => { if (h.HasScreenPower && h.ScreenOn != wanted) h.SetScreenOn(wanted); }), HardwareDevices, suggestedIcons: ScreenIcons,
                family: ScreenPowerFamily.Family, familyRole: on ? StateTarget.OnRole : StateTarget.OffRole);
        }
    }

    private IEnumerable<HotkeyAction> AllScreensActions(string hardware)
    {
        const string words = "turn off on all screens monitors black sleep standby power wake";
        yield return DisplayActions.Once(AllScreensId, "Toggle displays", words, Title, hardware, VcpSections.PowerMode,
            _ => { if (_systemScreens.IsOff || _hardware.Any(h => h.HasScreenPower && h.ScreenOn == false)) WakeScreens(); else TurnOffDisplays(); },
            devices: null, shortName: "Screens", suggestedIcons: ScreenIcons, family: AllScreensFamily.Family, familyRole: OnOffFamily.ToggleRole);
        yield return DisplayActions.Once(OnOffFamily.SiblingId(AllScreensId, true), "Turn on displays", words, Title, hardware, VcpSections.PowerMode,
            _ => WakeScreens(), devices: null, shortName: "Screens on", suggestedIcons: ScreenIcons, family: AllScreensFamily.Family, familyRole: StateTarget.OnRole,
            manageSettings: new[] { ActionOption.Heading("displayson.note", "Wakes the screens that take DDC power commands", "Screens Windows put to sleep wake on any mouse movement or key press.") });
        yield return DisplayActions.Once(DisplaysOffId, "Turn off displays", words, Title, hardware, VcpSections.PowerMode,
            _ => TurnOffDisplays(), devices: null, shortName: "Screens off", suggestedIcons: ScreenIcons, screensOff: true, common: CommonGroups.Screens,
            family: AllScreensFamily.Family, familyRole: StateTarget.OffRole,
            manageSettings: new[] { ActionOption.Heading("displaysoff.note", "Turns every screen off at once", "Any mouse movement or key press wakes them again; that is how Windows works.") });
    }

    private void WakeScreens()
    {
        if (_systemScreens.IsOff) _systemScreens.WakeUp(); // monitors get no signal to answer DDC while Windows has the screens off
        else EachHardware(null, h => { if (h.HasScreenPower && h.ScreenOn != true) h.SetScreenOn(true); });
    }
}
