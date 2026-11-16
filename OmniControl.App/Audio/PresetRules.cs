namespace OmniControl.App.Audio;

/// <summary>The naming rules every preset kind shares (adapted from TabForge's PresetRules). No UI.</summary>
public static class PresetRules
{
    public const int MaxNameLength = 64;

    /// <summary>Why the (trimmed) text cannot be a preset name; null when it can.</summary>
    public static string? Error(string text)
    {
        var name = text.Trim();
        if (name.Length == 0) return "Type a name.";
        if (name.Length > MaxNameLength) return $"Use at most {MaxNameLength} characters.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "A name cannot contain \\ / : * ? \" < > |";
        if (name.EndsWith('.')) return "A name cannot end with a dot.";
        return null;
    }

    /// <summary>"name copy", then "name copy 2" ...: the first one no preset has yet.</summary>
    public static string CopyName(string name, IReadOnlyList<string> existing)
    {
        for (var n = 1; ; n++)
        {
            var suffix = n == 1 ? " copy" : $" copy {n}";
            var candidate = name[..Math.Min(name.Length, MaxNameLength - suffix.Length)].TrimEnd() + suffix;
            if (!existing.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }
    }

    /// <summary>"name", then "name 2", "name 3" ...: the first one no preset has yet (a new preset's name before the user types one).</summary>
    public static string FirstFree(string name, IReadOnlyList<string> existing)
    {
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? name : $"{name} {n}";
            if (!existing.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }
    }

    /// <summary>A new stable id for a saved preset (a preset's shortcut follows it through renames and moves).</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
