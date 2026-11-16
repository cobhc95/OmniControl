namespace OmniControl.App.Hotkeys;

/// <summary>What "Restore defaults" means for the shortcuts (pure): no key combination on any action, and every action working on all its devices.</summary>
public static class HotkeyDefaults
{
    /// <summary>One action with nothing bound.</summary>
    public static readonly StoredHotkey Cleared = new(string.Empty, false, new List<string>());

    /// <summary>True when no action has a key combination or a device pick of its own.</summary>
    public static bool IsAtDefaults(IReadOnlyDictionary<string, StoredHotkey> all) => all.Values.All(IsCleared);

    /// <summary>The same actions with nothing bound.</summary>
    public static Dictionary<string, StoredHotkey> Of(IReadOnlyDictionary<string, StoredHotkey> all) => all.Keys.ToDictionary(id => id, _ => Cleared);

    private static bool IsCleared(StoredHotkey s) => !s.Keys.Any() && !s.UseSelected;
}
