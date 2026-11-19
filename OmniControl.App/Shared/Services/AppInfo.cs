using System.Reflection;

namespace OmniControl.App.Shared.Services;

/// The one place the app's name and version are read: the project's <c>Version</c> property (OmniControl.App.csproj) is the only source.
internal static class AppInfo
{
    public const string Name = "OmniControl";

    /// The GitHub repository the releases are published in ("owner/name"): the one place it is written (the update check and its link read it).
    public const string Repository = "cobhc95/OmniControl";

    /// The page listing every release, in a browser.
    public static string ReleasesPageUrl => $"https://github.com/{Repository}/releases";

    /// GitHub's public list of the repository's releases (pre-releases included).
    public static string ReleasesApiUrl => $"https://api.github.com/repos/{Repository}/releases?per_page=20";

    /// The version as written in the project file, without any build suffix.
    public static string Version { get; } = ReadVersion();

    /// "Version: x.y.z", for the sidebar and anywhere a short label is wanted.
    public static string VersionText => $"Version: {Version}";

    /// "OmniControl 0.1", for window titles.
    public static string NameAndVersion => $"{Name} {Version}";

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString(2) ?? string.Empty;
        var plus = text.IndexOf('+'); // the SDK appends "+<commit>" to the informational version
        return plus >= 0 ? text[..plus] : text;
    }
}
