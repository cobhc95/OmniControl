namespace OmniControl.App.Features.Hotkeys.Actions;

/// The one rewrite of a stored text ("kind:id" or "kind:id|options") whose id moved (a merged smart device's actions and state): every
/// owner of stored actions (buttons, gestures, gesture rules, automation rules) follows <c>HotkeyRegistry.Moved</c> through this.
public static class MovedActions
{
    /// The stored text with its id replaced when it is of <paramref name="kind"/> and its id is in <paramref name="moved"/>; unchanged otherwise.
    public static string Rewrite(string stored, string kind, IReadOnlyDictionary<string, string> moved)
    {
        var prefix = kind + ":";
        if (!stored.StartsWith(prefix, StringComparison.Ordinal)) return stored;
        var bar = stored.IndexOf('|');
        var id = bar < 0 ? stored[prefix.Length..] : stored[prefix.Length..bar];
        return moved.TryGetValue(id, out var now) ? prefix + now + (bar < 0 ? string.Empty : stored[bar..]) : stored;
    }

    /// A master action as a button, gesture or rule step stores it ("omni:id...").
    public static string RewriteAction(string stored, IReadOnlyDictionary<string, string> moved) => Rewrite(stored, MouseActionCatalog.Omni, moved);

    /// Rewrites every entry of a list in place; true when one changed.
    public static bool RewriteAll(IList<string> stored, IReadOnlyDictionary<string, string> moved)
    {
        var changed = false;
        for (var i = 0; i < stored.Count; i++)
        {
            var now = RewriteAction(stored[i], moved);
            if (now == stored[i]) continue;
            stored[i] = now;
            changed = true;
        }
        return changed;
    }
}
