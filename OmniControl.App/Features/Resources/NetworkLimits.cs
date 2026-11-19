using StateTarget = OmniControl.App.Features.Hotkeys.StateTarget;
using OmniControl.App.Features.Hotkeys;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources;

/// The programs' network rules (upload limit, download limit, block): saved, made what Windows holds on a worker (throttled; each sync reads
/// Windows, so a crashed run's rules go at the next start), all taken out at shut-down.
public sealed class NetworkLimits : IDisposable
{
    /// The limits a chip offers, in KB/s (and the limit an "On" action uses when none was chosen).
    public static readonly IReadOnlyList<int> Choices = new[] { 64, 128, 256, 512, 1024, 2048, 5120, 10240 };
    public const int DefaultKbps = 1024;

    private static readonly ShapingTimes Shaping = new(Timing.ResourcesShapeBurst, Timing.ResourcesShapeQueue, Timing.ResourcesShapePorts,
        Timing.ResourcesShapePortsIdle, Timing.ResourcesShapeStopWait);

    private readonly ResourcesSettings _settings;
    private readonly Action<string> _report;
    private readonly Func<string, string?> _pathOf;
    private readonly NetworkRules _windows = new();
    private readonly ThrottledRun _sync;
    private readonly List<HotkeyAction> _actions = new();
    private volatile IReadOnlyList<NetworkRule> _wanted; // made on the UI thread, read by the worker
    private volatile IReadOnlyList<DownloadLimit> _downloads;
    private volatile DownloadState _download = DownloadState.Checking;
    private DownloadShaper? _shaper; // the worker's, under the lock
    private bool _stopped, _ruled, _installTried;
    private string? _toldFailure; // the download failure last told (worker or packet thread), told once
    private string? _toldRules; // the rules' problems last told (worker, under the lock), told once until they change or clear
    private volatile string? _installFailure; // why the last install of Windows Packet Filter failed, until one works
    private volatile bool _installing;

    /// The one line the Network by app group shows instead of its programs while OmniControl does not run as administrator.
    public const string NeedsAdministratorLine = "Network limits need OmniControl to run as administrator";

    /// True while OmniControl does not run as administrator: no limit, block, download limit or per-program speed is read or put into Windows.
    public static bool NeedsAdministrator => !AdministratorMode.IsElevated;

    /// <param name="pathOf">Where a program the poller saw lives (the firewall names a program by its path).</param>
    public NetworkLimits(ResourcesSettings settings, Action<string> report, Func<string, string?> pathOf)
    {
        (_settings, _report, _pathOf) = (settings, report, pathOf);
        (_wanted, _downloads) = (Wanted(), Downloads());
        _sync = new ThrottledRun(Timing.ResourcesRulesGap, Sync);
        _sync.Request(); // finds the download driver, then makes Windows hold the saved rules (taking out what a crashed run left)
        settings.Changed += Reapply; // a chip, an action, a preset or an import
    }

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

    /// "Restart as administrator" of the Network by app group; a refused prompt is said in one line.
    public async Task RestartAsAdministratorAsync()
    {
        if (await AdministratorMode.RestartElevatedAsync() is { } problem) _report(problem);
    }

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
        var (wanted, downloads) = (Wanted(), Downloads());
        if (wanted.SequenceEqual(_wanted) && downloads.SequenceEqual(_downloads)) return;
        (_wanted, _downloads) = (wanted, downloads);
        _sync.Request();
    }

    private IReadOnlyList<NetworkRule> Wanted() => _settings.Setup.Apps.Where(r => (r.UploadOn && r.UploadKbps > 0) || r.Blocked)
        .Select(r => new NetworkRule(r.Id, r.Path, r.UploadOn && r.UploadKbps > 0 ? (ulong)r.UploadKbps * 1024 * 8 : null, r.Blocked)).ToList();

    private IReadOnlyList<DownloadLimit> Downloads() => _settings.Setup.Apps.Where(r => r.DownloadOn && r.DownloadKbps > 0)
        .Select(r => new DownloadLimit(r.Id, r.DownloadKbps * 1024L)).ToList();

    private void Sync()
    {
        if (NeedsAdministrator) return; // Windows would refuse every rule, and the group says so
        lock (_windows)
        {
            ShapeDownloads(); // first: it is quick, and the page's chips wait for it
            var wanted = _wanted;
            _ruled |= wanted.Count > 0;
            Tell(_windows.Sync(wanted));
        }
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

    /// Runs the shaper while a download limit is set and Windows Packet Filter is installed (worker, under the lock); a shaper that ended is
    /// let go. The first limit set without the driver (or one saved from before) installs it, once a run; after a failure only the retry does.
    private void ShapeDownloads()
    {
        if (_stopped) return;
        if (_shaper is { Running: false }) StopShaper();
        var limits = _downloads;
        var installed = DownloadShaper.Installed;
        if (limits.Count == 0 || !installed)
        {
            StopShaper();
            if (limits.Count > 0 && !installed && !_installTried) InstallPacketFilter();
            (_download, _toldFailure) = (!installed && _installFailure is { } failure ? DownloadState.InstallFailed(failure) : DownloadState.Idle(installed), null);
            return;
        }
        if (_shaper is null)
        {
            _download = DownloadState.Running; // before the limits reach it, so a failure to open the driver is never written over
            _shaper = DownloadShaper.Start(Shaping, Failed);
        }
        _shaper.SetLimits(limits);
    }

    /// Shaping ended by itself (the packet thread; the driver could not open or failed, traffic already passes untouched).
    private void Failed(string reason)
    {
        _download = DownloadState.Failure(reason);
        if (reason == _toldFailure) return; // the same failure again (another change retried): told once
        _toldFailure = reason;
        _report($"Download limits not applied: {reason}. Traffic passes without a limit.");
    }

    private void StopShaper()
    {
        _shaper?.Dispose();
        _shaper = null;
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
            if (NeedsAdministrator)
            {
                _report(NeedsAdministratorLine + ".");
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

    /// Windows' devices changed or the PC woke (a worker): a running shaper picks up new adapters, so a limit holds on every adapter.
    public void RefreshAdapters()
    {
        lock (_windows) _shaper?.RefreshAdapters();
    }

    /// Shut-down, on its own and first: download shaping ends (held packets go on, the driver closes) and nothing starts it again.
    public void StopDownloads()
    {
        lock (_windows)
        {
            _stopped = true;
            StopShaper();
        }
    }

    /// Shut-down: no rule is left in Windows (runs on its own at the stage that puts Windows back); skipped when the last sync proved none.
    public void Dispose()
    {
        _settings.Changed -= Reapply;
        _sync.Dispose();
        lock (_windows)
        {
            if (!NeedsAdministrator && !_windows.HoldsNone) Log(_windows.RemoveAll());
        }
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
            ? new($"Download limits use {Driver}", $"Download limits run through {Driver}, which OmniControl opens only while a limited program has network connections.")
            : Missing;

        public static DownloadState Failure(string reason) => new($"Download limits failed: {reason}",
            $"Download limits are not applied ({reason}); traffic passes without a limit.", Failed: true);
    }
}
