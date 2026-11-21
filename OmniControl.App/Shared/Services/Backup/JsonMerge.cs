using System.Text.Json.Nodes;

namespace OmniControl.App.Shared.Services.Backup;

/// The one merge of a backed-up store into the one here: what is here is kept, what the backup has that is missing here is added.
/// Objects gain missing properties (and are merged inside), lists gain the entries they lack (an entry is the same when its Id, else
/// its Name, else its whole text matches), and a plain value already here is never changed. Pure; no files.
public static class JsonMerge
{
    private static readonly string[] KeyProperties = { "Id", "Name" };

    /// The merged value; <paramref name="here"/> is changed in place when it is an object or a list.
    public static JsonNode? Merge(JsonNode? here, JsonNode? backup)
    {
        switch (here, backup)
        {
            case (null, _):
                return backup?.DeepClone();
            case (JsonObject mine, JsonObject theirs):
                foreach (var (name, value) in theirs)
                {
                    if (mine[name] is { } own) Merge(own, value); // merged in place (a plain value stays as it is)
                    else mine[name] = value?.DeepClone();
                }
                return mine;
            case (JsonArray mine, JsonArray theirs):
                var known = mine.Select(KeyOf).ToHashSet();
                foreach (var entry in theirs.Where(e => known.Add(KeyOf(e)))) mine.Add(entry?.DeepClone());
                return mine;
            default:
                return here;
        }
    }

    /// Removes <paramref name="names"/> from a backed-up store and puts back the values the store here has for them.
    public static JsonNode? KeepOwn(JsonNode? backup, JsonNode? here, IEnumerable<string> names)
    {
        if (backup is not JsonObject incoming) return backup;
        foreach (var name in names)
        {
            incoming.Remove(name);
            if (here is JsonObject own && own[name] is { } value) incoming[name] = value.DeepClone();
        }
        return incoming;
    }

    /// Removes <paramref name="names"/> from every object inside <paramref name="node"/>, at any depth (in place).
    public static void StripInside(JsonNode? node, IReadOnlyCollection<string> names)
    {
        switch (node)
        {
            case JsonObject item:
                foreach (var name in names) item.Remove(name);
                foreach (var (_, value) in item.ToList()) StripInside(value, names);
                break;
            case JsonArray list:
                foreach (var entry in list) StripInside(entry, names);
                break;
        }
    }

    private static string KeyOf(JsonNode? entry)
    {
        if (entry is JsonObject item)
            foreach (var property in KeyProperties)
                if (item[property] is JsonValue key && key.ToString() is { Length: > 0 } text) return $"{property}:{text}";
        return entry?.ToJsonString() ?? "null";
    }
}
