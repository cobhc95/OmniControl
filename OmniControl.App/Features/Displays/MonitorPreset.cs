using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Displays;

/// One feature of a monitor in a preset, by its VCP code. A level keeps its 0-100 share of the monitor's range (the way every write is
/// made); a choice (input source, colour preset...) keeps the monitor's own raw value for it.
public sealed class FeatureValue
{
    public int Code { get; set; }
    public int Value { get; set; }
}

/// What a preset keeps for one monitor: who it was (model name and Windows' id at the time) and its feature values.
public sealed class MonitorEntry
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public List<FeatureValue> Values { get; set; } = new();
}

/// A named set of hardware (DDC/CI) values, one <see cref="MonitorEntry"/> per monitor that was present when it was saved.
public sealed class MonitorPreset : INamedPreset
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public List<MonitorEntry> Monitors { get; set; } = new();
}
