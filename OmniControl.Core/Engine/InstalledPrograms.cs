using System.Runtime.InteropServices;
using System.Security;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Win32;

namespace OmniControl.Core.Engine;

/// <summary>A program found on this PC that has an .exe to start: its name and the full path of the .exe.</summary>
public sealed record FoundProgram(string Name, string Path);

/// <summary>
/// The programs installed on this PC, read only: the Start menu's shortcuts (the current user's and all users'; their targets) and
/// Windows' "App Paths" registry keys. Slow (it reads many files): call it off the UI thread. Setup tools, uninstallers and Windows'
/// own programs are left out, and a program found twice is listed once.
/// </summary>
public static class InstalledPrograms
{
    private const string ExeEnding = ".exe";
    private const string ShortcutPattern = "*.lnk";
    private const string StartMenuPrograms = "Programs";
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string ShellProgId = "WScript.Shell";

    /// <summary>File names that start like a setup tool's, and Start menu names that say "uninstall".</summary>
    private static readonly string[] SetupStarts = { "unins", "setup", "install", "update" };
    private const string UninstallWord = "uninstall";

    public static IReadOnlyList<FoundProgram> Find() =>
        Clean(FromStartMenu().Concat(FromAppPaths()), Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    /// <summary>Keeps only .exe programs that are not setup tools or Windows' own, once each (by path, ignoring case), the first name found wins.</summary>
    internal static List<FoundProgram> Clean(IEnumerable<FoundProgram> found, string windowsFolder) =>
        Dedupe(found.Where(p => p.Path.EndsWith(ExeEnding, StringComparison.OrdinalIgnoreCase) && !IsSetupTool(p, windowsFolder)));

    internal static List<FoundProgram> Dedupe(IEnumerable<FoundProgram> found)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return found.Where(p => seen.Add(Normal(p.Path))).ToList();
    }

    /// <summary>An installer, uninstaller or updater by its file or Start menu name, or a program inside Windows' own folder.</summary>
    internal static bool IsSetupTool(FoundProgram program, string windowsFolder)
    {
        var file = System.IO.Path.GetFileName(program.Path);
        return SetupStarts.Any(s => file.StartsWith(s, StringComparison.OrdinalIgnoreCase))
            || program.Name.Contains(UninstallWord, StringComparison.OrdinalIgnoreCase)
            || (windowsFolder.Length > 0 && Normal(program.Path).StartsWith(Normal(windowsFolder) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normal(string path)
    {
        var trimmed = path.Trim().Trim('"');
        try { return System.IO.Path.GetFullPath(trimmed); }
        catch (ArgumentException) { return trimmed; } // not a path Windows understands: compare it as written
        catch (NotSupportedException) { return trimmed; }
    }

    // ---- Start menu ----

    private static IEnumerable<FoundProgram> FromStartMenu()
    {
        var shortcuts = new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu }
            .Select(f => System.IO.Path.Combine(Environment.GetFolderPath(f), StartMenuPrograms))
            .SelectMany(ShortcutsIn)
            .ToList();
        if (shortcuts.Count == 0 || Type.GetTypeFromProgID(ShellProgId) is not { } shellType) yield break;

        object? shell = null;
        try { shell = Activator.CreateInstance(shellType); }
        catch (COMException) { /* Windows Script Host is switched off: the App Paths keys still give programs */ }
        if (shell is null) yield break;
        try
        {
            foreach (var file in shortcuts)
                if (TargetOf(shell, file) is { } target)
                    yield return new FoundProgram(System.IO.Path.GetFileNameWithoutExtension(file), target);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    private static List<string> ShortcutsIn(string folder)
    {
        try { return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, ShortcutPattern, SearchOption.AllDirectories).ToList() : new(); }
        catch (IOException) { return new(); } // a folder that cannot be read is skipped
        catch (UnauthorizedAccessException) { return new(); }
    }

    /// <summary>The .exe a shortcut starts, when it exists (a shortcut to a document, a folder or a Store app has none).</summary>
    private static string? TargetOf(object shell, string shortcut)
    {
        object? link = null;
        try
        {
            link = ((dynamic)shell).CreateShortcut(shortcut);
            string target = ((dynamic)link).TargetPath;
            return target.EndsWith(ExeEnding, StringComparison.OrdinalIgnoreCase) && File.Exists(target) ? target : null;
        }
        catch (COMException) { return null; } // an unreadable or broken shortcut
        catch (RuntimeBinderException) { return null; }
        finally { if (link is not null) Marshal.FinalReleaseComObject(link); }
    }

    // ---- App Paths ----

    private static IEnumerable<FoundProgram> FromAppPaths()
    {
        var places = new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) };
        foreach (var (hive, view) in places)
            foreach (var path in AppPathsOf(hive, view))
                yield return new FoundProgram(ProgramFile.NameOf(path), path);
    }

    private static List<string> AppPathsOf(RegistryHive hive, RegistryView view)
    {
        var paths = new List<string>();
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var keys = root.OpenSubKey(AppPathsKey);
            if (keys is null) return paths;
            foreach (var name in keys.GetSubKeyNames().Where(n => n.EndsWith(ExeEnding, StringComparison.OrdinalIgnoreCase)))
            {
                using var key = keys.OpenSubKey(name);
                if (key?.GetValue(null) is string value && value.Trim().Trim('"') is { Length: > 0 } path && File.Exists(path)) paths.Add(path);
            }
        }
        catch (SecurityException) { /* a key that may not be read is skipped */ }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        return paths;
    }
}
