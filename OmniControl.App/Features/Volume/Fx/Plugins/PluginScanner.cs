using System.Collections.Concurrent;
using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Fx.Plugins;

/// Finds plug-ins of every type in <see cref="FxCatalog.PluginTypes"/> by looking at file names and file headers only. It never loads
/// or runs a plug-in, so a scan can't crash or run unknown code. A VST3 is a single .vst3 file or a folder named *.vst3 (a bundle; the
/// scan doesn't look further inside it); a VST2 is a .dll that exports a VST2 entry point. Plug-ins that can't run here (32-bit, no
/// Windows version, not a plug-in) are left out of the scan; a hand-picked file says why.
public static class PluginScanner
{
    /// How many folder levels below each scanned folder are searched (the common folders and the user's own alike).
    public const int MaxDepth = 4;

    /// The folders are walked side by side (one per scanned folder) and the found files' headers read side by side; a cancelled scan
    /// gives what it found so far.
    public static List<PluginEntry> Scan(IEnumerable<string> roots, CancellationToken cancel, IProgress<int>? progress = null)
    {
        var found = new ConcurrentDictionary<string, PluginEntry>(StringComparer.OrdinalIgnoreCase);
        var options = new ParallelOptions { CancellationToken = cancel };
        try
        {
            var candidates = new ConcurrentBag<string>();
            Parallel.ForEach(roots.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists), options,
                root => Walk(new DirectoryInfo(root), 0, candidates, cancel));
            Parallel.ForEach(candidates, options, path =>
            {
                if (FromPath(path, out _) is { } plugin && found.TryAdd(plugin.Path, plugin)) progress?.Report(found.Count);
            });
        }
        catch (OperationCanceledException)
        {
            // Stopped by the user: what was found so far is the answer.
        }
        return found.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// A plug-in file or bundle the user picked by hand; null with the reason when it can't be used.
    public static PluginEntry? FromPath(string path, out string? problem)
    {
        var trimmed = path.TrimEnd('\\', '/');
        if (TypeOf(trimmed) is not { } type)
        {
            problem = $"That isn't a plug-in OmniControl can use (a {string.Join(" or ", FxCatalog.PluginTypes.Select(t => $"{t.Tag} {t.Extension}"))} file).";
            return null;
        }
        problem = PluginLoader.Problem(type.Format, trimmed);
        return problem is null ? new PluginEntry(Path.GetFileNameWithoutExtension(trimmed), trimmed, type.Tag) : null;
    }

    /// The plug-in type whose files end like <paramref name="path"/>; null for any other file.
    private static PluginType? TypeOf(string path) =>
        FxCatalog.PluginTypes.FirstOrDefault(t => path.EndsWith(t.Extension, StringComparison.OrdinalIgnoreCase));

    /// Gathers every file or bundle named like a plug-in under <paramref name="dir"/>; whether it is a folder comes with the listing (no
    /// second look per entry).
    private static void Walk(DirectoryInfo dir, int depth, ConcurrentBag<string> candidates, CancellationToken cancel)
    {
        List<FileSystemInfo> entries;
        try { entries = dir.EnumerateFileSystemInfos().ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // no access or vanished: skip

        foreach (var entry in entries)
        {
            if (cancel.IsCancellationRequested) return;
            if (TypeOf(entry.FullName) is not null) candidates.Add(entry.FullName); // a VST3 bundle folder is a plug-in too, so the scan does not go into it
            else if (depth < MaxDepth && entry is DirectoryInfo sub) Walk(sub, depth + 1, candidates, cancel);
        }
    }
}
