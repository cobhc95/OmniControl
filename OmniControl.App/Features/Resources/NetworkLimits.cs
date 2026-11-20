using StateTarget = OmniControl.App.Features.Hotkeys.StateTarget;
using OmniControl.App.Features.Hotkeys;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources;

/// The programs' network rules (upload limit, download limit, block): saved, made what Windows holds on a worker (throttled; each sync reads
/// Windows, so a crashed run's rules go at the next start), all taken out at shut-down. The work runs in the network service when it is
/// installed, else in OmniControl itself while it runs as administrator, else nowhere (the page says so). The "Packet filter" switch keeps
/// Windows Packet Filter out of all traffic while off.
public sealed class NetworkLimits : IDisposable
{
    /// The limits a chip offers, in KB/s (and the limit an "On" action uses when none was chosen).
    public static readonly IReadOnlyList<int> Choices = new[] { 64, 128, 256, 512, 1024, 2048, 5120, 10240 };
    public const int DefaultKbps = 1024;

    /// The one line the App traffic control group shows instead of its programs while no one can do the work.
    public string UnavailableLine => _canHost ? "Per-app limits and speeds need the network service, which is not installed"
        : "Per-app limits and speeds need OmniControl as administrator; the network service needs the installed version";

    /// The tooltip of every Install… of the network service.
    public const string InstallServiceTip = "Installs OmniControl's network service (Windows asks once), so per-app limits work without running OmniControl as administrator.";

    private readonly ResourcesSettings _settings;
    private readonly Action<string> _report;
    private readonly Func<string, string?> _pathOf;
    private readonly NetworkServiceClient _service;
    private readonly ThrottledRun _sync;
    private readonly List<HotkeyAction> _actions = new();
    private readonly object _gate = new(); // the runner and its syncs
    private NetworkWork? _local; // OmniControl's own work while it runs as administrator and the service is not installed
    private volatile IReadOnlyList<NetworkRule> _wanted; // made on the UI thread, read by the worker
    private volatile DownloadState _download = DownloadState.Checking;
    private volatile bool _serviceInstalled, _canHost = true; // _canHost: this copy may install the service (not portable), known after the first sync
    private bool _stopped, _ruled, _installTried, _hostChecked;
    private string? _toldFailure; // the download failure last told (worker or packet thread), told once
    private string? _toldRules; // the rules' problems last told (worker, under the lock), told once until they change or clear
    private volatile string? _installFailure; // why the last install of Windows Packet Filter failed, until one works
    private volatile bool _installing;
    private readonly object _filterGate = new(); // one attach or detach of Windows Packet Filter at a time
    private bool _filterSeen; // the packet filter switch as last applied (an import may change it)

    /// <param name="pathOf">Where a program the poller saw lives (the firewall and the service name a program by its path).</param>
    public NetworkLimits(ResourcesSettings settings, Action<string> report, Func<string, string?> pathOf)
    {
        (_settings, _report, _pathOf) = (settings, report, pathOf);
        _wanted = Wanted();
        _filterSeen = settings.PacketFilterOn;
        _service = new NetworkServiceClient(Failed, () => Changed?.Invoke(), () => _sync!.Request());
        _sync = new ThrottledRun(Timing.ResourcesRulesGap, Sync);
        _sync.Request(); // finds where the work runs and the download driver, then makes Windows hold the saved rules (taking out what a crashed run left)
        settings.Changed += Reapply; // a chip, an action, a preset or an import
        if (!_filterSeen) _ = Guard.RunAsync("Keep Windows Packet Filter off", () => Task.Run(KeepFilterOut)); // an adapter added while OmniControl was closed
    }

    /// The "Packet filter" switch: false keeps Windows Packet Filter out of all traffic (any thread).
    public bool PacketFilterOn => _settings.PacketFilterOn;

    /// The switch and its Restore defaults (UI thread): off stops download limits, then detaches the filter from every network adapter; on
    /// attaches it again. Windows asks only when OmniControl is not administrator; a refusal or failure turns the switch back and says why.
    public void SetPacketFilter(bool on)
    {
        if (on == _settings.PacketFilterOn) return;
        _settings.PacketFilterOn = on;
        _settings.Save();
        ApplyFilter();
    }

    /// Makes Windows follow the switch as it is now (a worker); the download rules follow first, so an open driver is let go before it is detached.
    private void ApplyFilter()
    {
        _filterSeen = _settings.PacketFilterOn;
        _ = Guard.RunAsync("Switch Windows Packet Filter", () => Task.Run(() =>
        {
            lock (_filterGate)
            {
                var on = _settings.PacketFilterOn;
                Sync();
                if (PacketFilterSetup.Attach(on) is { } problem)
                {
                    if (on == _settings.PacketFilterOn)
                    {
                        _settings.PacketFilterOn = _filterSeen = !on;
                        _settings.Save();
                        Sync();
                    }
                    _report(problem);
                }
                else if (on) RefreshAdapters();
            }
            Changed?.Invoke();
        }));
    }

    /// While the switch is off and OmniControl is administrator (no prompt), detaches the filter from an adapter Windows added since (a worker).
    private void KeepFilterOut()
    {
        if (_settings.PacketFilterOn || !AdministratorMode.IsElevated) return;
        lock (_filterGate)
            if (!_settings.PacketFilterOn && PacketFilterSetup.Attach(false) is { } problem) Log(new[] { problem });
    }

    /// Where the work runs changed, or the service's state (a worker).
    public event Action? Changed;

    /// True when limits and per-program speeds can work: the service is installed or OmniControl runs as administrator (any thread).
    public bool Available => _serviceInstalled || AdministratorMode.IsElevated;

    /// True when this copy may offer the network service's Install… (an installed copy, not a portable one).
    public bool CanInstallService => _canHost;

    /// What the network service does now: the status line's first part, its tooltip, a real failure, the Install… link.
    public ServiceState Service => !_serviceInstalled ? _canHost ? ServiceState.NotInstalled : ServiceState.NeedsInstalledVersion
        : _service.Problem is { } problem ? ServiceState.Failure(problem, _canHost)
        : _service.Connected ? ServiceState.Running : ServiceState.Ready;

    /// What download limits really do now (any thread).
    public DownloadState Download => _installing ? DownloadState.Installing : _download;

    /// Installs Windows Packet Filter's bundled driver (the first time a download limit needs it, or the person's retry after a failure; Windows
    /// asks only when OmniControl is not administrator), then looks for it again. Any thread.
    public void InstallPacketFilter()
    {
        if (_installing) return;
        (_installing, _installTried) = (true, true);
        _ = Guard.RunAsync("Install Windows Packet Filter", async () =>
        {
            try
            {
                if ((_installFailure = await Task.Run(PacketFilterSetup.Install).ConfigureAwait(false)) is { } failure) _report(failure);
            }
            finally
            {
                _installing = false;
                _sync.Request();
            }
        });
    }

    /// The Install… link and pill: installs the network service (one Windows prompt); a failure is said in one line. Then the work moves there.
    public async Task InstallServiceAsync()
    {
        if (await NetworkServiceMode.InstallAsync() is { } problem) _report(problem);
        _sync.Request();
    }

    /// The page came on screen: where the work runs and what the service holds are looked at again (the service may have been removed).
    public void Recheck() => _sync.Request();

    public AppNetRule? Rule(string appId) => _settings.Setup.Apps.FirstOrDefault(r => r.Id == appId);

    /// Changes one program's rule and saves; Windows follows. A rule that asks nothing and keeps no value is forgotten.
    public void Set(string appId, Action<AppNetRule> change)
    {
        var apps = _settings.Setup.Apps;
        var rule = Rule(appId);
        if (rule is null) apps.Add(rule = new AppNetRule { Id = appId });
        change(rule);
        if (rule.Path.Length == 0) rule.Path = _pathOf(appId) ?? string.Empty;
        if (rule.IsPlain && rule.UploadKbps == 0 && rule.DownloadKbps == 0) apps.Remove(rule);
        _settings.Commit();
    }

    /// The setup changed: Windows follows only when the rules themselves did.
    private void Reapply()
    {
        if (_settings.PacketFilterOn != _filterSeen) ApplyFilter();
        var wanted = Wanted();
        if (wanted.SequenceEqual(_wanted)) return;
        _wanted = wanted;
        _sync.Request();
    }

    private IReadOnlyList<NetworkRule> Wanted() => _settings.Setup.Apps
        .Where(r => (r.UploadOn && r.UploadKbps > 0) || r.Blocked || (r.DownloadOn && r.DownloadKbps > 0))
        .Select(r => new NetworkRule(r.Id, r.Path, r.UploadOn && r.UploadKbps > 0 ? (ulong)r.UploadKbps * 1024 * 8 : null, r.Blocked,
            r.DownloadOn && r.DownloadKbps > 0 ? r.DownloadKbps * 1024L : null)).ToList();

    private void Sync()
    {
        lock (_gate)
        {
            if (Runner() is not { } work) return; // nobody may change Windows' rules, and the group says so
            var wanted = _wanted;
            var filterOn = _settings.PacketFilterOn;
            var downloads = !_stopped && wanted.Any(r => r.DownloadBytes is not null);
            var installed = DownloadShaper.Installed;
            if (filterOn && downloads && !installed && !_installTried) InstallPacketFilter();
            // Before the limits reach the shaper, so a failure to open the driver is never written over.
            if (!filterOn) (_download, _toldFailure) = (DownloadState.Off, null);
            else if (downloads && installed) _download = DownloadState.Running;
            else (_download, _toldFailure) = (!installed && _installFailure is { } failure ? DownloadState.InstallFailed(failure) : DownloadState.Idle(installed), null);
            _ruled |= wanted.Count > 0;
            Tell(work.Sync(installed && filterOn ? wanted : wanted.Select(r => r with { DownloadBytes = null }).ToList()));
        }
    }

    /// Where the work runs now (under the lock, on a worker): the network service when installed, else OmniControl itself while it runs as
    /// administrator, else nowhere. Work left where it ran before is taken out of Windows.
    private INetworkWork? Runner()
    {
        var (installed, canHost) = (NetworkServiceSetup.Installed, _canHost);
        if (!_hostChecked) (_hostChecked, canHost) = (true, NetworkServiceMode.CanInstall());
        if (installed != _serviceInstalled || canHost != _canHost)
        {
            (_serviceInstalled, _canHost) = (installed, canHost);
            Changed?.Invoke();
        }
        INetworkWork? work = installed ? _service : AdministratorMode.IsElevated ? _local ??= new NetworkWork(Timing.ResourcesShaping, Failed) : null;
        if (work != _service) _service.RemoveAll();
        if (work != _local && _local is not null)
        {
            _local.StopDownloads();
            Log(_local.RemoveAll());
            _local = null;
        }
        return work;
    }

    /// Starts reading traffic per program where the work runs (the poller's worker); throws an <see cref="IOException"/> saying why when it cannot.
    public ITrafficSession StartTraffic()
    {
        INetworkWork? work;
        lock (_gate) work = Runner();
        return work?.StartTraffic() ?? throw new IOException(UnavailableLine + ".");
    }

    /// Logs the rules' problems and tells them once (until they change or clear); before any rule was set this run (only a crashed run's to
    /// take out, or no QoS on this edition) the log alone.
    private void Tell(IReadOnlyList<string> problems)
    {
        Log(problems);
        var words = problems.Count > 0 ? string.Join(" ", problems) : null;
        if (words == _toldRules) return;
        _toldRules = words;
        if (words is not null && _ruled) _report(words);
    }

    /// Shaping ended by itself (the packet thread, or an answer of the service; the driver could not open or failed, traffic already passes untouched).
    private void Failed(string reason)
    {
        _download = DownloadState.Failure(reason);
        if (reason == _toldFailure) return; // the same failure again (another change retried): told once
        _toldFailure = reason;
        _report($"Download limits not applied: {reason}. Traffic passes without a limit.");
    }

    private static void Log(IReadOnlyList<string> problems)
    {
        foreach (var problem in problems) AppDataStore.AppendLog(Guard.LogFile, $"Network limits: {problem}");
    }

    // ---- Actions: one family per kind, its devices the programs listed ----

    /// Registers Upload limit, Download limit and Block network as On / Off / Toggle families over the listed programs.
    public void RegisterActions(IActionRegistry actions, string category, Func<IReadOnlyList<HotkeyDevice>> programs)
    {
        Family(actions, category, programs, "resources.upload", "Upload limit", "upload", r => r.UploadOn, (r, on) => (r.UploadOn, r.UploadKbps) = (on, r.UploadKbps > 0 ? r.UploadKbps : DefaultKbps));
        Family(actions, category, programs, "resources.download", "Download limit", "download", r => r.DownloadOn, (r, on) => (r.DownloadOn, r.DownloadKbps) = (on, r.DownloadKbps > 0 ? r.DownloadKbps : DefaultKbps));
        Family(actions, category, programs, "resources.block", "Block network", "ban", r => r.Blocked, (r, on) => r.Blocked = on);
    }

    private void Family(IActionRegistry actions, string category, Func<IReadOnlyList<HotkeyDevice>> programs, string id, string name, string icon,
        Func<AppNetRule, bool> isOn, Action<AppNetRule, bool> set)
    {
        var family = new ActionFamily(id, name);
        void Run(IReadOnlyList<string>? ids, bool? on)
        {
            if (!Available)
            {
                _report(UnavailableLine + ".");
                return;
            }
            foreach (var app in ids ?? programs().Select(p => p.Id).ToList())
                Set(app, rule => set(rule, on ?? !isOn(rule)));
        }
        HotkeyAction Make(string suffix, string role, bool? on) => new($"{id}.{suffix}", $"{name}: {role}", category, $"network internet app program limit throttle block {name}",
            ids => Run(ids, on), "programs", programs) { Family = family, FamilyRole = role, SuggestedIcons = new[] { icon } };
        var made = new[] { Make("toggle", OnOffFamily.ToggleRole, null), Make("on", StateTarget.OnRole, true), Make("off", StateTarget.OffRole, false) };
        _actions.AddRange(made);
        actions.Register(made);
    }

    /// The listed programs changed: the actions offer them.
    public void RefreshPrograms()
    {
        foreach (var action in _actions) action.RefreshDevices();
    }

    /// Windows' devices changed or the PC woke (a worker): running download limits pick up new adapters, so a limit holds on every adapter;
    /// with the packet filter off, a new adapter has it detached.
    public void RefreshAdapters()
    {
        KeepFilterOut();
        lock (_gate) (_local ?? (INetworkWork)_service).RefreshAdapters();
    }

    /// Shut-down, on its own and first: download shaping ends (held packets go on, the driver closes) and nothing starts it again.
    public void StopDownloads()
    {
        lock (_gate)
        {
            _stopped = true;
            _local?.StopDownloads();
            _service.StopDownloads();
        }
    }

    /// Shut-down: no rule is left in Windows (runs on its own at the stage that puts Windows back).
    public void Dispose()
    {
        _settings.Changed -= Reapply;
        _sync.Dispose();
        lock (_gate)
        {
            if (_local is not null) Log(_local.RemoveAll());
            _service.RemoveAll();
        }
    }

    /// What the network service does now (the first part of the status line).
    public sealed record ServiceState(string Line, string Tip, bool Failed = false, bool OffersInstall = false)
    {
        private const string Why = "The network service lets OmniControl set per-app limits and read per-app speeds without running as administrator.";

        public static readonly ServiceState NotInstalled = new("Network service not installed", $"{Why} Installing it asks Windows once.", OffersInstall: true);

        public static readonly ServiceState Ready = new("Network service ready", $"{Why} Windows starts it when a limit or the speeds need it; it stops itself when idle.");

        public static readonly ServiceState Running = new("Network service: running", $"{Why} It takes every rule out of Windows when OmniControl closes.");

        /// A portable copy: the service runs as SYSTEM, so only from a folder only administrators can change.
        public static readonly ServiceState NeedsInstalledVersion = new("Network service needs the installed version",
            $"{Why} It runs only from OmniControl installed by Setup in Program Files, whose files only administrators can change.");

        /// It could not be reached or answered (one plain sentence); installing it again (an installed copy) points it at this copy of OmniControl.
        public static ServiceState Failure(string reason, bool offersInstall) =>
            new(reason, $"{Why} Installing it again may help; the details are in the errors log.", Failed: true, OffersInstall: offersInstall);
    }

    /// What download limits really do now: the status line, the chips' tooltip, a real failure (red), usable chips, the install offer.
    public sealed record DownloadState(string Line, string Tip, bool Failed = false, bool Usable = true, bool OffersInstall = false)
    {
        private const string Driver = "Windows Packet Filter";

        public static readonly DownloadState Checking = new($"Download limits: looking for {Driver}…", string.Empty, Usable: false);

        public static readonly DownloadState Missing = new($"Download limits install {Driver} when first set",
            $"{Driver}'s official driver comes with OmniControl; setting a download limit installs it.");

        public static readonly DownloadState Installing = new($"Installing {Driver}…",
            "Windows is adding its driver; download limits start once it is done.");

        /// The install failed (one plain sentence): the limits are kept, the retry link shows.
        public static DownloadState InstallFailed(string failure) => new(failure.TrimEnd('.'),
            "Download limits are kept but not applied until the driver is installed.", Failed: true, OffersInstall: true);

        public static readonly DownloadState Running = new($"Download limits running ({Driver})",
            $"{Driver} holds each limited program's incoming packets back to its limit; other traffic passes unchanged.");

        public static DownloadState Idle(bool installed) => installed
            ? new($"Download limits use {Driver}", $"Download limits run through {Driver}, which is opened only while a limited program has network connections.")
            : Missing;

        /// The "Packet filter" switch is off: limits are kept, not applied; the chips are greyed.
        public static readonly DownloadState Off = new("Packet filter is off",
            $"{Driver} is switched off and out of all traffic, so download limits are kept but not applied. Upload limits and blocks still work.", Usable: false);

        public static DownloadState Failure(string reason) => new($"Download limits failed: {reason}",
            $"Download limits are not applied ({reason}); traffic passes without a limit.", Failed: true);
    }
}
