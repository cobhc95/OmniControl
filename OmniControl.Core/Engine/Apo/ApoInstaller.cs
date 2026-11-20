using System.Security.Cryptography;

namespace OmniControl.Core.Engine.Apo;

/// Equalizer APO's official 1.4.2 x64 installer, shipped unmodified in the app folder's <see cref="Folder"/>. Run silently (NSIS /S) it
/// installs Equalizer APO as its own program (its folder, Start menu, uninstaller and Apps entry) and opens its Device Selector, where the
/// user ticks the devices; OmniControl never changes what it installed.
public static class ApoInstaller
{
    /// Both retyped in OmniControl.App.csproj (the copy into the app folder) and tools/setup.iss (Setup's task).
    public const string Folder = "equalizer-apo", FileName = "EqualizerAPO-x64-1.4.2.exe";

    /// The SHA-256 of the file downloaded from Equalizer APO's SourceForge page (its size and MD5 match the ones published there).
    private const string Sha256 = "7403BE7427BBE1936A40DDED082829B6E217FC4F5990FEE5CBA501F0AE055AFA";

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, Folder, FileName);

    /// Runs it as administrator (one prompt) and waits until it and its Device Selector are closed (<see cref="CoreTiming.ConfiguratorWaitMs"/>).
    /// Held open for reading only from the hash check until it ends, so what runs is what was checked. Call off the UI thread.
    public static HelperResult Run()
    {
        try
        {
            using var file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(file)) != Sha256
                ? new HelperResult(-1, HelperFailure.NotStarted, $"{FilePath} is not the shipped installer.")
                : HelperProcess.RunElevated(FilePath, "/S", CoreTiming.ConfiguratorWaitMs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HelperResult(-1, HelperFailure.NotStarted, $"{FilePath}: {ex.Message}");
        }
    }
}
