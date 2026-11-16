namespace OmniControl.Core.Engine;

/// <summary>
/// python-kasa's command line (<c>kasa</c>): where its program is, how a command template becomes arguments, and how its answers read. The
/// password is never put in the arguments (they show in process lists): the caller hands it over in <see cref="PasswordVariable"/> (and the
/// email in <see cref="UserVariable"/>), which kasa reads itself.
/// </summary>
public static class KasaCommand
{
    public const string UserVariable = "KASA_USERNAME", PasswordVariable = "KASA_PASSWORD";

    /// <summary>The placeholders of a command template, filled in at run time.</summary>
    public const string IpField = "{ip}", EmailField = "{email}", PasswordField = "{password}";

    /// <summary>The email as kasa gets it: trimmed, and in lower case when the template asks (Tapo compares it exactly); the field keeps what was typed.</summary>
    public static string EmailFor(string email, bool lowerCase) => lowerCase ? email.Trim().ToLowerInvariant() : email.Trim();

    /// <summary>A template's first word that means "the kasa program that was found" (kasa.exe, or python -m kasa).</summary>
    public const string Program = "kasa";

    /// <summary>
    /// A template ("kasa --host {ip} on") as its words: quotes group words, {ip} and {email} are filled in, and {password} is never put in:
    /// the word and the option before it ("--password") are left out, because the password travels in <see cref="PasswordVariable"/>.
    /// </summary>
    public static IReadOnlyList<string> Fill(string template, string ip, string email)
    {
        var words = Split(template);
        var filled = new List<string>();
        foreach (var word in words)
        {
            if (word.Contains(PasswordField, StringComparison.OrdinalIgnoreCase))
            {
                if (filled.Count > 0 && filled[^1].StartsWith('-')) filled.RemoveAt(filled.Count - 1);
                continue;
            }
            filled.Add(word.Replace(IpField, ip, StringComparison.OrdinalIgnoreCase).Replace(EmailField, email, StringComparison.OrdinalIgnoreCase));
        }
        return filled;
    }

    /// <summary>The words of a command line; double quotes keep spaces inside one word.</summary>
    public static IReadOnlyList<string> Split(string line)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (word.Length > 0) { words.Add(word.ToString()); word.Clear(); }
                continue;
            }
            word.Append(c);
        }
        if (word.Length > 0) words.Add(word.ToString());
        return words;
    }

    /// <summary>The on/off state in kasa's <c>state</c> answer ("Device state: True", "State: ON"); null when it does not say.</summary>
    public static bool? StateOf(string printed)
    {
        var m = System.Text.RegularExpressions.Regex.Match(printed, @"(?im)^\s*(device\s+)?state\s*:\s*(true|on|false|off)\b");
        return m.Success ? m.Groups[2].Value.ToLowerInvariant() is "true" or "on" : null;
    }

    private const string KasaExe = "kasa.exe", ScriptsFolder = "Scripts", PythonFolder = "Python";

    // Lines kasa prints that are not the answer: asyncio/aiohttp complaining at exit, the hint to debug, and its progress line.
    private static readonly string[] NoiseMarkers = { "Unclosed client session", "Unclosed connector", "Run with --debug", "Discovering device", "client_connection:", "connections:", "connector:" };

    private const string RaisedError = "Raised error:";

    /// <summary>kasa's output without its noise lines (see <see cref="NoiseMarkers"/>).</summary>
    public static IReadOnlyList<string> Meaningful(string printed) =>
        printed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !NoiseMarkers.Any(n => l.Contains(n, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>
    /// The line of kasa's output that says what went wrong, noise dropped: the first "Raised error:" line, else the last line naming an error
    /// or exception (Python ends a traceback with it), else the last line; empty when it printed nothing.
    /// </summary>
    public static string ProblemLine(string printed)
    {
        var lines = Meaningful(printed);
        return lines.FirstOrDefault(l => l.StartsWith(RaisedError, StringComparison.OrdinalIgnoreCase))
            ?? lines.LastOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.Contains("exception", StringComparison.OrdinalIgnoreCase)
                || l.Contains("failed", StringComparison.OrdinalIgnoreCase) || l.Contains("unable", StringComparison.OrdinalIgnoreCase))
            ?? lines.LastOrDefault() ?? string.Empty;
    }

    /// <summary>Python code that prints the real python.exe (the <c>py</c> launcher and the Store alias only point at it).</summary>
    public const string ExecutableQuery = "import sys;print(sys.executable)";

    /// <summary>Python code that prints the folders its programs (kasa.exe) are installed in: the main one and the user's own.</summary>
    public const string ScriptsQuery = "import sysconfig,os;print(sysconfig.get_path('scripts'));print(sysconfig.get_path('scripts', os.name + '_user'))";

    /// <summary>The module that runs kasa when its program is not found (<c>python -m kasa</c> does not work: kasa is a package).</summary>
    public static readonly string[] ModuleLead = { "-m", "kasa.cli" };

    /// <summary>
    /// The kasa program installed with <paramref name="python"/>: <c>kasa.exe</c> in the folders Python named (<paramref name="scriptFolders"/>),
    /// its Scripts folder or its own folder (a virtual environment), the user's Scripts folders of any Python, or a Python of the Python install
    /// manager (<c>%LOCALAPPDATA%\Python\pythoncore-*\Scripts</c>); null when there is none yet.
    /// </summary>
    public static string? FindExe(string python, IEnumerable<string>? scriptFolders = null)
    {
        var home = Path.GetDirectoryName(python);
        var candidates = (scriptFolders ?? Enumerable.Empty<string>()).Select(f => Path.Combine(f.Trim(), KasaExe)).ToList();
        if (home is not null) candidates.AddRange(new[] { Path.Combine(home, ScriptsFolder, KasaExe), Path.Combine(home, KasaExe) });
        foreach (var root in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            var folder = Path.Combine(Environment.GetFolderPath(root), PythonFolder);
            try
            {
                if (Directory.Exists(folder))
                    candidates.AddRange(Directory.EnumerateDirectories(folder).Select(d => Path.Combine(d, ScriptsFolder, KasaExe)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable folder only means kasa is not found there.
            }
        }
        return candidates.FirstOrDefault(c => { try { return File.Exists(c); } catch (ArgumentException) { return false; } });
    }

    /// <summary>True when <paramref name="python"/> is only the <c>py</c> launcher or the Microsoft Store alias, not python.exe itself.</summary>
    public static bool IsLauncher(string python) =>
        Path.GetFileName(python).Equals("py.exe", StringComparison.OrdinalIgnoreCase) || python.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
}
