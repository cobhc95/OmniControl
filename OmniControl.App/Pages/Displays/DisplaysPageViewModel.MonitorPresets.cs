namespace OmniControl.App.Pages.Displays;

/// <summary>The Hardware group's monitor presets on the page: the header drop-down and its cogwheel (the shared preset manager, at the Hardware card).</summary>
public sealed partial class DisplaysPageViewModel
{
    /// <summary>The monitor preset drop-down on the Hardware group's title line.</summary>
    public ChoiceItem MonitorPresetChoice => _monitorPresets.Choice;

    /// <summary>The cogwheel beside the monitor preset drop-down: the preset manager at the Hardware card.</summary>
    public void ManageMonitorPresets() => _openPresets(Title, HardwarePresetsId);

    /// <summary>The displays a monitor preset reads and writes (the laptop's panel is one, and takes no part).</summary>
    private IReadOnlyList<IMonitorPresetTarget> PresetTargets() => _hardware.ToList<IMonitorPresetTarget>();
}
