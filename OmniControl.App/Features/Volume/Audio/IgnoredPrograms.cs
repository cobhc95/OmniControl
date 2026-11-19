namespace OmniControl.App.Features.Volume.Audio;

/// Programs that are never Volume rows: the setup tools of the effects engines (they make a sound session while they test a device,
/// and a row for them would only be clutter). The one list; <see cref="MixerService"/> skips them while scanning.
public static class IgnoredPrograms
{
    /// Folders whose programs are all setup tools: Equalizer APO's install folder (Device Selector, Configuration Editor, Benchmark, uninstaller).
    private static readonly string[] Folders = { @"\equalizerapo\" };

    /// Installers by the start of their file name: Equalizer APO's and VB-CABLE's.
    private static readonly string[] FileStarts = { "equalizerapo-x64-", "equalizerapo64-", "vbcable_setup" };

    /// <param name="key">A program key (<see cref="AppAudioEntry.KeyFor"/>: its lower-case path).</param>
    public static bool Contains(string key) =>
        Folders.Any(f => key.Contains(f, StringComparison.Ordinal))
        || FileStarts.Any(f => Path.GetFileName(key).StartsWith(f, StringComparison.Ordinal));
}
