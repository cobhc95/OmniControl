namespace OmniControl.App.Hotkeys;

/// <summary>
/// OmniControl actions that were merged into another: the old id still works wherever it was stored (hotkeys.json, a button, a gesture, a rule),
/// read as the id that replaced it. The one place the old ids are named.
/// </summary>
public static class RenamedActions
{
    private static readonly IReadOnlyDictionary<string, string> Merged = new Dictionary<string, string>
    {
        ["automation.open"] = "app.show", // "Open OmniControl" and "Show OmniControl window" were the same action
    };

    /// <summary>The id an action is called now.</summary>
    public static string Id(string id) => Merged.TryGetValue(id, out var current) ? current : id;

    /// <summary>The saved shortcuts with merged ids renamed (an action that already has its own entry keeps it).</summary>
    public static Dictionary<string, T> Migrate<T>(Dictionary<string, T> saved)
    {
        foreach (var (old, current) in Merged)
            if (saved.Remove(old, out var value)) saved.TryAdd(current, value);
        return saved;
    }
}
