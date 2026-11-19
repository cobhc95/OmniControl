using System.Security;

namespace OmniControl.Core.Engine;

/// A place an app's program may be, with the arguments that start it (Discord and the old Teams start through their updater).
public sealed record KnownPath(string Path, string Arguments = "");

/// How to find one app on this PC. Any list may be empty; they are tried in the order of the properties below.
public sealed record AppLocator
{
    /// Program names Windows' "App Paths" registry keys know ("chrome.exe").
    public IReadOnlyList<string> AppPaths { get; init; } = Array.Empty<string>();

    /// Install folders (environment variables allowed) to look in.
    public IReadOnlyList<KnownPath> KnownPaths { get; init; } = Array.Empty<KnownPath>();

    /// Start menu shortcut names (without ".lnk") whose target is the app.
    public IReadOnlyList<string> StartMenuNames { get; init; } = Array.Empty<string>();

    /// A Microsoft Store app: its package name ("SpotifyAB.SpotifyMusic") and the id of its app inside the package.
    public string? StorePackage { get; init; }
    public string StoreAppId { get; init; } = "App";

    /// A command or protocol that is part of Windows ("calc.exe", "ms-settings:"); found when it exists.
    public string? Direct { get; init; }
}

/// What starting an app means: the program (or shell address), its arguments and the folder to start in. <see cref="IsFile"/> is false for a Store app or a protocol.
public sealed record AppTarget(string Path, string Arguments, string StartIn, bool IsFile);

/// Finds installed apps by their <see cref="AppLocator"/>: App Paths registry, known install folders, Start menu shortcuts, Store packages,
/// then Windows' own commands. Read only and slow (it may read the whole Start menu): call it off the UI thread.
public static class AppLookup
{
    private const string StorePackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
    private const string AppsFolder = @"shell:AppsFolder\";
    private const string ExeEnding = ".exe";

    /// The target of each locator that is installed (by the id it was given); the others are left out.
    public static IReadOnlyDictionary<string, AppTarget> Find(IReadOnlyDictionary<string, AppLocator> locators)
    {
        var found = new Dictionary<string, AppTarget>();
        var storeNames = locators.Values.Any(l => l.StorePackage is not null) ? InstalledStorePackages() : new List<string>();
        Lazy<IReadOnlyList<FoundProgram>> startMenu = new(InstalledPrograms.Find);
        foreach (var (id, locator) in locators)
            if (FindOne(locator, storeNames, startMenu) is { } target) found[id] = target;
        return found;
    }

    private static AppTarget? FindOne(AppLocator locator, List<string> storeNames, Lazy<IReadOnlyList<FoundProgram>> startMenu)
    {
        foreach (var name in locator.AppPaths)
            if (InstalledPrograms.AppPathOf(name) is { } path) return Program(path);
        foreach (var known in locator.KnownPaths)
            if (Environment.ExpandEnvironmentVariables(known.Path) is var expanded && File.Exists(expanded)) return Program(expanded, known.Arguments);
        if (locator.StartMenuNames.Count > 0
            && startMenu.Value.FirstOrDefault(p => locator.StartMenuNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) is { } shortcut)
            return Program(shortcut.Path);
        if (locator.StorePackage is { } package && StoreAddress(package, locator.StoreAppId, storeNames) is { } address)
            return new AppTarget(address, string.Empty, string.Empty, IsFile: false);
        return locator.Direct is { } direct ? DirectTarget(direct) : null;
    }

    private static AppTarget Program(string path, string arguments = "") =>
        new(path, arguments, System.IO.Path.GetDirectoryName(path) ?? string.Empty, IsFile: true);

    /// A Windows command that is there (a program in Windows' folders), or a protocol such as "ms-settings:", which has no file to check.
    private static AppTarget? DirectTarget(string direct)
    {
        if (!direct.EndsWith(ExeEnding, StringComparison.OrdinalIgnoreCase)) return new AppTarget(direct, string.Empty, string.Empty, IsFile: false);
        var folders = new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
        return folders.Select(f => System.IO.Path.Combine(f, direct)).FirstOrDefault(File.Exists) is { } path ? Program(path) : null;
    }

    /// The Store apps registered for this user, as the package folder names Windows keeps ("SpotifyAB.SpotifyMusic_1.2.3.0_x64__zpdnekdrzrea0").
    private static List<string> InstalledStorePackages()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StorePackagesKey);
            return key?.GetSubKeyNames().ToList() ?? new List<string>();
        }
        catch (SecurityException) { return new List<string>(); } // not readable: Store apps are then simply not found
        catch (UnauthorizedAccessException) { return new List<string>(); }
        catch (IOException) { return new List<string>(); }
    }

    /// "shell:AppsFolder\Name_PublisherId!AppId" for an installed package: the folder name is Name_Version_Architecture_ResourceId_PublisherId.
    private static string? StoreAddress(string package, string appId, List<string> installed)
    {
        var folder = installed.FirstOrDefault(n => n.StartsWith(package + "_", StringComparison.OrdinalIgnoreCase));
        if (folder is null) return null;
        var parts = folder.Split('_');
        return parts.Length < 2 ? null : $"{AppsFolder}{parts[0]}_{parts[^1]}!{appId}";
    }
}
