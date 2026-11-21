namespace OmniControl.App.Features.Displays.Pages;

/// The Hardware group's monitor presets on the page: the header drop-down and its cogwheel (the shared preset manager, at the Hardware card).
public sealed partial class DisplaysService
{
    /// The monitor preset drop-down on the Hardware group's title line.
    public ChoiceItem MonitorPresetChoice => _monitorPresets.Choice;

    /// The cogwheel beside the monitor preset drop-down: the preset manager at the Hardware card.
    public void ManageMonitorPresets() => _openPresets(Title, HardwarePresetsId);

    /// The displays a monitor preset reads and writes (the laptop's panel is one, and takes no part).
    private IReadOnlyList<IMonitorPresetTarget> PresetTargets() => _hardware.ToList<IMonitorPresetTarget>();
}
