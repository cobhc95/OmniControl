using System.Reflection;
using System.Runtime.Loader;
using OmniControl.Core.Engine.Apo;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.Core.Engine;

/// The files Setup leaves out with a feature that was not ticked when "Don't install them" is chosen (tools/setup.iss retypes them): while
/// one is missing its feature is not installed, so nothing touches the assembly or driver that is not there.
public static class OptionalFiles
{
    // One statement: the App csproj reads every quoted .dll name in it to take them out of deps.json (OptionalFiles.targets), so retype nothing elsewhere.
    public const string Sensors = "LibreHardwareMonitorLib.dll", QrCodes = "zxing.dll", SensorSupport = "DiskInfoToolkit.dll,HidSharp.dll,RAMSPDToolkit-NDD.dll,BlackSharp.Core.dll";
    public static readonly string EqualizerApo = Path.Combine(ApoInstaller.Folder, ApoInstaller.FileName);
    public static readonly string PacketFilter = Path.Combine(PacketFilterSetup.Folder, PacketFilterSetup.Inf);

    public static bool Present(string file) => File.Exists(Path.Combine(AppContext.BaseDirectory, file));

    /// Every managed file Setup may leave out, as assembly names.
    private static readonly HashSet<string> Names = new[] { Sensors, QrCodes }.Concat(SensorSupport.Split(','))
        .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase)!;

    /// For the default load context's Resolving event: deps.json does not list these files (so a left-out one never stops the start), which
    /// means the host does not find a present one either; this loads it from the app folder, only these names and only when the file exists.
    public static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name is not { } file || !Names.Contains(file)) return null;
        var path = Path.Combine(AppContext.BaseDirectory, file + ".dll");
        return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    }

    /// Loads every optional assembly that is present, the way the app will; false if one that is there will not load (the start check).
    public static bool LoadPresent()
    {
        var ok = true;
        foreach (var file in Names.Where(n => Present(n + ".dll")))
        {
            try { AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(file)); }
            catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException) { ok = false; }
        }
        return ok;
    }
}