using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmniControl.App.Services.Backup;

/// <summary>One area a backup holds: its store, and how many entries it has when it is a list (null for plain settings).</summary>
public sealed record BackupArea(DataStore Store, int? Count);

/// <summary>What a backup file holds, read and checked: the version that made it, when, and each store's json.</summary>
public sealed class BackupContents
{
    public required string AppVersion { get; init; }
    public required DateTime Created { get; init; }
    public required IReadOnlyList<BackupArea> Areas { get; init; }
    public required IReadOnlyDictionary<string, string> Files { get; init; }
}

/// <summary>
/// The .omnibackup file: a zip of every backed-up store (<see cref="DataStores.BackedUp"/>) that exists, plus a small manifest (format,
/// app version, date, the count per area). Logs, undo copies and safety journals are never in it. Reading checks everything and gives
/// one plain sentence for a bad file. Works on any folder, so tests use a temporary one.
/// </summary>
public static class BackupFile
{
    public const string Extension = "omnibackup";
    private const string ManifestName = "manifest.json";
    private const int Format = 1;

    /// <summary>A backup or a store in it larger than this is not one OmniControl made.</summary>
    private const long MaxBytes = 64L * 1024 * 1024;

    private const string BadFile = "That file is not an OmniControl backup, or it is damaged.";

    private sealed record Manifest(int Format, string AppVersion, DateTime Created, List<ManifestArea> Areas);

    private sealed record ManifestArea(string File, string Area, int? Count);

    /// <summary>Writes the backup of <paramref name="folder"/> to <paramref name="path"/>; gives the number of areas written.</summary>
    public static int Write(string folder, string path, string appVersion)
    {
        // Written beside the target first, so a failed export never leaves half a backup under the chosen name.
        var partial = path + ".partial";
        File.Delete(partial);
        var count = WriteZip(folder, partial, appVersion);
        File.Move(partial, path, overwrite: true);
        return count;
    }

    private static int WriteZip(string folder, string path, string appVersion)
    {
        var areas = new List<ManifestArea>();
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var store in DataStores.BackedUp)
        {
            var file = Path.Combine(folder, store.FileName);
            if (!File.Exists(file)) continue;
            var text = File.ReadAllText(file);
            if (!TryParse(text, out var node)) continue; // a damaged store is not worth carrying over: it loads as defaults here too
            zip.CreateEntryFromFile(file, store.FileName);
            areas.Add(new ManifestArea(store.FileName, store.Area, CountOf(store, node)));
        }
        var entry = zip.CreateEntry(ManifestName);
        using (var stream = entry.Open()) JsonSerializer.Serialize(stream, new Manifest(Format, appVersion, DateTime.Now, areas));
        return areas.Count;
    }

    /// <summary>The checked contents of <paramref name="path"/>, or the one sentence that says why it can't be used.</summary>
    public static (BackupContents? Contents, string? Problem) Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxBytes) return (null, BadFile);
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry(ManifestName) is not { } manifestEntry || manifestEntry.Length > MaxBytes) return (null, BadFile);
            Manifest? manifest;
            using (var stream = manifestEntry.Open()) manifest = JsonSerializer.Deserialize<Manifest>(stream);
            if (manifest is null) return (null, BadFile);
            if (manifest.Format > Format) return (null, "That backup was made by a newer OmniControl; update OmniControl to use it.");

            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var areas = new List<BackupArea>();
            foreach (var entry in zip.Entries)
            {
                if (DataStores.BackedUpByName(entry.FullName) is not { } store) continue; // the manifest, or a store this version does not know
                if (entry.Length > MaxBytes) return (null, BadFile);
                using var reader = new StreamReader(entry.Open());
                var text = reader.ReadToEnd();
                if (!TryParse(text, out var node)) return (null, BadFile);
                files[store.FileName] = text;
                areas.Add(new BackupArea(store, CountOf(store, node)));
            }
            if (areas.Count == 0) return (null, "That backup holds no settings.");
            return (new BackupContents { AppVersion = manifest.AppVersion ?? string.Empty, Created = manifest.Created, Areas = areas, Files = files }, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"That file could not be read: {ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or NotSupportedException)
        {
            return (null, BadFile);
        }
    }

    private static bool TryParse(string text, out JsonNode? node)
    {
        try
        {
            node = JsonNode.Parse(text);
            return node is not null;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }

    /// <summary>The entries of a list store (the items of its lists, or the keys of a dictionary of entries); null for plain settings.</summary>
    private static int? CountOf(DataStore store, JsonNode? node)
    {
        if (!store.Mergeable) return null;
        return node switch
        {
            JsonArray list => list.Count,
            JsonObject root when root.Any(p => p.Value is JsonArray) => root.Where(p => !(store.NotImported ?? Array.Empty<string>()).Contains(p.Key))
                .Sum(p => p.Value is JsonArray list ? list.Count : 0),
            JsonObject root => root.Count,
            _ => null,
        };
    }
}
