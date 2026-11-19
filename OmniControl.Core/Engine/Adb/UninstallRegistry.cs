using System.Security;

namespace OmniControl.Core.Engine;

/// Reads Windows' Uninstall keys (read only) to find where a program says it was installed.
public static class UninstallRegistry
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly (RegistryHive Hive, RegistryView View)[] Places =
        { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) };

    /// The install folders (that exist) of every entry whose DisplayName starts with one of the given names, ignoring case.
    public static IReadOnlyList<string> InstallLocations(IReadOnlyList<string> displayNameStarts)
    {
        var found = new List<string>();
        foreach (var (hive, view) in Places)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var keys = root.OpenSubKey(UninstallKey);
                if (keys is null) continue;
                foreach (var name in keys.GetSubKeyNames())
                {
                    using var key = keys.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") is not string display || !displayNameStarts.Any(s => display.StartsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
                    if (key.GetValue("InstallLocation") is string location && location.Trim().Trim('"') is { Length: > 0 } folder && Directory.Exists(folder)) found.Add(folder);
                }
            }
            catch (SecurityException) { /* a key that may not be read is skipped */ }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return found;
    }
}
