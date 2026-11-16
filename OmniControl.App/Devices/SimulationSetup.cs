using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Devices;

/// <summary>
/// What the simulation method needs on this PC, shared by every device: which known emulators are installed, adb (the Android SDK's
/// platform-tools) and which adb addresses answered. Looking is read-only; opening an emulator or a download page is the only thing it
/// does, and nothing is ever downloaded or installed for the user.
/// </summary>
public sealed class SimulationSetup
{
    private readonly Action<string> _log;
    private readonly HashSet<string> _connected = new();

    public SimulationSetup(Action<string> log) => _log = log;

    /// <summary>The adb program that was found, or null.</summary>
    public string? AdbPath { get; private set; }

    /// <summary>The emulators found on this PC (the ones in <see cref="Emulators.All"/> that are installed).</summary>
    public IReadOnlyList<EmulatorInfo> Installed { get; private set; } = Array.Empty<EmulatorInfo>();

    /// <summary>Raised when what was found or a connection changed (may come from another thread).</summary>
    public event Action? Changed;

    public async Task RefreshAsync()
    {
        AdbPath = await Task.Run(Emulators.FindAdb);
        Installed = await Task.Run(() => Emulators.All.Where(e => Emulators.Find(e) is not null).ToList());
        Changed?.Invoke();
    }

    /// <summary>The emulator the device uses: the one it chose, else the first installed one; null when it has none yet.</summary>
    public EmulatorInfo? EmulatorOf(DeviceRecord device) => Emulators.Of(device.EmulatorId) ?? Installed.FirstOrDefault();

    public bool IsInstalled(EmulatorInfo emulator) => Installed.Contains(emulator);

    /// <summary>Where adb reaches the device's emulator: what was typed, else the emulator's usual address; empty when neither is known.</summary>
    public string AddressOf(DeviceRecord device) =>
        device.AdbAddress.Trim().Length > 0 ? device.AdbAddress.Trim() : EmulatorOf(device)?.DefaultAddress ?? string.Empty;

    public bool IsConnected(string address) => _connected.Contains(address);

    /// <summary>Joins adb to the emulator at the address; null when it worked, otherwise one plain sentence.</summary>
    public async Task<string?> ConnectAsync(string address)
    {
        if (AdbPath is not { } adb) return "adb isn't installed yet.";
        var result = await AdbClient.ConnectAsync(adb, address);
        _log($"adb connect {address}: {result.Text.Trim().ReplaceLineEndings(" | ")}");
        if (result.Ok) _connected.Add(address); else _connected.Remove(address);
        Changed?.Invoke();
        return result.Ok ? null : $"adb couldn't reach {address}. Is the emulator open, with adb switched on?";
    }

    /// <summary>Starts the emulator; null when it started, otherwise the problem.</summary>
    public string? Open(EmulatorInfo emulator) => Emulators.Open(emulator);

    /// <summary>Opens the emulator's official download page in the browser; null when it opened, otherwise the problem.</summary>
    public string? OpenDownloadPage(EmulatorInfo emulator) => WebPage.Open(emulator.DownloadPage);

    /// <summary>Opens the page where the Android SDK platform-tools (adb) are downloaded.</summary>
    public string? OpenAdbPage() => WebPage.Open(Emulators.PlatformToolsPage);
}
