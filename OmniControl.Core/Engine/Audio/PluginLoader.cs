namespace OmniControl.Core.Engine;

/// The plug-in formats OmniControl hosts in its own engine.
public enum PluginFormat
{
    Vst3,
    Vst2,
}

/// The one place that turns a plug-in file into a loaded <see cref="IPluginEffect"/> of its format, and checks a file by its header
/// alone (never loading it). Every load failure is thrown as <see cref="InvalidOperationException"/> with one plain sentence.
public static class PluginLoader
{
    // The sentences both formats use, so a refusal reads the same whatever the format.
    internal const string Missing = "The plug-in file isn't there any more.";
    internal const string Not64Bit = "This is a 32-bit plug-in; only 64-bit plug-ins can run.";
    internal const string Instrument = "This is an instrument, not an effect.";
    internal const string Blocked = "Windows Smart App Control blocked this plug-in.";

    internal const string NotLocal = "Plug-ins load only from a folder on this PC's own drives, not from a network share or device path.";
    internal const string UserFolder = "While OmniControl runs as administrator it loads plug-ins only from folders only administrators can change, such as Program Files.";

    private static readonly bool Elevated = Elevation.IsElevated();

    // A plug-in's own DLLs are looked for only in its folder and System32, never on PATH or in the current folder, so no library of a
    // known name elsewhere is loaded into OmniControl (elevated when it is).
    private const uint LoadSearchSystem32 = 0x00000800, LoadSearchDllLoadDir = 0x00000100;
    private const int ErrorModuleNotFound = 126;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LoadLibraryExW")]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);

    /// Loads a plug-in's library (its DllMain runs) with that search order; a refusal by Windows becomes one plain sentence.
    internal static IntPtr LoadLibrary(string binary)
    {
        var module = LoadLibraryEx(binary, IntPtr.Zero, LoadSearchSystem32 | LoadSearchDllLoadDir);
        if (module != IntPtr.Zero) return module;
        var error = Marshal.GetLastPInvokeError();
        if (AppControl.IsBlock(error)) throw new InvalidOperationException(Blocked);
        throw new InvalidOperationException(error == ErrorModuleNotFound
            ? "Couldn't load the plug-in: a file it needs is missing."
            : $"Couldn't load the plug-in: {new Win32Exception(error).Message}");
    }

    /// Loads the plug-in and sets it up for the given rate and block size (UI thread: its editor belongs to this thread).
    public static IPluginEffect Load(PluginFormat format, string path, int sampleRate, int maxBlockFrames)
    {
        if (PlaceProblem(path) is { } refused) throw new InvalidOperationException(refused);
        return format switch
        {
            PluginFormat.Vst2 => Vst2Effect.Load(path, sampleRate, maxBlockFrames),
            _ => Vst3Effect.Load(path, sampleRate, maxBlockFrames),
        };
    }

    /// The one sentence saying why the file can't run here, or null when it can be loaded (its place and header only).
    public static string? Problem(PluginFormat format, string path) => PlaceProblem(path) ?? format switch
    {
        PluginFormat.Vst2 => Vst2File.Problem(path),
        _ => Vst3File.Problem(path),
    };

    // A plug-in is a library run inside OmniControl: only from a local drive (never a share, \\?\ or \\.\ path), and while elevated only
    // from a folder its user cannot change (or anything running as them could swap the library and run elevated).
    private static string? PlaceProblem(string path)
    {
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] is not ('\\' or '/') || !Path.IsPathFullyQualified(path)) return NotLocal;
        try
        {
            if (new DriveInfo(path[..1]).DriveType == DriveType.Network) return NotLocal; // a mapped share
            return Elevated && !Resources.NetworkServiceSetup.CanServeFrom(path) ? UserFolder : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return NotLocal;
        }
    }
}
