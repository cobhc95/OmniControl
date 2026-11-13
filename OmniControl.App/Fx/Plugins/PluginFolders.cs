using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Fx.Plugins;

/// <summary>
/// Where the plug-in scan looks: the standard places Windows VST3 and VST2 plug-ins are installed (TabForge's list, the VST2 folders
/// Steinberg's and other installers use, and the VST2 folder the registry names), then the user's own folders from App settings
/// (<see cref="AppSettings.PluginFolders"/>, read each time, so a folder added or removed counts at the next scan).
/// </summary>
public static class PluginFolders
{
    public static IReadOnlyList<string> All() => Common().Concat(AppSettings.Current.PluginFolders)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static IEnumerable<string> Common()
    {
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new List<string>();
        Add(roots, common, "VST3");
        Add(roots, programs, "Common Files", "VST3");
        Add(roots, local, "Programs", "Common", "VST3");
        Add(roots, programs, "VstPlugins");
        Add(roots, programs, "Steinberg", "VSTPlugins");
        Add(roots, common, "VST2");
        Add(roots, common, "Steinberg", "VST2");
        if (Vst2File.RegisteredFolder() is { } registered) roots.Add(registered);
        return roots;
    }

    private static void Add(List<string> roots, string root, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        roots.Add(parts.Aggregate(root, Path.Combine));
    }
}
