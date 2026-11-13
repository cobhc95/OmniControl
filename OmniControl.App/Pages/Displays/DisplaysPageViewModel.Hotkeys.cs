using OmniControl.App.Hotkeys;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// The page's entries in the master list of actions: ONE action per control (not per display), each with the "all displays / pick
/// displays" chooser of the Shortcuts page. A control is applied to every chosen display that has it; the others are skipped silently.
/// The same functions are what the page's cards use, through the displays' own items. The displays that come and go are only listed by
/// the chooser (<see cref="RefreshActionDevices"/>); nothing is registered or removed when a display appears or goes.
/// </summary>
public sealed partial class DisplaysPageViewModel
{
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

        // One generic "next" for presets and inputs: the choices differ from monitor to monitor, so a named input would only fit some.
        actions.Add(DisplayActions.Once("displays.preset.next", "Next colour preset", "colour color preset temperature srgb", Title, hardware,
            ids => EachHardware(ids, h => h.NextPreset()), HardwareDevices));
        actions.Add(DisplayActions.Once("displays.input.next", "Next input source", "input source switch hdmi displayport dvi vga kvm", Title, hardware,
            ids => EachHardware(ids, h => h.NextInput()), HardwareDevices));
        actions.Add(DisplayActions.Once("displays.power.off", "Turn screen off", "power off sleep standby turn screen off", Title, hardware,
            ids => EachHardware(ids, h => h.TurnOff()), HardwareDevices));

        foreach (var spec in new[] { DisplayControls.SoftwareBrightness, DisplayControls.SoftwareContrast, DisplayControls.SoftwareWarmth })
        {
            var captured = spec;
            actions.AddRange(DisplayActions.Level(spec, DisplayControls.SoftwareKey(spec), Title, software,
                (ids, delta) => StepSoftware(captured, ids, delta), SoftwareDevices));
        }

        _scopedActions.AddRange(actions);
        actions.AddRange(_global.Actions);
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
