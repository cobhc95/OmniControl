using OmniControl.App.Audio;
using OmniControl.Core.Engine;

namespace OmniControl.App.Fx.Plugins;

/// <summary>
/// Finds plug-ins of every type in <see cref="FxCatalog.PluginTypes"/> by looking at file names and file headers only. It never loads
/// or runs a plug-in, so a scan can't crash or run unknown code. A VST3 is a single .vst3 file or a folder named *.vst3 (a bundle; the
/// scan doesn't look further inside it); a VST2 is a .dll that exports a VST2 entry point. Plug-ins that can't run here (32-bit, no
/// Windows version, not a plug-in) are left out of the scan; a hand-picked file says why.
/// </summary>
public static class PluginScanner
{
    /// <summary>How many folder levels below each scanned folder are searched (the common folders and the user's own alike).</summary>
    public const int MaxDepth = 4;

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

    /// <summary>A plug-in file or bundle the user picked by hand; null with the reason when it can't be used.</summary>
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

    /// <summary>The plug-in type whose files end like <paramref name="path"/>; null for any other file.</summary>
    private static PluginType? TypeOf(string path) =>
        FxCatalog.PluginTypes.FirstOrDefault(t => path.EndsWith(t.Extension, StringComparison.OrdinalIgnoreCase));

    private static void Walk(string dir, int depth, Dictionary<string, PluginEntry> found, CancellationToken cancel, IProgress<int>? progress)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // no access or vanished: skip

        foreach (var entry in entries)
        {
            if (cancel.IsCancellationRequested) return;
            if (TypeOf(entry) is not null) // a VST3 bundle folder is a plug-in too, so the scan does not go into it
            {
                if (FromPath(entry, out _) is not { } plugin) continue;
                found[plugin.Path] = plugin;
                progress?.Report(found.Count);
            }
            else if (depth < MaxDepth && Directory.Exists(entry)) Walk(entry, depth + 1, found, cancel, progress);
        }
    }
}
