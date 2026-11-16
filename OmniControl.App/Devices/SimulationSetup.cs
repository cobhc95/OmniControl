using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Devices;

/// <summary>
/// What the simulation method needs on this PC, shared by every device: which known emulators are installed, adb (the Android SDK's
/// platform-tools) and which adb addresses answered. Looking is read-only; opening an emulator or a download page is the only thing it
/// does, and nothing is ever downloaded or installed for the user.
/// </summary>
public sealed class SimulationSetup : IDisposable
{
    private readonly Action<string> _log;
    private IReadOnlySet<string> _connected = new HashSet<string>();
    private readonly Timer? _live;
    private int _asking;

    private readonly Timer? _recheck;
    private int _busy;

    /// <summary>Looks again every <see cref="Timing.DeviceSimRecheck"/>, so installing or uninstalling an emulator shows up without a restart.</summary>
    public SimulationSetup(Action<string> log, EmulatorKeepSettings keep, Action save)
    {
        _log = log;
        Keeper = new EmulatorKeeper(keep, () => AdbPath, save, log, () => Changed?.Invoke());
        if (StartupMode.Inert) return; // a screenshot export looks for no emulator
        _recheck = new Timer(_ => Recheck(), null, Timing.DeviceSimRecheck, Timing.DeviceSimRecheck);
        _live = new Timer(_ => _ = LookAtConnectionsAsync(), null, Timing.DeviceSimLive, Timing.DeviceSimLive);
    }

    /// <summary>Asks adb which emulators are up, so the checklist follows one opening, closing or being connected without a click.</summary>
    private async Task LookAtConnectionsAsync()
    {
        if (AdbPath is not { } adb || Interlocked.Exchange(ref _asking, 1) == 1) return;
        try
        {
            var online = await AdbClient.OnlineSerialsAsync(adb);
            if (online.SetEquals(_connected)) return;
            _connected = online;
            Changed?.Invoke();
        }
        finally { Interlocked.Exchange(ref _asking, 0); }
    }

    private void Recheck()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var adb = Emulators.FindAdb();
            var installed = Emulators.All.Where(e => Emulators.Find(e) is not null).ToList();
            if (adb == AdbPath && installed.SequenceEqual(Installed)) return;
            AdbPath = adb;
            Installed = installed;
            Changed?.Invoke();
        }
        catch (IOException ex) { _log($"Looking for emulators failed: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { _log($"Looking for emulators failed: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    public void Dispose()
    {
        _recheck?.Dispose();
        _live?.Dispose();
        Keeper.Dispose();
    }

    /// <summary>Wakes, shows and closes the emulator (the keep-running settings).</summary>
    public EmulatorKeeper Keeper { get; }

    /// <summary>The adb program that was found, or null.</summary>
    public string? AdbPath { get; private set; }

    /// <summary>The emulators found on this PC (the ones in <see cref="Emulators.All"/> that are installed).</summary>
    public IReadOnlyList<EmulatorInfo> Installed { get; private set; } = Array.Empty<EmulatorInfo>();

    /// <summary>Raised when what was found or a connection changed (may come from another thread).</summary>
    public event Action? Changed;

    public async Task RefreshAsync()
    {
        AdbPath = await Task.Run(() => Emulators.FindAdb());
        Installed = await Task.Run(() => Emulators.All.Where(e => Emulators.Find(e) is not null).ToList());
        Changed?.Invoke();
    }

    /// <summary>The emulator the simulation method uses: the one it chose, else the first installed one; null when it has none yet.</summary>
    public EmulatorInfo? EmulatorOf(MethodRecord method) => Emulators.Of(method.EmulatorId) ?? Installed.FirstOrDefault();

    public bool IsInstalled(EmulatorInfo emulator) => Installed.Contains(emulator);

    /// <summary>Where adb reaches the device's emulator: what was typed, else the emulator's usual address; empty when neither is known.</summary>
    public string AddressOf(MethodRecord method) =>
        method.AdbAddress.Trim().Length > 0 ? method.AdbAddress.Trim() : EmulatorOf(method)?.DefaultAddress ?? string.Empty;

    public bool IsConnected(string address) => _connected.Contains(address);

    /// <summary>
    /// Joins adb to the emulator at the address (a host:port is connected, a serial such as emulator-5554 is looked for in <c>adb devices</c>);
    /// starts the emulator first when it is not running and one is given (with its window when <paramref name="show"/>), telling <paramref name="progress"/> what it is doing. Null when it worked, otherwise one plain sentence.
    /// </summary>
    public async Task<string?> ConnectAsync(string address, EmulatorInfo? emulator, Action<string> progress, bool show = false)
    {
        if (AdbPath is not { } adb) return "adb isn't installed yet.";
        if (await IsUpAsync(adb, address)) return Joined(address);
        if (emulator is null) return "The emulator isn't running. Press Show to start it.";
        if (!IsInstalled(emulator)) return $"{emulator.Name} isn't installed, so it can't be started.";
        progress("Starting the emulator… (first start can take a minute or two)");
        if (await Keeper.EnsureRunningAsync(emulator, address, progress, show) is { } problem) return problem;
        if (await IsUpAsync(adb, address)) return Joined(address);
        return $"adb couldn't reach {address}. Is adb switched on in the emulator?";
    }

    private async Task<bool> IsUpAsync(string adb, string address)
    {
        var up = address.Contains(':')
            ? (await AdbClient.ConnectAsync(adb, address)).Ok
            : (await AdbClient.OnlineSerialsAsync(adb)).Contains(address);
        _log($"adb {(address.Contains(':') ? "connect" : "devices")} {address}: {(up ? "up" : "not up")}");
        return up;
    }

    private string? Joined(string address)
    {
        _connected = new HashSet<string>(_connected) { address };
        Changed?.Invoke();
        return null;
    }

    /// <summary>Opens the emulator's official download page in the browser; null when it opened, otherwise the problem.</summary>
    public string? OpenDownloadPage(EmulatorInfo emulator) => WebPage.Open(emulator.DownloadPage);

    /// <summary>Opens the page where the Android SDK platform-tools (adb) are downloaded.</summary>
    public string? OpenAdbPage() => WebPage.Open(Emulators.PlatformToolsPage);
}
