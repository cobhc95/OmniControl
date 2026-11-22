namespace OmniControl.App.Shared.Presets;

/// Which preset a group's actions apply (pure): "Switch to" with its "Run again to switch back", and Next / Previous.
public static class PresetStepping
{
    /// The preset "Switch to <paramref name="preset"/>" applies: the one in use before it (<paramref name="before"/>) when switching back
    /// is on and <paramref name="preset"/> is still in use, else <paramref name="preset"/> itself.
    public static string Target(string preset, string? inUse, string? before, bool switchBack) =>
        switchBack && inUse == preset && before is not null && before != preset ? before : preset;

    /// The preset after (<paramref name="direction"/> +1) or before (-1) the one in use, in the group's order, wrapping at both ends; with
    /// none in use, the first or the last. Null for an empty group.
    public static string? Step(IReadOnlyList<string> presets, string? inUse, int direction)
    {
        if (presets.Count == 0) return null;
        var at = inUse is null ? -1 : presets.ToList().IndexOf(inUse);
        if (at < 0) return direction > 0 ? presets[0] : presets[^1];
        return presets[Wrap.Index(at + direction, presets.Count)];
    }
}
