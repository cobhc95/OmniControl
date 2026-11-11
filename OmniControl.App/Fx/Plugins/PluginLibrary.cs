using System.Text.Json;
using OmniControl.App.Audio;

namespace OmniControl.App.Fx.Plugins;

/// <summary>
/// Everything the Add effect window can offer: the built-in effects, the plug-ins found by the last scan, and the ones the user loaded by hand.
/// The scan result and the hand-loaded files are remembered in plugins.json so the list is there next time without scanning again.
/// </summary>
public sealed class PluginLibrary
{
    private sealed class Stored
    {
        public List<PluginEntry> Scanned { get; set; } = new();
        public List<PluginEntry> Loaded { get; set; } = new();
    }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniControl", "plugins.json");

    private Stored _stored = Read();

    public IReadOnlyList<PluginEntry> BuiltIns { get; } =
        FxCatalog.All.Select(k => new PluginEntry(k.Name, k.Kind, PluginEntry.BuiltIn, "OmniControl", k.Description)).ToList();

    /// <summary>Built-ins first, then plug-ins by name. Files that no longer exist are left out.</summary>
    public IReadOnlyList<PluginEntry> All() => BuiltIns
        .Concat(_stored.Loaded.Concat(_stored.Scanned)
            .Where(p => File.Exists(p.Path) || Directory.Exists(p.Path))
            .DistinctBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        .ToList();

    public bool HasScanned => _stored.Scanned.Count > 0;

    /// <summary>Looks in the common plug-in folders (off the UI thread) and remembers what it found.</summary>
    public async Task<int> ScanCommonAsync(IProgress<int>? progress, CancellationToken cancel)
    {
        var found = await Task.Run(() => PluginScanner.Scan(PluginFolders.Common(), cancel, progress), cancel);
        _stored.Scanned = found;
        Write();
        return found.Count;
    }

    /// <summary>Remembers a plug-in file the user chose by hand. Null when the file isn't a VST3.</summary>
    public PluginEntry? AddFile(string path)
    {
        var entry = PluginScanner.FromPath(path);
        if (entry is null) return null;
        if (!_stored.Loaded.Any(p => string.Equals(p.Path, entry.Path, StringComparison.OrdinalIgnoreCase)))
        {
            _stored.Loaded.Add(entry);
            Write();
        }
        return entry;
    }

    private static Stored Read()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception) { /* a damaged list is rebuilt by the next scan */ }
        return new();
    }

    private void Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_stored));
        }
        catch (Exception) { /* not saving is not worth stopping for */ }
    }
}
