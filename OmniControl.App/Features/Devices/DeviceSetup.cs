using OmniControl.App.Shared.Scripts;

namespace OmniControl.App.Features.Devices;

/// What the scripts method needs on this PC, shared by every device: a Python, python-kasa installed for it, and the router's address
/// for the guide. Looks are read-only; <see cref="InstallAsync"/> is the one change, run as the normal user (never elevated).
public sealed class DeviceSetup
{
    private const string PythonDownloadPage = "https://www.python.org/downloads/";
    private readonly Action<string> _log;
    private string? _gateway;

    public DeviceSetup(Action<string> log) => _log = log;

    /// The installed Python that was found, or null.
    public string? PythonPath { get; private set; }

    /// python-kasa is installed for <see cref="PythonPath"/>.
    public bool KasaInstalled { get; private set; }

    /// The kasa program (and the arguments before the command) the last look found; null while python-kasa is missing.
    public (string File, string[] Lead)? Kasa { get; private set; }

    public bool Installing { get; private set; }

    /// The router's page (<c>http://192.168.0.1</c>), or null when no address was found.
    public string? RouterPage => _gateway is null ? null : $"http://{_gateway}";

    /// Raised when what was found or the installing state changed.
    public event Action? Changed;

    /// Looks again at Python, python-kasa and the router's address. The <c>py</c> launcher or the Store alias is followed to the real
    /// python.exe, Python is asked where its programs go, and kasa.exe is looked for there (and in the usual folders); when kasa is installed
    /// but its program is nowhere, <c>python -m kasa.cli</c> is used once it is seen to work.
    public async Task RefreshAsync()
    {
        _gateway = NetworkGateway.FindDefault();
        PythonPath = await Task.Run(PythonLocator.Find);
        if (PythonPath is { } found && KasaCommand.IsLauncher(found) && (await AskPythonAsync(found, KasaCommand.ExecutableQuery)).FirstOrDefault() is { } real && File.Exists(real))
            PythonPath = real;
        Kasa = null;
        if (PythonPath is { } python)
        {
            var folders = await AskPythonAsync(python, KasaCommand.ScriptsQuery);
            if (KasaCommand.FindExe(python, folders) is { } exe) Kasa = (exe, Array.Empty<string>());
            else if (await ScriptRunner.RunAsync(python, KasaCommand.ModuleLead.Append("--help").ToList(), hidden: true, Timing.DeviceProbeTimeout) is null)
                Kasa = (python, KasaCommand.ModuleLead.ToArray());
        }
        KasaInstalled = Kasa is not null;
        _log(Kasa is { } kasa ? $"kasa: {kasa.File} {string.Join(' ', kasa.Lead)} (Python {PythonPath})" : $"kasa not found (Python {PythonPath ?? "not found"})");
        Changed?.Invoke();
    }

    /// The lines Python prints for <paramref name="code"/>; empty when it could not run.
    private static async Task<IReadOnlyList<string>> AskPythonAsync(string python, string code)
    {
        var printed = string.Empty;
        var problem = await ScriptRunner.RunAsync(python, new[] { "-c", code }, hidden: true, Timing.DeviceProbeTimeout, output: text => printed = text);
        return problem is null ? printed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : Array.Empty<string>();
    }

    /// Installs python-kasa for the user (pip); null when it worked, otherwise one plain sentence. Its output goes to the devices log.
    public async Task<string?> InstallAsync()
    {
        if (PythonPath is not { } python) return "No Python was found yet.";
        if (Installing) return null;
        Installing = true;
        Changed?.Invoke();
        try
        {
            _log("installing python-kasa: pip install python-kasa");
            var problem = await ScriptRunner.RunAsync(python, new[] { "-m", "pip", "install", "python-kasa" }, hidden: true, Timing.DeviceInstallTimeout,
                output: text => _log("pip: " + text.ReplaceLineEndings(" | ")));
            if (problem is not null) _log("pip failed: " + problem.ReplaceLineEndings(" | "));
            return problem is null ? null : "python-kasa couldn't be installed.";
        }
        finally
        {
            Installing = false;
            await RefreshAsync();
        }
    }

    /// Opens the page where Python can be downloaded; null when it opened, otherwise the problem.
    public string? OpenPythonPage() => WebPage.Open(PythonDownloadPage);

    /// Opens the router's page; null when it opened, otherwise the problem.
    public string? OpenRouterPage() => RouterPage is { } page ? WebPage.Open(page) : "Couldn't find your router's address.";
}
