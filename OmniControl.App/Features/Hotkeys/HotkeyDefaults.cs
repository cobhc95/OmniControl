namespace OmniControl.App.Features.Hotkeys;

/// What "Restore defaults" means for the shortcuts (pure): every action on its default key combination (<see cref="HotkeyAction.DefaultKey"/>,
/// none for most), on all its devices and with its settings back at their defaults. <c>keyOf</c> gives an action's default key by id (null: none), so nothing here keeps its own list.
public static class HotkeyDefaults
{
    /// One action with nothing bound.
    public static readonly StoredHotkey Cleared = new(string.Empty, false, new List<string>());

    /// One action on its default key (<see cref="Cleared"/> when it has none).
    public static StoredHotkey For(string? defaultKey) => defaultKey is null ? Cleared : new(defaultKey, false, new List<string>());

    /// True when every action has exactly its default key, no device pick of its own and no changed settings.
    public static bool IsAtDefaults(IReadOnlyDictionary<string, StoredHotkey> all, Func<string, string?>? keyOf = null) =>
        all.All(p => !p.Value.UseSelected && p.Value.Options is not { Count: > 0 } && p.Value.Keys.SequenceEqual(For(keyOf?.Invoke(p.Key)).Keys));

    /// The same actions on their default keys.
    public static Dictionary<string, StoredHotkey> Of(IReadOnlyDictionary<string, StoredHotkey> all, Func<string, string?>? keyOf = null) =>
        all.Keys.ToDictionary(id => id, id => For(keyOf?.Invoke(id)));
}
