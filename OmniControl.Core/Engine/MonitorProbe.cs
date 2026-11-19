namespace OmniControl.Core.Engine;

/// Finds what one monitor supports by reading the codes of <see cref="VcpFeatures"/> (a monitor answers the codes it implements and
/// refuses the rest). The capabilities string, which is slow and which many monitors answer badly, is asked for once, after the
/// monitor has answered a first code: its list narrows the codes read (a monitor that gave none is asked about every code) and gives
/// the values of the inputs, colour presets and other choices.
internal static class MonitorProbe
{
    /// A monitor with DDC/CI off refuses everything; after this many refusals in a row from the start the probe stops waiting for more.
    private const int GiveUpAfterRefusals = 3;

    public static MonitorCapabilities Run(Func<byte, VcpValue?> read, Func<string?> readCapabilities)
    {
        var features = new Dictionary<byte, VcpValue>();
        var refusals = 0;
        foreach (var code in VcpFeatures.Core)
        {
            if (read(code) is { } value) features[code] = value;
            else if (features.Count == 0 && ++refusals >= GiveUpAfterRefusals) return MonitorCapabilities.None;
        }
        if (features.Count == 0) return MonitorCapabilities.None;

        var listed = readCapabilities() is { } text ? CapabilitiesParser.Vcp(text) : new Dictionary<byte, List<uint>>();
        foreach (var feature in VcpFeatures.All.Where(f => !VcpFeatures.Core.Contains(f.Code)))
        {
            if (listed.Count > 0 && !listed.ContainsKey(feature.Code)) continue;
            if (read(feature.Code) is { } value) features[feature.Code] = value;
        }

        var choices = new Dictionary<byte, IReadOnlyList<VcpChoice>>();
        foreach (var feature in VcpFeatures.Choices.Where(f => features.ContainsKey(f.Code)))
            choices[feature.Code] = Choices(features, feature.Code, listed);
        return new MonitorCapabilities(features, choices);
    }

    /// The values the capabilities string allows for <paramref name="code"/>, named; empty when the monitor did not list any.
    private static IReadOnlyList<VcpChoice> Choices(Dictionary<byte, VcpValue> features, byte code, Dictionary<byte, List<uint>> listed)
    {
        if (!listed.TryGetValue(code, out var values) || values.Count == 0) return Array.Empty<VcpChoice>();

        // The value the monitor is on now is always a choice, even if its string forgot to list it.
        if (!values.Contains(features[code].Current)) values = values.Append(features[code].Current).ToList();
        return values.Select(v => new VcpChoice(v, VcpNames.Of(code, v))).ToList();
    }
}
