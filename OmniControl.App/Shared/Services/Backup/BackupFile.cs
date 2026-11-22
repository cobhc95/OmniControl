using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmniControl.App.Shared.Services.Backup;

/// One area a backup holds: its store, and how many entries it has when it is a list (null for plain settings).
public sealed record BackupArea(DataStore Store, int? Count);

/// What a backup file holds, read and checked: the version that made it, when, and each store's json.
public sealed class BackupContents
{
    public required string AppVersion { get; init; }
    public required DateTime Created { get; init; }
    public required IReadOnlyList<BackupArea> Areas { get; init; }
    public required IReadOnlyDictionary<string, string> Files { get; init; }
}

/// The .omnibackup file: a zip of every backed-up store (<see cref="DataStores.BackedUp"/>) that exists, plus a small manifest (format,
/// app version, date, the count per area). Logs, undo copies and safety journals are never in it. Reading checks everything and gives
/// one plain sentence for a bad file. Works on any folder, so tests use a temporary one.
public static class BackupFile
{
    public const string Extension = "omnibackup";
    private const string ManifestName = "manifest.json";

    /// The automatic backup taken before "Import settings from another PC" (one file in the data folder, replaced by the next import; a wipe removes it).
    public const string BeforeImportName = "import-backup." + Extension;
    private const int Format = 1;

    /// A backup, or all it holds unpacked together, larger than this is not one OmniControl made.
    internal const long MaxBytes = 64L * 1024 * 1024;

    private const string BadFile = "That file is not an OmniControl backup, or it is damaged.";

    private sealed record Manifest(int Format, string AppVersion, DateTime Created, List<ManifestArea> Areas);

    private sealed record ManifestArea(string File, string Area, int? Count);

    /// Writes the backup of <paramref name="folder"/> to <paramref name="path"/>: every backed-up store, or only <paramref name="stores"/>
    /// (a file of some parts reads like any other, also in older versions); gives the number of stores written.
    public static int Write(string folder, string path, string appVersion, IEnumerable<DataStore>? stores = null)
    {
        // Written beside the target first, so a failed export never leaves half a backup under the chosen name.
        var partial = path + ".partial";
        File.Delete(partial);
        try
        {
            var count = WriteZip(folder, partial, appVersion, stores ?? DataStores.BackedUp);
            if (count > 0 && Read(partial).Contents is null) throw new InvalidDataException("it would be too large or damaged for OmniControl to read back, so the previous backup was kept"); // same rules as an import
            File.Move(partial, path, overwrite: true);
            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try { File.Delete(partial); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                AppDataStore.AppendLog(Guard.LogFile, $"Backup: the unfinished file stays for now ({cleanup.Message})"); // the next export deletes it
            }
            throw;
        }
    }

    private static int WriteZip(string folder, string path, string appVersion, IEnumerable<DataStore> stores)
    {
        var areas = new List<ManifestArea>();
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var store in stores)
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

    /// The checked contents of <paramref name="path"/>, or the one sentence that says why it can't be used.
    public static (BackupContents? Contents, string? Problem) Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxBytes) return (null, BadFile);
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry(ManifestName) is not { } manifestEntry) return (null, BadFile);
            var budget = MaxBytes; // for everything unpacked together, counted as it unpacks (an entry's stated size may lie)
            var manifest = JsonSerializer.Deserialize<Manifest>(new MemoryStream(Unpack(manifestEntry, ref budget)));
            if (manifest is null) return (null, BadFile);
            if (manifest.Format > Format) return (null, "That backup was made by a newer OmniControl; update OmniControl to use it.");

            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var areas = new List<BackupArea>();
            foreach (var entry in zip.Entries)
            {
                if (DataStores.BackedUpByName(entry.FullName) is not { } store) continue; // the manifest, or a store this version does not know
                if (files.ContainsKey(store.FileName)) return (null, BadFile); // one store twice: OmniControl never writes that
                using var reader = new StreamReader(new MemoryStream(Unpack(entry, ref budget)));
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

    /// An entry's bytes, unpacked at most up to what is left of <paramref name="budget"/> (which it uses up); more is a bad file.
    private static byte[] Unpack(ZipArchiveEntry entry, ref long budget)
    {
        if (entry.Length > budget) throw new InvalidDataException("too large");
        using var stream = entry.Open();
        using var bytes = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if ((budget -= read) < 0) throw new InvalidDataException("too large");
            bytes.Write(chunk, 0, read);
        }
        return bytes.ToArray();
    }

    /// The entries of the list store whose text this is (null for plain settings or damaged text): what an import window counts.
    public static int? CountOfText(DataStore store, string text) => TryParse(text, out var node) ? CountOf(store, node) : null;

    /// Whether the text is json (the node), as every store has to be.
    public static bool TryParse(string text, out JsonNode? node)
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

    /// The entries of a list store (the items of its lists, or the keys of a dictionary of entries); null for plain settings.
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
