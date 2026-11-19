namespace OmniControl.App.Features.Hotkeys;

/// What one part of a drawn shortcut is: a key, the "+" between keys, or the dashed slot of the key still to press.
public enum KeycapKind { Key, Plus, Slot }

/// One part of a shortcut drawn by <c>KeycapRow</c> (keycaps, the plus signs between them, the empty slot).
public sealed record KeycapPart(string Text, KeycapKind Kind)
{
    public bool IsKey => Kind == KeycapKind.Key;
    public bool IsPlus => Kind == KeycapKind.Plus;
    public bool IsSlot => Kind == KeycapKind.Slot;
}

/// Turns a shortcut ("Ctrl+Alt+S") or a live preview ("Ctrl + Alt + …") into the parts the keycap row draws.
public static class Keycaps
{
    /// <param name="shortcut">Keys joined by "+"; the <see cref="HotkeyCapture.Ellipsis"/> part (or an empty text) is the key still to press.</param>
    /// <param name="emptySlotText">What the slot says when no key is set at all.</param>
    public static IReadOnlyList<KeycapPart> From(string? shortcut, string emptySlotText)
    {
        var names = (shortcut ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0) return new[] { new KeycapPart(emptySlotText, KeycapKind.Slot) };
        var parts = new List<KeycapPart>();
        foreach (var name in names)
        {
            if (parts.Count > 0) parts.Add(new KeycapPart("+", KeycapKind.Plus));
            parts.Add(new KeycapPart(name, name == HotkeyCapture.Ellipsis ? KeycapKind.Slot : KeycapKind.Key));
        }
        return parts;
    }

    // The arrow keys, in the order a row of them reads, drawn as arrows.
    private static readonly (string Name, string Glyph)[] Arrows = { ("Up", "↑"), ("Down", "↓"), ("Left", "←"), ("Right", "→") };

    private static int ArrowRank(string name)
    {
        var at = Array.FindIndex(Arrows, a => a.Name == name);
        return at >= 0 ? at : Arrows.Length;
    }

    /// Several shortcuts as one short row of keycaps: the keys they all begin with ("Ctrl", "Alt"), then each one's last key; arrow keys are drawn as arrows
    /// and read Up, Down, Left, Right. Shortcuts without a common beginning give just their last keys.
    public static IReadOnlyList<KeycapPart> Summary(IEnumerable<string> shortcuts)
    {
        var split = shortcuts.Select(s => s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Where(p => p.Length > 0).ToList();
        if (split.Count == 0) return Array.Empty<KeycapPart>();
        var common = split[0].Take(split[0].Length - 1).TakeWhile((name, i) => split.All(p => p.Length - 1 > i && p[i] == name)).ToList();
        var last = split.Select(p => p[^1]).OrderBy(ArrowRank).ToList();
        var parts = common.Select(name => new KeycapPart(name, KeycapKind.Key)).ToList();
        var ends = string.Join(" ", last.Select(name => Arrows.FirstOrDefault(a => a.Name == name).Glyph ?? name));
        parts.Add(new KeycapPart(ends, KeycapKind.Key));
        return parts;
    }
}
