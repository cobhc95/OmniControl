namespace OmniControl.Core.Models;

/// <summary>One value of a table-type VCP code (an input, a colour preset) with its name.</summary>
public sealed record VcpChoice(uint Value, string Name);

/// <summary>
/// What one monitor answers over DDC/CI, found once by probing (reading each code we know) and kept for as long as the monitor object lives.
/// A code is "supported" when the monitor answered reading it; the choice lists are the values the monitor's capabilities string allows.
/// </summary>
public sealed class MonitorCapabilities
{
    public static MonitorCapabilities None { get; } = new(new Dictionary<byte, VcpValue>(), new Dictionary<byte, IReadOnlyList<VcpChoice>>());

    public MonitorCapabilities(IReadOnlyDictionary<byte, VcpValue> features, IReadOnlyDictionary<byte, IReadOnlyList<VcpChoice>> choices)
    {
        Features = features;
        Choices = choices;
    }

    public IReadOnlyDictionary<byte, VcpValue> Features { get; }

    /// <summary>For each choice feature the monitor has and lists values for, the values it accepts.</summary>
    public IReadOnlyDictionary<byte, IReadOnlyList<VcpChoice>> Choices { get; }

    /// <summary>True when the monitor answered at least one code (DDC/CI is on and working).</summary>
    public bool Answers => Features.Count > 0;

    public bool Has(byte code) => Features.ContainsKey(code);

    /// <summary>The code's value when it is a continuous control with a known maximum; null otherwise.</summary>
    public VcpValue? Level(byte code) => Features.TryGetValue(code, out var v) && v.Maximum > 0 ? v : null;

    /// <summary>The values a choice feature accepts; empty when the monitor does not have it or did not list any.</summary>
    public IReadOnlyList<VcpChoice> ChoicesOf(byte code) => Choices.TryGetValue(code, out var list) ? list : Array.Empty<VcpChoice>();
}
