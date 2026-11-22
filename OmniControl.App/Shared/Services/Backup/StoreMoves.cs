using System.Text.Json.Nodes;

namespace OmniControl.App.Shared.Services.Backup;

/// What an older version kept of a store in another store's file (<see cref="OldFile"/>): each move takes the value at <c>From</c> (a dotted path)
/// out of the old file into the top-level property <c>To</c> of the store; each drop is a path taken out with nothing moved (<c>*</c> = every
/// entry of a list).
public sealed record StoreMove(string OldFile, IReadOnlyList<(string To, string From)> Moves, IReadOnlyList<string> Drops);

/// THE one way settings that moved to a store of their own come out of their old file: once at start in the data folder, and in every
/// backup or other PC's files before they are offered or imported, so an older file still fills the new area. Re-running changes nothing.
public static class StoreMoves
{
    /// The store's text (made from the old file only when the store has none and something moved) and the old file without what moved;
    /// null when the old file holds nothing of the store or is not json.
    public static (string? New, string Old)? Split(StoreMove move, string oldText, string? newText)
    {
        if (!BackupFile.TryParse(oldText, out var node) || node is not JsonObject old) return null;
        var made = new JsonObject();
        foreach (var (to, from) in move.Moves)
            if (Take(old, from.Split('.'), out var value)) made[to] = value;
        var dropped = move.Drops.Select(d => Drop(old, d.Split('.'), 0)).ToList().Contains(true);
        if (made.Count == 0 && !dropped) return null;
        return (newText ?? (made.Count > 0 ? made.ToJsonString() : null), old.ToJsonString());
    }

    /// <paramref name="files"/> (by file name) with every moved store split out of its old file.
    public static IReadOnlyDictionary<string, string> Upgrade(IReadOnlyDictionary<string, string> files)
    {
        Dictionary<string, string>? result = null;
        foreach (var store in DataStores.All)
        {
            if (store.MovedFrom is not { } move || !files.TryGetValue(move.OldFile, out var old)) continue;
            if (Split(move, old, files.GetValueOrDefault(store.FileName)) is not var (made, rest)) continue;
            result ??= new Dictionary<string, string>(files, StringComparer.OrdinalIgnoreCase);
            if (made is not null) result[store.FileName] = made;
            result[move.OldFile] = rest;
        }
        return result ?? files;
    }

    /// At start, before anything is loaded: the store is written first, then its old file without what moved, each atomically. A file that
    /// exists but could not be read is never written.
    public static void RunAtStart()
    {
        foreach (var store in DataStores.All)
        {
            if (store.MovedFrom is not { } move || DataStores.BackedUpByName(move.OldFile) is not { } oldStore) continue;
            if (AppDataStore.LoadText(oldStore) is not { } old) continue;
            var here = AppDataStore.LoadText(store);
            if (here is null && File.Exists(AppDataStore.PathOf(store.FileName))) continue;
            if (Split(move, old, here) is not var (made, rest)) continue;
            if (here is null && made is not null && !AppDataStore.SaveText(store, made)) continue;
            if (AppDataStore.SaveText(oldStore, rest)) AppDataStore.AppendLog(Guard.LogFile, $"{store.Area} moved out of {move.OldFile} into {store.FileName}.");
        }
    }

    private static bool Take(JsonObject root, string[] path, out JsonNode? value)
    {
        value = null;
        var at = root;
        foreach (var part in path[..^1])
            if (at[part] is JsonObject inner) at = inner;
            else return false;
        if (!at.TryGetPropertyValue(path[^1], out var found)) return false;
        at.Remove(path[^1]);
        value = found;
        return true;
    }

    private static bool Drop(JsonNode? node, string[] path, int at) => path[at] switch
    {
        _ when at == path.Length - 1 => node is JsonObject last && last.Remove(path[at]),
        "*" => node is JsonArray list && list.Select(e => Drop(e, path, at + 1)).ToList().Contains(true),
        var name => node is JsonObject item && Drop(item[name], path, at + 1),
    };
}
