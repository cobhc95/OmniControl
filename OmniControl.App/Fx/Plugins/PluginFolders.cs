namespace OmniControl.App.Fx.Plugins;

/// <summary>The standard places Windows VST plug-ins are installed (the same list TabForge scans).</summary>
public static class PluginFolders
{
    public static IReadOnlyList<string> Common()
    {
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new List<string>();
        Add(roots, common, "VST3");
        Add(roots, programs, "Common Files", "VST3");
        Add(roots, programs, "VstPlugins");
        Add(roots, programs, "Steinberg", "VSTPlugins");
        Add(roots, common, "VST2");
        Add(roots, local, "Programs", "Common", "VST3");
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Add(List<string> roots, string root, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        roots.Add(parts.Aggregate(root, Path.Combine));
    }
}
