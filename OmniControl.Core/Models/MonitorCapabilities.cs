namespace OmniControl.Core.Models;

/// <summary>One value of a table-type VCP code (an input, a colour preset) with its name.</summary>
public sealed record VcpChoice(uint Value, string Name);

/// <summary>
/// What one monitor answers over DDC/CI, found once by probing (reading each code we know) and kept for as long as the monitor object lives.
/// A code is "supported" when the monitor answered reading it; the lists are the values the monitor's capabilities string allows.
/// </summary>
public sealed class MonitorCapabilities
{
    public static MonitorCapabilities None { get; } = new(new Dictionary<byte, VcpValue>(), Array.Empty<VcpChoice>(), Array.Empty<VcpChoice>());

    public MonitorCapabilities(IReadOnlyDictionary<byte, VcpValue> features, IReadOnlyList<VcpChoice> inputs, IReadOnlyList<VcpChoice> presets)
    {
        Features = features;
        Inputs = inputs;
        Presets = presets;
    }

    public IReadOnlyDictionary<byte, VcpValue> Features { get; }
    public IReadOnlyList<VcpChoice> Inputs { get; }
    public IReadOnlyList<VcpChoice> Presets { get; }

    /// <summary>True when the monitor answered at least one code (DDC/CI is on and working).</summary>
    public bool Answers => Features.Count > 0;

    public bool Has(byte code) => Features.ContainsKey(code);

    /// <summary>The code's value when it is a continuous control with a known maximum; null otherwise.</summary>
    public VcpValue? Level(byte code) => Features.TryGetValue(code, out var v) && v.Maximum > 0 ? v : null;
}
