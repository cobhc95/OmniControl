namespace OmniControl.Core.Models;

/// <summary>Names for the values of the table-type VCP codes (input source, colour preset), from the MCCS standard.</summary>
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

    public static string Input(uint value) => Inputs.TryGetValue(value, out var name) ? name : $"Input 0x{value:X2}";

    public static string Preset(uint value) => Presets.TryGetValue(value, out var name) ? name : $"Preset 0x{value:X2}";
}
