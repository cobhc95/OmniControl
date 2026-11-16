namespace OmniControl.App.Hotkeys;

/// <summary>What one part of a drawn shortcut is: a key, the "+" between keys, or the dashed slot of the key still to press.</summary>
public enum KeycapKind { Key, Plus, Slot }

/// <summary>One part of a shortcut drawn by <c>KeycapRow</c> (keycaps, the plus signs between them, the empty slot).</summary>
public sealed record KeycapPart(string Text, KeycapKind Kind)
{
    public bool IsKey => Kind == KeycapKind.Key;
    public bool IsPlus => Kind == KeycapKind.Plus;
    public bool IsSlot => Kind == KeycapKind.Slot;
}

/// <summary>Turns a shortcut ("Ctrl+Alt+S") or a live preview ("Ctrl + Alt + …") into the parts the keycap row draws.</summary>
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
}
