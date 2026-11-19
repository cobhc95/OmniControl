namespace OmniControl.Core.Engine;

/// Checks whether a .vst3 file or bundle has a binary OmniControl (64-bit Windows) can load, by reading the file header only: the
/// plug-in's code is never loaded or run, so the plug-in scan stays safe.
public static class Vst3File
{
    /// The one sentence saying why the plug-in can't run here, or null when it can be loaded.
    public static string? Problem(string path)
    {
        if (Directory.Exists(path))
        {
            var name = Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));
            var binary = Path.Combine(path, "Contents", "x86_64-win", name + ".vst3");
            if (File.Exists(binary)) return ProblemOf(binary);
            return File.Exists(Path.Combine(path, "Contents", "x86-win", name + ".vst3")) ? PluginLoader.Not64Bit : NotForWindows;
        }
        return File.Exists(path) ? ProblemOf(path) : PluginLoader.Missing;
    }

    private const string NotForWindows = "This plug-in has no 64-bit Windows version in it.";

    private static string? ProblemOf(string binary) => PeFile.Machine(binary) switch
    {
        PeFile.MachineX64 or null => null, // unreadable now: loading it says why
        PeFile.MachineX86 => PluginLoader.Not64Bit,
        _ => NotForWindows,
    };
}
