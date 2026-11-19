namespace OmniControl.App.Shared.Presets;

/// The naming rules every preset kind shares (adapted from TabForge's PresetRules). No UI.
public static class PresetRules
{
    public const int MaxNameLength = 64;

    /// Why the (trimmed) text cannot be a preset name; null when it can.
    public static string? Error(string text)
    {
        var name = text.Trim();
        if (name.Length == 0) return "Type a name.";
        if (name.Length > MaxNameLength) return $"Use at most {MaxNameLength} characters.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "A name cannot contain \\ / : * ? \" < > |";
        if (name.EndsWith('.')) return "A name cannot end with a dot.";
        return null;
    }

    /// "name copy", then "name copy 2" ...: the first one no preset has yet.
    public static string CopyName(string name, IReadOnlyList<string> existing)
    {
        for (var n = 1; ; n++)
        {
            var suffix = n == 1 ? " copy" : $" copy {n}";
            var candidate = name[..Math.Min(name.Length, MaxNameLength - suffix.Length)].TrimEnd() + suffix;
            if (!existing.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }
    }

    /// "name", then "name 2", "name 3" ...: the first one no preset has yet (a new preset's name before the user types one).
    public static string FirstFree(string name, IReadOnlyList<string> existing)
    {
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? name : $"{name} {n}";
            if (!existing.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }
    }

    /// A new stable id for a saved preset (a preset's shortcut follows it through renames and moves).
    public static string NewId() => Guid.NewGuid().ToString("N");

    public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
