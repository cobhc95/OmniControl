using OmniControl.App.Features.States;
using OmniControl.Core.Interfaces;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Displays.Pages;

/// The page's entries in the master list of actions: ONE action per control (not per display), each with the "all displays / pick
/// displays" chooser of the Shortcuts page. A control is applied to every chosen display that has it; the others are skipped silently.
/// The same functions are what the page's cards use, through the displays' own items. The displays that come and go are only listed by
/// the chooser (<see cref="RefreshActionDevices"/>); nothing is registered or removed when a display appears or goes. The list of
/// controls is <see cref="DisplayControls"/>: adding one there adds its card and actions here.
public sealed partial class DisplaysPageViewModel
{
    private const string OrientationSection = "Orientation";

    /// The id of the orientation hotkeys' suggested-keys set (<see cref="HotkeyAction.SuggestedKeySet"/>): the Orientation card's switch turns them on and off together.
    private const string RotationKeySet = "displays.rotate";

    /// The orientations the hotkeys turn to, with the key suggested for each (as NVIDIA's panel: Up and Down for landscape, Left and Right for portrait).
    private static readonly (DisplayOrientation Orientation, string Key)[] RotationKeys =
    {
        (DisplayOrientation.Landscape, "Ctrl+Alt+Up"),
        (DisplayOrientation.Portrait, "Ctrl+Alt+Right"),
        (DisplayOrientation.LandscapeFlipped, "Ctrl+Alt+Down"),
        (DisplayOrientation.PortraitFlipped, "Ctrl+Alt+Left"),
    };

    private const string DisplaysOffId = "displays.screens.off";

    private readonly List<HotkeyAction> _scopedActions = new();

    /// Waits a moment (so the input that started it is over), then asks Windows to switch every screen off.
    private static void TurnOffDisplays() => Guard.OffUi("Displays: turning the displays off", async () =>
    {
        await Task.Delay(Timing.DisplaysOffGrace);
        Guard.Run("Turning the displays off", () => PowerCommands.TurnOffDisplays());
    });

    private void RegisterActions()
    {
        var actions = new List<HotkeyAction>();
        var hardware = DisplayControls.HardwareGroup;
        var software = DisplayControls.SoftwareGroup;

        foreach (var level in DisplayControls.Hardware)
        {
            var captured = level;
            actions.AddRange(DisplayActions.Level(level.Spec, level.Spec.Key, Title, hardware,
                (ids, delta) => StepHardware(captured, ids, delta), HardwareDevices));
        }

        // One generic "next" per choice: the values differ from monitor to monitor, so a named input would only fit some.
        foreach (var choice in DisplayControls.HardwareChoices)
        {
            var captured = choice;
            actions.Add(DisplayActions.Once($"displays.{choice.Feature.Key}.next", choice.ActionTitle, choice.Feature.Keywords, Title, hardware, choice.Feature.Section,
                ids => EachHardware(ids, h => h.NextChoice(captured)), HardwareDevices));
        }
        actions.AddRange(ScreenPowerActions(hardware));
        actions.AddRange(AllScreensActions(hardware));

        foreach (var (orientation, suggested) in RotationKeys)
        {
            var captured = orientation;
            actions.Add(DisplayActions.Once($"displays.rotate.{captured.ToString().ToLowerInvariant()}", $"Rotate to {DisplayOrientationCard.Title(captured)}",
                "rotate rotation orientation landscape portrait flipped turn screen", Title, hardware, OrientationSection,
                ids => EachHardware(ids, h => h.Rotate(captured)), HardwareDevices, suggested, shortName: DisplayOrientationCard.ShortTitle(captured), icon: DisplayOrientationCard.Icon(captured), suggestedKeySet: RotationKeySet));
        }

        actions.Add(DisplayActions.Once("displays.hw.preset.next", "Next monitor preset", "hardware ddc preset saved profile monitors settings", Title, hardware, VcpSections.Presets,
            _ => _monitorBook.Next(), devices: null));

        foreach (var level in DisplayControls.Software)
        {
            var spec = level.Spec;
            actions.AddRange(DisplayActions.Level(spec, DisplayControls.SoftwareKey(spec), Title, software,
                (ids, delta) => StepSoftware(spec, ids, delta), SoftwareDevices));
        }
        actions.Add(DisplayActions.Once("displays.sw.preset.next", "Next display preset", "digital software preset picture saved profile", Title, software, VcpSections.Presets,
            _ => NextPreset(), devices: null));

        foreach (var action in actions.Where(a => a.HasDevices))
            action.HardwareWatch = action.Group == hardware ? HardwareWatchId : SoftwareWatchId; // a picked display that is missing is looked for once before the action is refused
        _scopedActions.AddRange(actions);
        actions.AddRange(_global.Actions);
        actions.AddRange(_warm.Actions);
        _hotkeys.Register(actions);
    }

    private IReadOnlyList<HotkeyDevice> HardwareDevices() => _hardware.Select(h => new HotkeyDevice(h.Id, h.Name)).ToList();

    private IReadOnlyList<HotkeyDevice> SoftwareDevices() => _softwareItems.Select(s => new HotkeyDevice(s.Id, s.Name)).ToList();

    /// The scope choosers list the displays that are attached now.
    private void RefreshActionDevices()
    {
        foreach (var action in _scopedActions) action.RefreshDevices();
    }

    /// Runs <paramref name="act"/> on the chosen hardware displays (all when <paramref name="ids"/> is null).
    private void EachHardware(IReadOnlyList<string>? ids, Action<HardwareDisplayItem> act)
    {
        foreach (var display in _hardware.Where(h => ids is null || ids.Contains(h.Id)).ToList()) act(display);
    }

    /// Steps the level on the chosen hardware displays that have it; the pop-up shows the first display that reports one. The displays
    /// are chosen here on the UI thread (their cards are read), the monitors are written on a worker thread.
    private void StepHardware(HardwareLevel level, IReadOnlyList<string>? ids, int delta)
    {
        var targets = _hardware.Where(h => (ids is null || ids.Contains(h.Id)) && h.Supports(level)).ToList();
        Guard.OffUi("Displays: stepping a level", () =>
        {
            var shown = false;
            void Show(int value) => _osd.Show(level.Spec.Osd, value);
            foreach (var display in targets)
            {
                if (display.Step(level, delta, shown ? null : Show) is not { } now || shown) continue;
                Show(now);
                shown = true;
            }
        });
    }

    /// Steps the digital control on the chosen displays; the pop-up shows the level of the first one.
    private void StepSoftware(LevelSpec spec, IReadOnlyList<string>? ids, int delta)
    {
        int? first = null;
        foreach (var display in _softwareItems.Where(s => ids is null || ids.Contains(s.Id)).ToList())
        {
            var now = display.Step(spec, delta);
            first ??= now;
        }
        if (first is { } value) _osd.Show(spec.Osd, value);
    }
}
