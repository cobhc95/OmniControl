using OmniControl.Core.Interfaces;
using OmniControl.Core.Models;
using OmniControl.App.Hotkeys;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// The page's entries in the master list of actions: ONE action per control (not per display), each with the "all displays / pick
/// displays" chooser of the Shortcuts page. A control is applied to every chosen display that has it; the others are skipped silently.
/// The same functions are what the page's cards use, through the displays' own items. The displays that come and go are only listed by
/// the chooser (<see cref="RefreshActionDevices"/>); nothing is registered or removed when a display appears or goes. The list of
/// controls is <see cref="DisplayControls"/>: adding one there adds its card and actions here.
/// </summary>
public sealed partial class DisplaysPageViewModel
{
    private const string OrientationSection = "Orientation";

    /// <summary>The orientations the hotkeys turn to, with the key suggested for each (the arrows point where the top of the picture goes).</summary>
    private static readonly (DisplayOrientation Orientation, string Key)[] RotationKeys =
    {
        (DisplayOrientation.Landscape, "Ctrl+Alt+Up"),
        (DisplayOrientation.Portrait, "Ctrl+Alt+Right"),
        (DisplayOrientation.LandscapeFlipped, "Ctrl+Alt+Down"),
        (DisplayOrientation.PortraitFlipped, "Ctrl+Alt+Left"),
    };

    private readonly List<HotkeyAction> _scopedActions = new();

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
        actions.Add(DisplayActions.Once("displays.power.off", "Turn screen off", "power off sleep standby turn screen off", Title, hardware, VcpSections.PowerMode,
            ids => EachHardware(ids, h => h.TurnOff()), HardwareDevices));

        foreach (var (orientation, suggested) in RotationKeys)
        {
            var captured = orientation;
            actions.Add(DisplayActions.Once($"displays.rotate.{captured.ToString().ToLowerInvariant()}", $"Rotate to {DisplayOrientationCard.Title(captured)}",
                "rotate rotation orientation landscape portrait flipped turn screen", Title, hardware, OrientationSection,
                ids => EachHardware(ids, h => h.Rotate(captured)), HardwareDevices, suggested, shortName: "Rotate"));
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

        _scopedActions.AddRange(actions);
        actions.AddRange(_global.Actions);
        actions.AddRange(_warm.Actions);
        _hotkeys.Register(actions);
    }

    private IReadOnlyList<HotkeyDevice> HardwareDevices() => _hardware.Select(h => new HotkeyDevice(h.Id, h.Name)).ToList();

    private IReadOnlyList<HotkeyDevice> SoftwareDevices() => _softwareItems.Select(s => new HotkeyDevice(s.Id, s.Name)).ToList();

    /// <summary>The scope choosers list the displays that are attached now.</summary>
    private void RefreshActionDevices()
    {
        foreach (var action in _scopedActions) action.RefreshDevices();
    }

    /// <summary>Runs <paramref name="act"/> on the chosen hardware displays (all when <paramref name="ids"/> is null).</summary>
    private void EachHardware(IReadOnlyList<string>? ids, Action<HardwareDisplayItem> act)
    {
        foreach (var display in _hardware.Where(h => ids is null || ids.Contains(h.Id)).ToList()) act(display);
    }

    /// <summary>
    /// Steps the level on the chosen hardware displays that have it; the pop-up shows the first display that reports one. The displays
    /// are chosen here on the UI thread (their cards are read), the monitors are written on a worker thread.
    /// </summary>
    private void StepHardware(HardwareLevel level, IReadOnlyList<string>? ids, int delta)
    {
        var targets = _hardware.Where(h => (ids is null || ids.Contains(h.Id)) && h.Supports(level)).ToList();
        Task.Run(() =>
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

    /// <summary>Steps the digital control on the chosen displays; the pop-up shows the level of the first one.</summary>
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
