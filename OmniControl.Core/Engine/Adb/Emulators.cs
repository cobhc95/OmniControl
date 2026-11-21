namespace OmniControl.Core.Engine;

/// How much setup an emulator needs before OmniControl can use it.
public enum EmulatorEase { Easy, Hard }

/// An Android emulator OmniControl knows: how to find it on this PC (candidate program paths, else its Uninstall entry's install folder plus a
/// relative program path), where its official download page is, the address adb reaches it at and the programs it runs as. UsesGb is the memory it
/// holds while running, PcGb the memory the PC should have.
public sealed record EmulatorInfo(string Id, string Name, string DownloadPage, string DefaultAddress, string[] Programs, string[] Processes, string AdbNote, EmulatorEase Ease, int UsesGb, int PcGb, string Summary,
    string[] RegistryNames, string[] RegistryPrograms, string? RegistryAdb)
{
    /// Only Android Studio's emulator window is hidden and shown by OmniControl (<see cref="Emulators.SetWindowVisible"/>); the others always show theirs.
    public bool CanHide => Id == Emulators.AndroidStudio;

    /// Memory the emulator holds while running, as the menu shows it.
    public string UsesText => $"~{UsesGb} GB";

    /// Memory the PC should have, as the menu shows it.
    public string PcText => $"PC {PcGb} GB+";
}

/// The master list of known emulators (the Devices page's emulator menu reads it) and the lookups on it. Nothing is ever downloaded:
/// a missing emulator is only a link to its official page; adb (the Android SDK's platform-tools) is looked up the same way.
public static class Emulators
{
    public const string AndroidStudio = "studio", BlueStacks = "bluestacks", LdPlayer = "ldplayer", MuMu = "mumu", MEmu = "memu";

    public const string PlatformToolsPage = "https://developer.android.com/tools/releases/platform-tools";

    public static readonly IReadOnlyList<EmulatorInfo> All = new[]
    {
        new EmulatorInfo(BlueStacks, "BlueStacks", "https://www.bluestacks.com/download.html", "127.0.0.1:5555",
            new[] { @"%ProgramFiles%\BlueStacks_nxt\HD-Player.exe", @"%ProgramFiles%\BlueStacks\HD-Player.exe", @"%ProgramFiles(x86)%\BlueStacks_nxt\HD-Player.exe" },
            new[] { "HD-Player" },
            "In BlueStacks' settings (Advanced), switch on Android Debug Bridge (ADB).",
            EmulatorEase.Easy, 3, 8, "Easiest, has ads.",
            new[] { "BlueStacks" }, new[] { "HD-Player.exe" }, "HD-Adb.exe"),
        new EmulatorInfo(AndroidStudio, "Android Studio", "https://developer.android.com/studio", "emulator-5554",
            new[] { @"%LOCALAPPDATA%\Android\Sdk\emulator\emulator.exe" },
            new[] { "qemu-system-x86_64", "qemu-system-aarch64" },
            "Create a virtual device in Android Studio's Device Manager first.",
            EmulatorEase.Hard, 3, 8, "Official, no ads. Needs a virtual device.",
            Array.Empty<string>(), Array.Empty<string>(), null),
        new EmulatorInfo(LdPlayer, "LDPlayer", "https://www.ldplayer.net/", "127.0.0.1:5555",
            new[] { @"C:\LDPlayer\LDPlayer9\dnplayer.exe", @"C:\LDPlayer\LDPlayer4.0\dnplayer.exe", @"%ProgramFiles%\LDPlayer\LDPlayer9\dnplayer.exe", @"%ProgramFiles(x86)%\LDPlayer\LDPlayer9\dnplayer.exe" },
            new[] { "dnplayer", "LdVBoxHeadless" },
            "In LDPlayer's settings (Others), set ADB debugging to local connection.",
            EmulatorEase.Easy, 2, 4, "Light, bundled extras.",
            new[] { "LDPlayer" }, new[] { "dnplayer.exe" }, "adb.exe"),
        new EmulatorInfo(MuMu, "MuMu Player", "https://www.mumuplayer.com/", "127.0.0.1:16384",
            new[]
            {
                @"%ProgramFiles%\Netease\MuMuPlayer\nx_main\MuMuNxMain.exe", @"%ProgramFiles(x86)%\Netease\MuMuPlayer\nx_main\MuMuNxMain.exe",
                @"%ProgramFiles%\Netease\MuMuPlayer-12.0\shell\MuMuPlayer.exe", @"%ProgramFiles%\Netease\MuMuPlayerGlobal-12.0\shell\MuMuPlayer.exe",
            },
            new[] { "MuMuNxMain", "MuMuNxDevice", "MuMuPlayer", "MuMuVMMHeadless" },
            "ADB is built in; MuMu answers at 127.0.0.1:16384.",
            EmulatorEase.Easy, 2, 4, "Play Store and adb built in.",
            new[] { "MuMuPlayer", "MuMu Player" }, new[] { @"nx_main\MuMuNxMain.exe", @"shell\MuMuPlayer.exe" }, @"nx_main\adb.exe"),
        new EmulatorInfo(MEmu, "MEmu", "https://www.memuplay.com/", "127.0.0.1:21503",
            new[] { @"%ProgramFiles%\Microvirt\MEmu\MEmu.exe", @"%ProgramFiles(x86)%\Microvirt\MEmu\MEmu.exe" },
            new[] { "MEmu", "MEmuHeadless" },
            "ADB is built in; MEmu's first instance answers at 127.0.0.1:21503.",
            EmulatorEase.Easy, 2, 4, "Light, installer bundles offers.",
            new[] { "MEmu" }, new[] { @"MEmu\MEmu.exe", "MEmu.exe" }, "adb.exe"),
    };

    public static EmulatorInfo? Of(string id) => All.FirstOrDefault(e => e.Id == id);

    /// The program that starts the emulator (a candidate path, else its Uninstall entry's install folder), or null when it is not installed.
    public static string? Find(EmulatorInfo emulator) =>
        emulator.Programs.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(IsFile) ?? InRegistryFolders(emulator, emulator.RegistryPrograms);

    private static string? InRegistryFolders(EmulatorInfo emulator, IEnumerable<string> relative) =>
        emulator.RegistryNames.Length == 0
            ? null
            : UninstallRegistry.InstallLocations(emulator.RegistryNames).SelectMany(folder => relative.Select(r => Path.Combine(folder, r))).FirstOrDefault(IsFile);

    /// The adb an installed emulator ships with (MuMu's nx_main\adb.exe and the like); null when it is not installed or ships none.
    public static string? FindBundledAdb(EmulatorInfo emulator) =>
        emulator.RegistryAdb is { } adb && Find(emulator) is not null ? InRegistryFolders(emulator, new[] { adb }) : null;

    /// adb from the Android SDK, else the bundled adb of the chosen emulator, else of any installed one; null when there is none.
    public static string? FindAdb(EmulatorInfo? chosen = null) =>
        FindSdkAdb() ?? (chosen is null ? null : FindBundledAdb(chosen)) ?? All.Select(FindBundledAdb).FirstOrDefault(a => a is not null);

    /// adb from the Android SDK's platform-tools (the SDK folder variables, Android Studio's folder, then the PATH); null when it is not installed.
    private static string? FindSdkAdb()
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

    /// Starts the emulator (Android Studio's with its first virtual device) as the normal user; null when it started, otherwise one plain sentence.
    /// <paramref name="quickBoot"/> lets it load its saved snapshot. It always starts with its window; hiding it is <see cref="SetWindowVisible"/>.
    public static string? Open(EmulatorInfo emulator, bool quickBoot = false)
    {
        if (Find(emulator) is not { } program) return $"{emulator.Name} isn't installed.";
        var arguments = new List<string>();
        if (emulator.Id == AndroidStudio)
        {
            if (VirtualDevices().FirstOrDefault() is not { } device) return "Android Studio has no virtual device yet. Create one in its Device Manager.";
            arguments.AddRange(new[] { "-avd", device });
            // A broken quick-boot snapshot leaves a black screen ("error while loading state"), so the snapshot is loaded only after one good cold boot.
            // 2 GB is enough for a home app and keeps the emulator usable on PCs with less memory than Android Studio suggests.
            if (!quickBoot) arguments.Add("-no-snapshot-load");
            arguments.AddRange(new[] { "-memory", "2048" });
        }
        try
        {
            using var _ = NormalRights.Start(program, arguments, hidden: true, readOutput: false, directory: Path.GetDirectoryName(program));
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"{emulator.Name} wouldn't start: {ex.Message}";
        }
    }

    private const string WindowTitlePrefix = "Android Emulator";

    /// Hides or shows the emulator's window (the emulator keeps running with full graphics; only the window goes). Only its titled top-level windows are
    /// touched (the toolbar belongs to them); <see cref="Process.MainWindowHandle"/> cannot find a hidden window, so the windows are enumerated.
    /// True when a window was found (a starting emulator has none yet).
    public static bool SetWindowVisible(EmulatorInfo emulator, bool visible)
    {
        var ids = Running(emulator).Select(p => { using (p) return (uint)p.Id; }).ToHashSet();
        var found = false;
        var text = new System.Text.StringBuilder(256);
        ForegroundNative.EnumWindows((window, _) =>
        {
            ForegroundNative.GetWindowThreadProcessId(window, out var owner);
            if (!ids.Contains(owner)) return true;
            text.Clear();
            ForegroundNative.GetWindowText(window, text, text.Capacity);
            if (!text.ToString().StartsWith(WindowTitlePrefix, StringComparison.Ordinal)) return true;
            found = true;
            ForegroundNative.ShowWindow(window, visible ? ForegroundNative.SwShow : ForegroundNative.SwHide);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// Brings the emulator's main window to the front (restoring it when minimised); false when it has no window (running hidden, or not running).
    public static bool BringToFront(EmulatorInfo emulator)
    {
        foreach (var process in Running(emulator))
        {
            using (process)
            {
                try
                {
                    var window = process.MainWindowHandle;
                    if (window == IntPtr.Zero) continue;
                    if (ForegroundNative.IsIconic(window)) ForegroundNative.ShowWindow(window, ForegroundNative.SwRestore);
                    return ForegroundNative.SetForegroundWindow(window);
                }
                catch (InvalidOperationException) { /* it ended just now */ }
            }
        }
        return false;
    }

    /// The memory each emulator's programs hold now, in bytes by emulator id (0 when none is running), from one look at the process list.
    public static IReadOnlyDictionary<string, long> MemoryUsed(IReadOnlyList<EmulatorInfo> emulators)
    {
        var totals = emulators.ToDictionary(e => e.Id, _ => 0L);
        if (emulators.Count == 0) return totals;
        foreach (var process in Process.GetProcesses()) // one list for every emulator
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    foreach (var emulator in emulators.Where(e => e.Processes.Contains(name, StringComparer.OrdinalIgnoreCase))) totals[emulator.Id] += process.WorkingSet64;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // It ended while the list was read, or Windows refused: it counts nothing.
                }
            }
        }
        return totals;
    }

    /// Stops the emulator's programs hard (only after a clean shutdown did not work).
    public static void Kill(EmulatorInfo emulator)
    {
        foreach (var process in Running(emulator))
        {
            using (process)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { /* it ended just now, or Windows refused */ }
            }
        }
    }

    private static List<Process> Running(EmulatorInfo emulator) => emulator.Processes.SelectMany(Process.GetProcessesByName).ToList();

    /// The names of the virtual devices Android Studio made (the .ini files in the user's .android\avd folder).
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
