namespace OmniControl.App.Display;

/// <summary>
/// One feature of a monitor in a preset, by its VCP code. A level keeps its 0-100 share of the monitor's range (the way every write is
/// made); a choice (input source, colour preset...) keeps the monitor's own raw value for it.
/// </summary>
public sealed class FeatureValue
{
    public int Code { get; set; }
    public int Value { get; set; }
}

/// <summary>What a preset keeps for one monitor: who it was (model name and Windows' id at the time) and its feature values.</summary>
public sealed class MonitorEntry
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public List<FeatureValue> Values { get; set; } = new();
}

/// <summary>A named set of hardware (DDC/CI) values, one <see cref="MonitorEntry"/> per monitor that was present when it was saved.</summary>
public sealed class MonitorPreset : INamedPreset
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public List<MonitorEntry> Monitors { get; set; } = new();
}
