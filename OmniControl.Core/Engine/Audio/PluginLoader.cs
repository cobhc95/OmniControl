namespace OmniControl.Core.Engine;

/// <summary>The plug-in formats OmniControl hosts in its own engine.</summary>
public enum PluginFormat
{
    Vst3,
    Vst2,
}

/// <summary>
/// The one place that turns a plug-in file into a loaded <see cref="IPluginEffect"/> of its format, and checks a file by its header
/// alone (never loading it). Every load failure is thrown as <see cref="InvalidOperationException"/> with one plain sentence.
/// </summary>
public static class PluginLoader
{
    // The sentences both formats use, so a refusal reads the same whatever the format.
    internal const string Missing = "The plug-in file isn't there any more.";
    internal const string Not64Bit = "This is a 32-bit plug-in; only 64-bit plug-ins can run.";
    internal const string Instrument = "This is an instrument, not an effect.";
    internal const string Blocked = "Windows Smart App Control blocked this plug-in.";

    /// <summary>Loads the plug-in and sets it up for the given rate and block size (UI thread: its editor belongs to this thread).</summary>
    public static IPluginEffect Load(PluginFormat format, string path, int sampleRate, int maxBlockFrames) => format switch
    {
        PluginFormat.Vst2 => Vst2Effect.Load(path, sampleRate, maxBlockFrames),
        _ => Vst3Effect.Load(path, sampleRate, maxBlockFrames),
    };

    /// <summary>The one sentence saying why the file can't run here, or null when it can be loaded (header only).</summary>
    public static string? Problem(PluginFormat format, string path) => format switch
    {
        PluginFormat.Vst2 => Vst2File.Problem(path),
        _ => Vst3File.Problem(path),
    };
}
