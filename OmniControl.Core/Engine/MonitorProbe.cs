using OmniControl.Core.Models;

namespace OmniControl.Core.Engine;

/// <summary>
/// Finds what one monitor supports by reading each code OmniControl uses (a monitor answers the codes it implements and refuses the
/// rest). The capabilities string, which is slow and which many monitors answer badly, is only asked for to list the values of the
/// inputs and colour presets, and only when the monitor has them.
/// </summary>
internal static class MonitorProbe
{
    private static readonly byte[] Codes =
    {
        VcpCode.Brightness, VcpCode.Contrast, VcpCode.ColorPreset, VcpCode.RedGain, VcpCode.GreenGain, VcpCode.BlueGain,
        VcpCode.Saturation, VcpCode.Sharpness, VcpCode.InputSource, VcpCode.SpeakerVolume, VcpCode.PowerMode,
    };

    /// <summary>A monitor with DDC/CI off refuses everything; after this many refusals in a row from the start the probe stops waiting for more.</summary>
    private const int GiveUpAfterRefusals = 3;

    public static MonitorCapabilities Run(Func<byte, VcpValue?> read, Func<string?> readCapabilities)
    {
        var features = new Dictionary<byte, VcpValue>();
        var refusals = 0;
        foreach (var code in Codes)
        {
            if (read(code) is { } value) features[code] = value;
            else if (features.Count == 0 && ++refusals >= GiveUpAfterRefusals) return MonitorCapabilities.None;
        }

        // Read at most once, however many codes need it.
        var listed = new Lazy<Dictionary<byte, List<uint>>>(() => readCapabilities() is { } text ? CapabilitiesParser.Vcp(text) : new());
        var inputs = features.ContainsKey(VcpCode.InputSource) ? Choices(features, VcpCode.InputSource, listed, VcpNames.Input) : Array.Empty<VcpChoice>();
        var presets = features.ContainsKey(VcpCode.ColorPreset) ? Choices(features, VcpCode.ColorPreset, listed, VcpNames.Preset) : Array.Empty<VcpChoice>();
        return new MonitorCapabilities(features, inputs, presets);
    }

    /// <summary>The values the capabilities string allows for <paramref name="code"/>, named; empty when the monitor did not list any.</summary>
    private static IReadOnlyList<VcpChoice> Choices(Dictionary<byte, VcpValue> features, byte code, Lazy<Dictionary<byte, List<uint>>> listed, Func<uint, string> name)
    {
        if (!listed.Value.TryGetValue(code, out var values) || values.Count == 0) return Array.Empty<VcpChoice>();

        // The value the monitor is on now is always a choice, even if its string forgot to list it.
        if (!values.Contains(features[code].Current)) values = values.Append(features[code].Current).ToList();
        return values.Select(v => new VcpChoice(v, name(v))).ToList();
    }
}
