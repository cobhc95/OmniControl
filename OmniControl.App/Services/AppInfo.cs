using System.Reflection;

namespace OmniControl.App.Services;

/// <summary>The one place the app's name and version are read: the project's <c>Version</c> property (OmniControl.App.csproj) is the only source.</summary>
internal static class AppInfo
{
    public const string Name = "OmniControl";

    /// <summary>The GitHub repository the releases are published in ("owner/name"): the one place it is written (the update check and its link read it).</summary>
    public const string Repository = "cobhc95/OmniControl";

    /// <summary>The page listing every release, in a browser.</summary>
    public static string ReleasesPageUrl => $"https://github.com/{Repository}/releases";

    /// <summary>GitHub's public list of the repository's releases (pre-releases included).</summary>
    public static string ReleasesApiUrl => $"https://api.github.com/repos/{Repository}/releases?per_page=20";

    /// <summary>The version as written in the project file, without any build suffix.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>"Version: x.y.z", for the sidebar and anywhere a short label is wanted.</summary>
    public static string VersionText => $"Version: {Version}";

    /// <summary>"OmniControl 0.1", for window titles.</summary>
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
