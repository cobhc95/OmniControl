namespace OmniControl.Core.Engine.Mouse;

/// Turns text such as "Ctrl+Shift+T" into Windows virtual-key codes. One place owns the key names.
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

    /// True when the text names at least one key and every key is known. Keys are separated by "+".
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

    /// The most shortcuts one "send keys" text may hold in a row.
    public const int MaxSequence = 8;

    /// The shortcuts of a text such as "Ctrl+K, Ctrl+C" or "Ctrl+K Ctrl+C", in order: separated by commas or spaces ("Ctrl + C" is still one).
    public static IReadOnlyList<string> Steps(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s*\+\s*", "+").Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    /// True when the text holds one to <see cref="MaxSequence"/> shortcuts (<see cref="Steps"/>) and every key is known.
    public static bool TryParseSequence(string text, out IReadOnlyList<ushort[]> chords)
    {
        var parsed = new List<ushort[]>();
        foreach (var step in Steps(text))
        {
            if (!TryParse(step, out var keys)) { chords = Array.Empty<ushort[]>(); return false; }
            parsed.Add(keys);
        }
        chords = parsed;
        return parsed.Count is > 0 and <= MaxSequence;
    }

    /// True when the shortcut can open one of Windows' own panels: it holds the Windows key (Win+S, Win+N, Win+Tab...) or is Alt+Tab
    /// (the window switcher, shown in the same kind of window as Task view); for a sequence, when any of its shortcuts can.
    public static bool OpensWindowsPanel(string text) =>
        Steps(text).Any(step => TryParse(step, out var keys) && (HasWinKey(keys) || keys.Contains(Names["Alt"]) && keys.Contains(Names["Tab"])));

    /// True when the keys hold the Windows key.
    public static bool HasWinKey(IReadOnlyList<ushort> keys) => keys.Contains(Names["Win"]);

    /// True when both name the same keys, in any order ("Shift+Alt+Tab" is "Alt+Shift+Tab").
    public static bool SameKeys(IReadOnlyList<ushort> a, IReadOnlyList<ushort> b) => a.Order().SequenceEqual(b.Order());

    /// Navigation, Windows and browser keys need the "extended" flag or Windows reads them as the number pad.
    internal static bool IsExtended(ushort vk) => vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C || vk is >= 0xA6 and <= 0xB4;

    // The standard keyboard's (E0-prefixed) scan codes of the browser, volume, media and mail keys, VK_BROWSER_BACK (0xA6) to VK_LAUNCH_MAIL (0xB4),
    // for a layout that maps them to none: Remote Desktop and virtual machines pass on only the scan code.
    private static readonly byte[] MediaScans = { 0x6A, 0x69, 0x67, 0x68, 0x65, 0x66, 0x32, 0x20, 0x2E, 0x30, 0x19, 0x10, 0x24, 0x22, 0x6C };

    /// The standard scan code of a browser, volume, media or mail key; 0 for any other key.
    internal static ushort MediaScanOf(ushort vk) => vk is >= 0xA6 and <= 0xB4 ? MediaScans[vk - 0xA6] : (ushort)0;
}
