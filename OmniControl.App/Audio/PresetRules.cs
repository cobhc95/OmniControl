namespace OmniControl.App.Audio;

/// <summary>The naming and ordering rules every preset kind shares (adapted from TabForge's PresetRules and PresetManagePresenter). No UI.</summary>
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

    public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The moves that shift the chosen names one row (delta) as a block: each moves past the row beside it when that row is not chosen,
    /// so a name at the edge, or behind a chosen one that cannot move, stays. Empty when nothing can move.
    /// </summary>
    public static List<(string Name, int Index)> BlockSteps(IReadOnlyList<string> order, IReadOnlyList<string> chosen, int delta)
    {
        var list = order.ToList();
        var set = new HashSet<string>(chosen, StringComparer.OrdinalIgnoreCase);
        var moving = list.Where(set.Contains).ToList();
        if (delta > 0) moving.Reverse();
        var steps = new List<(string Name, int Index)>();
        foreach (var name in moving)
        {
            var from = list.FindIndex(n => Same(n, name));
            var to = from + delta;
            if (to < 0 || to >= list.Count || set.Contains(list[to])) continue;
            list.RemoveAt(from);
            list.Insert(to, name);
            steps.Add((name, to));
        }
        return steps;
    }
}
