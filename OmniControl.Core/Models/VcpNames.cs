namespace OmniControl.Core.Models;

/// Names for the values of the table-type VCP codes (input source, colour preset), from the MCCS standard.
public static class VcpNames
{
    private static readonly Dictionary<uint, string> Inputs = new()
    {
        [0x01] = "VGA 1", [0x02] = "VGA 2", [0x03] = "DVI 1", [0x04] = "DVI 2",
        [0x05] = "Composite 1", [0x06] = "Composite 2", [0x07] = "S-Video 1", [0x08] = "S-Video 2",
        [0x09] = "Tuner 1", [0x0A] = "Tuner 2", [0x0B] = "Tuner 3",
        [0x0C] = "Component 1", [0x0D] = "Component 2", [0x0E] = "Component 3",
        [0x0F] = "DisplayPort 1", [0x10] = "DisplayPort 2", [0x11] = "HDMI 1", [0x12] = "HDMI 2",
    };

    private static readonly Dictionary<uint, string> Presets = new()
    {
        [0x01] = "sRGB", [0x02] = "Native", [0x03] = "4000 K", [0x04] = "5000 K", [0x05] = "6500 K",
        [0x06] = "7500 K", [0x07] = "8200 K", [0x08] = "9300 K", [0x09] = "10000 K", [0x0A] = "11500 K",
        [0x0B] = "User 1", [0x0C] = "User 2", [0x0D] = "User 3",
    };

    // Display mode (0xDC) and speaker mute (0x8D), MCCS 2.2a; a monitor's own list decides which of them it offers.
    private static readonly Dictionary<uint, string> Modes = new()
    {
        [0x00] = "Standard", [0x01] = "Productivity", [0x02] = "Mixed", [0x03] = "Movie", [0x04] = "User defined",
        [0x05] = "Games", [0x06] = "Sports", [0x07] = "Professional", [0x08] = "Standard (low power)",
    };

    private static readonly Dictionary<uint, string> Mutes = new() { [0x01] = "Muted", [0x02] = "Not muted" };

    /// The name of one value of a choice feature (<see cref="VcpKind.Choice"/>), by its VCP code.
    public static string Of(byte code, uint value) => code switch
    {
        VcpCode.InputSource => Input(value),
        VcpCode.ColorPreset => Preset(value),
        0xDC => Modes.TryGetValue(value, out var mode) ? mode : $"Mode 0x{value:X2}",
        0x8D => Mutes.TryGetValue(value, out var mute) ? mute : $"Value 0x{value:X2}",
        _ => $"Value 0x{value:X2}",
    };

    /// The names a choice feature can show, for search (empty for a feature whose values are not named here).
    public static IReadOnlyList<string> NamesOf(byte code) => code switch
    {
        VcpCode.InputSource => Inputs.Values.ToList(),
        VcpCode.ColorPreset => Presets.Values.ToList(),
        0xDC => Modes.Values.ToList(),
        0x8D => Mutes.Values.ToList(),
        _ => Array.Empty<string>(),
    };

    public static string Input(uint value) => Inputs.TryGetValue(value, out var name) ? name : $"Input 0x{value:X2}";

    public static string Preset(uint value) => Presets.TryGetValue(value, out var name) ? name : $"Preset 0x{value:X2}";
}
