using OmniControl.App.Services;
using OmniControl.App.Audio;

namespace OmniControl.App.Fx.Plugins;

/// <summary>
/// Everything the Add effect window can offer: the built-in effects, the plug-ins found by the last scan, and the ones the user loaded by hand.
/// The scan result and the hand-loaded files are remembered in plugins.json (read when first needed) so the list is there next time
/// without scanning again. One library for the whole program (the Add effect windows and the App settings page's scan share it). UI thread.
/// </summary>
public sealed class PluginLibrary
{
    private sealed class Stored
    {
        public List<PluginEntry> Scanned { get; set; } = new();
        public List<PluginEntry> Loaded { get; set; } = new();
    }

    private static readonly DataStore Store = DataStores.Plugins;

    private Stored? _stored;

    private Stored Data => _stored ??= AppDataStore.Load<Stored>(Store) ?? new();

    private IReadOnlyList<PluginEntry> BuiltIns { get; } =
        FxCatalog.All.Select(k => new PluginEntry(k.Name, k.Kind, PluginEntry.BuiltIn, "OmniControl", k.Description)).ToList();

    /// <summary>Built-ins first, then plug-ins by name. Files that no longer exist are left out.</summary>
    public IReadOnlyList<PluginEntry> All() => BuiltIns
        .Concat(Data.Loaded.Concat(Data.Scanned)
            .Where(p => File.Exists(p.Path) || Directory.Exists(p.Path))
            .DistinctBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        .ToList();

    public bool HasScanned => Data.Scanned.Count > 0;

    /// <summary>What the last scan found, in one sentence ("Found 20 plug-ins: 15 VST3, 5 VST2."), the one wording every scan button shows.</summary>
    public string FoundText()
    {
        var count = Data.Scanned.Count;
        if (count == 0) return "No plug-ins found in the plug-in folders.";
        var types = FxCatalog.PluginTypes.Select(t => (t.Tag, Count: Data.Scanned.Count(p => p.Format == t.Tag))).Where(t => t.Count > 0);
        return $"Found {count} plug-in{(count == 1 ? "" : "s")}: {string.Join(", ", types.Select(t => $"{t.Count} {t.Tag}"))}.";
    }

    /// <summary>Looks in the plug-in folders, the user's own included (off the UI thread), and remembers what it found.</summary>
    public async Task<int> ScanAsync(IProgress<int>? progress, CancellationToken cancel)
    {
        var folders = PluginFolders.All(); // read on the UI thread: the user's folders are a live setting
        var found = await Task.Run(() => PluginScanner.Scan(folders, cancel, progress), cancel);
        Data.Scanned = found;
        Write();
        return found.Count;
    }

    /// <summary>Remembers a plug-in file the user chose by hand. Null, with the reason, when it can't run here.</summary>
    public PluginEntry? AddFile(string path, out string? problem)
    {
        var entry = PluginScanner.FromPath(path, out problem);
        if (entry is null) return null;
        if (!Data.Loaded.Any(p => string.Equals(p.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))
        {
            Data.Loaded.Add(entry);
            Write();
        }
        return entry;
    }

    private void Write() => AppDataStore.Save(Store, Data);
}
