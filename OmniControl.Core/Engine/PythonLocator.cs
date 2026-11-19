using System.Security;

namespace OmniControl.Core.Engine;

/// Finds an installed Python interpreter, read only: the installs registered with Windows (PEP 514, the current user's then all users'),
/// then the Python launcher, then python.exe on the PATH. Windows' "App execution alias" stub in WindowsApps only opens the Store, so it
/// does not count. Null when there is none.
public static class PythonLocator
{
    private const string RegistryRoot = @"SOFTWARE\Python\PythonCore";
    private const string InstallPathKey = "InstallPath";
    private const string ExecutableValue = "ExecutablePath";
    private const string PythonExe = "python.exe", LauncherExe = "py.exe";
    private const string StoreStubFolder = "WindowsApps";

    public static string? Find() =>
        FromRegistry(Registry.CurrentUser) ?? FromRegistry(Registry.LocalMachine) ?? Launcher() ?? FromPath();

    private static string? FromRegistry(RegistryKey hive)
    {
        try
        {
            using var root = hive.OpenSubKey(RegistryRoot);
            if (root is null) return null;
            foreach (var version in root.GetSubKeyNames().OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase))
            {
                using var install = root.OpenSubKey($@"{version}\{InstallPathKey}");
                var path = install?.GetValue(ExecutableValue) as string
                           ?? (install?.GetValue(null) is string folder ? Path.Combine(folder, PythonExe) : null);
                if (path is not null && File.Exists(path)) return path;
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // An unreadable key only means this place has no usable Python; the others are still looked at.
        }
        return null;
    }

    private static string? Launcher()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), LauncherExe);
        return File.Exists(path) ? path : null;
    }

    private static string? FromPath() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(folder => !folder.Contains(StoreStubFolder, StringComparison.OrdinalIgnoreCase))
            .Select(folder => SafeCombine(folder, PythonExe))
            .FirstOrDefault(path => path is not null && File.Exists(path));

    /// A PATH entry with characters a path cannot hold is skipped.
    private static string? SafeCombine(string folder, string file)
    {
        try { return Path.Combine(folder, file); }
        catch (ArgumentException) { return null; } // a damaged PATH entry is not a Python
    }
}
