namespace OmniControl.App.Fx.Plugins;

/// <summary>
/// Finds VST3 plug-ins by looking at file names only. It never loads or runs a plug-in, so a scan can't crash or run unknown code.
/// A VST3 is either a single .vst3 file or a folder named *.vst3 (a bundle); the scan doesn't look inside a bundle.
/// </summary>
public static class PluginScanner
{
    private const int MaxDepth = 4;

    public static List<PluginEntry> Scan(IEnumerable<string> roots, CancellationToken cancel, IProgress<int>? progress = null)
    {
        var found = new Dictionary<string, PluginEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (cancel.IsCancellationRequested) break;
            if (Directory.Exists(root)) Walk(root, 0, found, cancel, progress);
        }
        return found.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A plug-in file or bundle the user picked by hand, or null when it isn't a VST3.</summary>
    public static PluginEntry? FromPath(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        if (!trimmed.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase)) return null;
        if (!File.Exists(trimmed) && !Directory.Exists(trimmed)) return null;
        return new PluginEntry(Path.GetFileNameWithoutExtension(trimmed), trimmed, PluginEntry.Vst3);
    }

    private static void Walk(string dir, int depth, Dictionary<string, PluginEntry> found, CancellationToken cancel, IProgress<int>? progress)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir).ToList(); }
        catch (Exception) { return; } // no access or vanished: skip

        foreach (var entry in entries)
        {
            if (cancel.IsCancellationRequested) return;
            if (FromPath(entry) is { } plugin)
            {
                found[plugin.Path] = plugin;
                progress?.Report(found.Count);
            }
            else if (depth < MaxDepth && Directory.Exists(entry)) Walk(entry, depth + 1, found, cancel, progress);
        }
    }
}
