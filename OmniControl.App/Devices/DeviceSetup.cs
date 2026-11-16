using OmniControl.App.Rules;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Devices;

/// <summary>
/// What the scripts method needs on this PC, shared by every device: a Python, python-kasa installed for it, and the router's address
/// for the guide. Looks are read-only; <see cref="InstallAsync"/> is the one change, run as the normal user (never elevated).
/// </summary>
public sealed class DeviceSetup
{
    private const string PythonDownloadPage = "https://www.python.org/downloads/";
    private readonly Action<string> _log;
    private string? _gateway;

    public DeviceSetup(Action<string> log) => _log = log;

    /// <summary>The installed Python that was found, or null.</summary>
    public string? PythonPath { get; private set; }

    /// <summary>python-kasa is installed for <see cref="PythonPath"/>.</summary>
    public bool KasaInstalled { get; private set; }

    /// <summary>The kasa program (and the arguments before the command) the last look found; null while python-kasa is missing.</summary>
    public (string File, string[] Lead)? Kasa { get; private set; }

    public bool Installing { get; private set; }

    /// <summary>The router's page (<c>http://192.168.0.1</c>), or null when no address was found.</summary>
    public string? RouterPage => _gateway is null ? null : $"http://{_gateway}";

    public string PythonPage => PythonDownloadPage;

    /// <summary>Raised when what was found or the installing state changed.</summary>
    public event Action? Changed;

    /// <summary>Looks again at Python, python-kasa and the router's address.</summary>
    public async Task RefreshAsync()
    {
        _gateway = NetworkGateway.FindDefault();
        PythonPath = await Task.Run(PythonLocator.Find);
        Kasa = null;
        if (PythonPath is { } python)
        {
            if (KasaCommand.FindExe(python) is { } exe) Kasa = (exe, Array.Empty<string>());
            else if (await ScriptRunner.RunAsync(python, new[] { "-c", "import kasa" }, hidden: true, Timing.DeviceProbeTimeout) is null)
                Kasa = (python, new[] { "-m", "kasa" }); // installed, but its program is not in a place we know
        }
        KasaInstalled = Kasa is not null;
        Changed?.Invoke();
    }

    /// <summary>Installs python-kasa for the user (pip); null when it worked, otherwise one plain sentence. Its output goes to the devices log.</summary>
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

    /// <summary>Opens the page where Python can be downloaded; null when it opened, otherwise the problem.</summary>
    public string? OpenPythonPage() => WebPage.Open(PythonDownloadPage);

    /// <summary>Opens the router's page; null when it opened, otherwise the problem.</summary>
    public string? OpenRouterPage() => RouterPage is { } page ? WebPage.Open(page) : "Couldn't find your router's address.";
}
