namespace OmniControl.Core.Engine.Mouse;

/// <summary>Turns text such as "Ctrl+Shift+T" into Windows virtual-key codes. One place owns the key names.</summary>
public static class KeyChord
{
    private static readonly Dictionary<string, ushort> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = 0x11, ["Control"] = 0x11, ["Alt"] = 0x12, ["Shift"] = 0x10, ["Win"] = 0x5B,
        ["Enter"] = 0x0D, ["Esc"] = 0x1B, ["Escape"] = 0x1B, ["Space"] = 0x20, ["Tab"] = 0x09, ["Backspace"] = 0x08,
        ["Delete"] = 0x2E, ["Del"] = 0x2E, ["Insert"] = 0x2D, ["Home"] = 0x24, ["End"] = 0x23,
        ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["PrintScreen"] = 0x2C, ["Pause"] = 0x13, ["CapsLock"] = 0x14,
        ["VolumeMute"] = 0xAD, ["VolumeDown"] = 0xAE, ["VolumeUp"] = 0xAF,
        ["MediaNext"] = 0xB0, ["MediaPrevious"] = 0xB1, ["MediaStop"] = 0xB2, ["MediaPlayPause"] = 0xB3,
        ["BrowserBack"] = 0xA6, ["BrowserForward"] = 0xA7, ["BrowserRefresh"] = 0xA8, ["BrowserStop"] = 0xA9,
        ["BrowserSearch"] = 0xAA, ["BrowserFavorites"] = 0xAB, ["BrowserHome"] = 0xAC, ["LaunchMail"] = 0xB4,
        ["Plus"] = 0xBB, ["Comma"] = 0xBC, ["Minus"] = 0xBD, ["Period"] = 0xBE,
    };

    /// <summary>True when the text names at least one key and every key is known. Keys are separated by "+".</summary>
    public static bool TryParse(string text, out ushort[] keys)
    {
        var parsed = new List<ushort>();
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Names.TryGetValue(part, out var vk)) parsed.Add(vk);
            else if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0])) parsed.Add(char.ToUpperInvariant(part[0]));
            else if (part.Length is 2 or 3 && part[0] is 'F' or 'f' && int.TryParse(part[1..], out var f) && f is >= 1 and <= 24) parsed.Add((ushort)(0x6F + f));
            else { keys = Array.Empty<ushort>(); return false; }
        }
        keys = parsed.ToArray();
        return keys.Length > 0;
    }

    /// <summary>Navigation, Windows and browser keys need the "extended" flag or Windows reads them as the number pad.</summary>
    internal static bool IsExtended(ushort vk) => vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C || vk is >= 0xA6 and <= 0xB4;
}
