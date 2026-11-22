namespace OmniControl.App.Features.Devices;

/// What the simulation method needs on this PC, shared by every device: which known emulators are installed, adb (the Android SDK's
/// platform-tools) and which adb addresses answered. Looking is read-only; opening an emulator or a download page is the only thing it
/// does, and nothing is ever downloaded or installed for the user.
public sealed class SimulationSetup : IDisposable
{
    private readonly Action<string> _log;
    private readonly Func<bool> _hasSimulation;
    private readonly HoldCount _pageOnScreen = new();
    private IReadOnlySet<string> _connected = new HashSet<string>();
    private Timer? _live, _memoryLook;
    private bool _looked;
    private int _asking;
    private int _busy;
    private IReadOnlyDictionary<string, long> _memory = new Dictionary<string, long>();

    /// Nothing runs until it is needed (<see cref="FollowNeed"/>): adb is asked every <see cref="Timing.DeviceSimLive"/> while a device has
    /// a simulation method or the Devices page is on screen, the emulators' memory is read only while the page is on screen, and the
    /// installed emulators are looked for once, then again each time the page opens (<see cref="RefreshAsync"/>).
    /// <param name="hasSimulation">True when a device has a simulation method (its emulator state is then kept fresh).</param>
    public SimulationSetup(Action<string> log, EmulatorKeepSettings keep, Action save, Func<bool> hasSimulation)
    {
        _log = log;
        _hasSimulation = hasSimulation;
        Keeper = new EmulatorKeeper(keep, () => AdbPath, save, log, () => Changed?.Invoke());
        _pageOnScreen.Changed += FollowNeed;
        FollowNeed();
    }

    /// The Devices page is on screen until the handle is disposed (UI thread).
    public IDisposable WatchClosely() => _pageOnScreen.Take();

    /// The devices or their methods changed, or the page came or went (UI thread): the looks start or stop to match.
    public void FollowNeed()
    {
        var live = _pageOnScreen.Held || _hasSimulation();
        Switch(ref _live, live, Timing.DeviceSimLive, () => _ = LookAtConnectionsAsync());
        Switch(ref _memoryLook, live && _pageOnScreen.Held, Timing.EmulatorMemoryRefresh, LookAtMemory);
        if (!live || _looked) return;
        _looked = true;
        Guard.OffUi("Smart Devices: looking for emulators", Recheck);
    }

    private static void Switch(ref Timer? timer, bool on, TimeSpan every, Action look)
    {
        if (on == timer is not null) return;
        if (on) timer = new Timer(_ => Guard.Run("Smart Devices: emulator look", look), null, TimeSpan.Zero, every);
        else
        {
            timer!.Dispose();
            timer = null;
        }
    }

    /// The memory each installed emulator's programs hold (Core measures it from one process list), read only while the page is on screen;
    /// <see cref="Changed"/> only when a figure's shown tenth of a GB moves.
    private void LookAtMemory()
    {
        IReadOnlyDictionary<string, long> now;
        try
        {
            now = Emulators.MemoryUsed(Installed);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _log($"Reading the emulator's memory failed: {ex.Message}"); // Windows refused to list the processes; the last figures stay
            return;
        }
        var moved = now.Count != _memory.Count || now.Any(pair => !_memory.TryGetValue(pair.Key, out var before) || Tenths(before) != Tenths(pair.Value));
        _memory = now;
        if (moved) Changed?.Invoke();
    }

    private static long Tenths(long bytes) => (long)Math.Round(bytes / 107374182.4);

    /// The memory the emulator held at the last look, in bytes (0 when it is not running).
    public long MemoryOf(EmulatorInfo emulator) => _memory.TryGetValue(emulator.Id, out var bytes) ? bytes : 0;

    /// Asks adb which emulators are up, so the checklist follows one opening, closing or being connected without a click.
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
        _pageOnScreen.Changed -= FollowNeed;
        _live?.Dispose();
        _memoryLook?.Dispose();
        Keeper.Dispose();
    }

    /// Wakes, shows and closes the emulator (the keep-running settings).
    public EmulatorKeeper Keeper { get; }

    /// The adb program that was found, or null.
    public string? AdbPath { get; private set; }

    /// The emulators found on this PC (the ones in <see cref="Emulators.All"/> that are installed).
    public IReadOnlyList<EmulatorInfo> Installed { get; private set; } = Array.Empty<EmulatorInfo>();

    /// Raised when what was found or a connection changed (may come from another thread).
    public event Action? Changed;

    public async Task RefreshAsync()
    {
        AdbPath = await Task.Run(() => Emulators.FindAdb());
        Installed = await Task.Run(() => Emulators.All.Where(e => Emulators.Find(e) is not null).ToList());
        Changed?.Invoke();
    }

    /// The emulator the simulation method uses: the one it chose, else the first installed one; null when it has none yet.
    public EmulatorInfo? EmulatorOf(MethodRecord method) => Emulators.Of(method.EmulatorId) ?? Installed.FirstOrDefault();

    public bool IsInstalled(EmulatorInfo emulator) => Installed.Contains(emulator);

    /// Where adb reaches the device's emulator: what was typed, else the emulator's usual address; empty when neither is known.
    public string AddressOf(MethodRecord method) =>
        method.AdbAddress.Trim().Length > 0 ? method.AdbAddress.Trim() : EmulatorOf(method)?.DefaultAddress ?? string.Empty;

    /// Hides the method's emulator window again once its set-up is closed; the emulator keeps running.
    public Task HideWindowAsync(MethodRecord method) =>
        EmulatorOf(method) is { } emulator ? Keeper.HideWindowAsync(emulator, AddressOf(method)) : Task.CompletedTask;

    public bool IsConnected(string address) => _connected.Contains(address);

    /// Joins adb to the emulator at the address (a host:port is connected, a serial such as emulator-5554 is looked for in <c>adb devices</c>);
    /// starts the emulator first when it is not running and one is given (with its window when <paramref name="show"/>), telling <paramref name="progress"/> what it is doing. Null when it worked, otherwise one plain sentence.
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

    /// Opens the emulator's official download page in the browser; null when it is being opened, otherwise the problem (a later failure goes to <paramref name="report"/>).
    public string? OpenDownloadPage(EmulatorInfo emulator, Action<string> report) => WebPage.Open(emulator.DownloadPage, report);

    /// Opens the page where the Android SDK platform-tools (adb) are downloaded.
    public string? OpenAdbPage(Action<string> report) => WebPage.Open(Emulators.PlatformToolsPage, report);
}
