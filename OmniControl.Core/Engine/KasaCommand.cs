namespace OmniControl.Core.Engine;

/// <summary>
/// How to run python-kasa's command line (<c>kasa</c>) for a Tapo plug: where its program is and the arguments of a command. The account is
/// never put in the arguments (they show in process lists): the caller hands it over in <see cref="UserVariable"/> and
/// <see cref="PasswordVariable"/>, which kasa reads itself.
/// </summary>
public static class KasaCommand
{
    public const string UserVariable = "KASA_USERNAME", PasswordVariable = "KASA_PASSWORD";

    private const string KasaExe = "kasa.exe", ScriptsFolder = "Scripts", PythonFolder = "Python";

    public static readonly string[] PowerOn = { "on" }, PowerOff = { "off" };

    /// <summary>The arguments for a Tapo plug (P100 / P110: KLAP, login version 2), then the command.</summary>
    public static IReadOnlyList<string> Arguments(string host, IReadOnlyList<string> command) =>
        new[] { "--host", host, "--device-family", "SMART.TAPOPLUG", "--encrypt-type", "KLAP", "--login-version", "2" }.Concat(command).ToList();

    /// <summary>
    /// The kasa program installed with <paramref name="python"/>: <c>kasa.exe</c> in its Scripts folder (or its own folder, as in a virtual
    /// environment) or in the current user's Scripts folder of any Python; null when there is none yet.
    /// </summary>
    public static string? FindExe(string python)
    {
        var home = Path.GetDirectoryName(python);
        if (home is null) return null;
        var candidates = new List<string> { Path.Combine(home, ScriptsFolder, KasaExe), Path.Combine(home, KasaExe) };
        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), PythonFolder);
        try
        {
            if (Directory.Exists(roaming))
                candidates.AddRange(Directory.EnumerateDirectories(roaming).Select(d => Path.Combine(d, ScriptsFolder, KasaExe)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder only means kasa is not found there.
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}
