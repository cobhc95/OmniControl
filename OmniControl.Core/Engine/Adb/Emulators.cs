using System.ComponentModel;
using System.Diagnostics;

namespace OmniControl.Core.Engine;

/// <summary>An Android emulator OmniControl knows: how to find it on this PC, where its official download page is, and the address adb reaches it at.</summary>
public sealed record EmulatorInfo(string Id, string Name, string DownloadPage, string DefaultAddress, string[] Programs, string AdbNote);

/// <summary>
/// The master list of known emulators (the Devices page's emulator menu reads it) and the lookups on it. Nothing is ever downloaded:
/// a missing emulator is only a link to its official page; adb (the Android SDK's platform-tools) is looked up the same way.
/// </summary>
public static class Emulators
{
    public const string AndroidStudio = "studio", BlueStacks = "bluestacks", LdPlayer = "ldplayer";

    public const string PlatformToolsPage = "https://developer.android.com/tools/releases/platform-tools";

    public static readonly IReadOnlyList<EmulatorInfo> All = new[]
    {
        new EmulatorInfo(BlueStacks, "BlueStacks", "https://www.bluestacks.com/download.html", "127.0.0.1:5555",
            new[] { @"%ProgramFiles%\BlueStacks_nxt\HD-Player.exe", @"%ProgramFiles%\BlueStacks\HD-Player.exe" },
            "In BlueStacks' settings (Advanced), switch on Android Debug Bridge (ADB)."),
        new EmulatorInfo(AndroidStudio, "Android Studio", "https://developer.android.com/studio", "emulator-5554",
            new[] { @"%LOCALAPPDATA%\Android\Sdk\emulator\emulator.exe" },
            "Create a virtual device in Android Studio's Device Manager first."),
        new EmulatorInfo(LdPlayer, "LDPlayer", "https://www.ldplayer.net/", "127.0.0.1:5555",
            new[] { @"C:\LDPlayer\LDPlayer9\dnplayer.exe", @"C:\LDPlayer\LDPlayer4.0\dnplayer.exe", @"%ProgramFiles%\LDPlayer\LDPlayer9\dnplayer.exe" },
            "In LDPlayer's settings (Others), set ADB debugging to local connection."),
    };

    public static EmulatorInfo? Of(string id) => All.FirstOrDefault(e => e.Id == id);

    /// <summary>The program that starts the emulator, or null when it is not installed.</summary>
    public static string? Find(EmulatorInfo emulator) =>
        emulator.Programs.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists);

    /// <summary>adb from the Android SDK's platform-tools (the SDK folder variables, Android Studio's folder, then the PATH); null when it is not installed.</summary>
    public static string? FindAdb()
    {
        var folders = new List<string?>
        {
            Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
            Environment.GetEnvironmentVariable("ANDROID_HOME"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
        };
        var candidates = folders.Where(f => !string.IsNullOrEmpty(f)).Select(f => Path.Combine(f!, "platform-tools", "adb.exe"))
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => Path.Combine(p.Trim().Trim('"'), "adb.exe")));
        return candidates.FirstOrDefault(IsFile);
    }

    private static bool IsFile(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (ArgumentException)
        {
            return false; // a PATH entry that is not a valid path
        }
    }

    /// <summary>Starts the emulator (Android Studio's with its first virtual device) as the normal user; null when it started, otherwise one plain sentence.</summary>
    public static string? Open(EmulatorInfo emulator)
    {
        if (Find(emulator) is not { } program) return $"{emulator.Name} isn't installed.";
        var start = new ProcessStartInfo(program) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(program) ?? string.Empty };
        if (emulator.Id == AndroidStudio)
        {
            if (VirtualDevices().FirstOrDefault() is not { } device) return "Android Studio has no virtual device yet. Create one in its Device Manager.";
            start.ArgumentList.Add("-avd");
            start.ArgumentList.Add(device);
        }
        try
        {
            using var _ = Process.Start(start);
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"{emulator.Name} wouldn't start: {ex.Message}";
        }
    }

    /// <summary>The names of the virtual devices Android Studio made (the .ini files in the user's .android\avd folder).</summary>
    public static IReadOnlyList<string> VirtualDevices()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "avd");
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.ini").Select(Path.GetFileNameWithoutExtension).Where(n => n is { Length: > 0 }).Select(n => n!).ToList() : new List<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new List<string>(); // an unreadable folder only means no virtual device is listed
        }
    }
}
