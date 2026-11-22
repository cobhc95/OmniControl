namespace OmniControl.App.Features.Displays.Pages;

/// The Hardware group's monitor presets: the cogwheel of the page's drop-down (the shared preset manager, at the Hardware card) and the displays they read.
public sealed partial class DisplaysService
{
    /// The cogwheel beside the monitor preset drop-down: the preset manager at the Hardware card.
    internal void ManageMonitorPresets() => _openPresets(Title, HardwarePresetsId);

    /// The displays a monitor preset reads and writes (the laptop's panel is one, and takes no part).
    private IReadOnlyList<IMonitorPresetTarget> PresetTargets() => _hardware.ToList<IMonitorPresetTarget>();
}
