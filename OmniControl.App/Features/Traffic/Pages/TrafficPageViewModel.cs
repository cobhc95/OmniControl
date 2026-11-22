using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Resources;

namespace OmniControl.App.Features.Traffic.Pages;

/// The Network traffic control page: one group with the Packet filter switch and the Apps chip on its header, a row per program (speeds,
/// limits, block) and the one status line; it reads the programs' traffic only while on screen with the group open.
public sealed class TrafficPageViewModel : PageViewModel, IDisposable
{
    private readonly TrafficMonitor _monitor;
    private readonly ResourcesSettings _settings;
    private readonly INetworkLimits _limits;
    private readonly AppScopeGate _gate;
    private readonly ProcessIcons _icons = new();
    private readonly Action<string> _report;
    private bool _onScreen;
    private int _seenShown = -1;
    private NetworkLimits.DownloadState? _downloadShown;

    public const string PageTitle = "Network traffic control";

    /// The sidebar's filled drawing of the icon library's "arrows-up-down" (traffic up and down).
    public static readonly PageInfo Info = new(PageTitle, "M7,3 L12,8 H8.5 V21 H5.5 V8 H2 Z M17,21 L12,16 H15.5 V3 H18.5 V16 H22 Z",
        NavGroups.System, "network traffic internet upload download speed limit throttle block firewall packet filter app program bandwidth");

    public CardGroup Apps { get; }
    public AppScopeModel AppsScope { get; }
    public ObservableCollection<AppNetItem> AppRows { get; } = new();
    public bool HasApps => AppRows.Count > 0;

    /// The Packet filter switch on the group's header: off takes Windows Packet Filter out of all traffic (for a conflict with other software).
    public ToggleItem PacketFilter { get; }
    private const string FilterKeywords = "packet filter windows packet filter winpkfilter ndisrd driver network adapter download limit conflict vpn tailscale off";

    /// The one status line under the programs: the network service, then what really runs for each kind of rule (red while the service or
    /// download limits have failed).
    public string Status => $"{_limits.Service.Line} · {_limits.Download.Line} · Upload limits use Windows' throttle rules · Blocks use Windows Firewall"
        + (_monitor.Problem is { } problem ? $" · {problem}" : string.Empty);

    public string StatusTip => $"{_limits.Service.Tip} {_limits.Download.Tip}".Trim();

    public bool StatusFailed => _limits.Service.Failed || _limits.Download.Failed;

    /// The status line's "Install Windows Packet Filter…" link shows only after its install failed, as a retry.
    public bool OffersInstall => _limits.Download.OffersInstall;

    public IRelayCommand InstallPacketFilterCommand { get; }

    /// The status line's network service "Install…" link: not installed, or failing.
    public bool OffersServiceInstall => _limits.Service.OffersInstall;

    /// Neither the network service nor OmniControl as administrator can do the work: the group shows only <see cref="UnavailableLine"/> and,
    /// on an installed copy, Install….
    public bool Unavailable => !_limits.Available;
    public string UnavailableLine => _limits.UnavailableLine;
    public bool CanInstallService => _limits.CanInstallService;
    public IRelayCommand InstallServiceCommand { get; }
    public string InstallServiceTip => NetworkLimits.InstallServiceTip;

    public override IEnumerable<SearchEntry> SearchEntries => new[] { Entry(PacketFilter, PacketFilter.Label, FilterKeywords, Apps.Name) };

    internal TrafficPageViewModel(TrafficMonitor monitor, ResourcesSettings settings, INetworkLimits limits, AppScopes scopes, AppScopeGate gate,
        Action<string> report) : base(Info)
    {
        (_monitor, _settings, _limits, _gate, _report) = (monitor, settings, limits, gate, report);
        InstallPacketFilterCommand = new RelayCommand(() =>
        {
            limits.InstallPacketFilter();
            ShowStatus();
        });
        InstallServiceCommand = new AsyncRelayCommand(() => Guard.RunAsync("Install the network service", limits.InstallServiceAsync));
        limits.Changed += OnLimitsChanged;

        Apps = new CardGroup("Resources/Apps", PageTitle, string.Empty)
        {
            ExpandedByDefault = true,
            Restore = () => limits.SetPacketFilter(true),
            RestoreTip = "Turns Windows Packet Filter back on.",
        };
        AppsScope = scopes.Group(PageTitle, () => settings.Scope, scope =>
        {
            settings.Scope = scope;
            settings.Save();
            ShowApps();
        });
        Apps.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CardGroup.IsExpanded)) return;
            Need(); // per-program traffic is read only while the group is open
            if (Apps.IsExpanded) ShowApps();
        };
        PacketFilter = new ToggleItem("Packet filter", limits.PacketFilterOn, limits.SetPacketFilter)
        {
            Tip = "Download limits use Windows Packet Filter. Turn it off if another program has network trouble.",
        };

        settings.Changed += OnSettingsChanged;
        monitor.Updated += OnUpdated;
        ShowApps();
    }

    private void OnSettingsChanged()
    {
        Need();
        ShowApps();
    }

    public override void OnOnScreenChanged(bool onScreen)
    {
        _onScreen = onScreen;
        Need();
        if (!onScreen) return;
        OnUpdated();
        _limits.Recheck();
    }

    /// The programs' traffic is read at the Resources page's interval while this page is on screen with the group open.
    private void Need() =>
        _monitor.Need(_onScreen && Apps.IsExpanded && !Unavailable ? TimeSpan.FromSeconds(_settings.Setup.ReadEverySeconds) : null);

    private void OnUpdated()
    {
        if (!_onScreen) return;
        if (Apps.IsExpanded) ShowTraffic();
        ShowStatus();
    }

    /// One row per program pinned, seen this session or holding a rule, inside the Apps scope: the pinned first in pin order, then the rest
    /// in the order first seen, each showing its rule as saved.
    private void ShowApps()
    {
        ListRows();
        foreach (var row in AppRows) row.Load();
        _downloadShown = _limits.Download;
        ShowTraffic();
        ShowStatus();
    }

    private void ListRows()
    {
        _seenShown = _monitor.Seen.Count;
        var pins = _settings.Setup.Pinned.ToLookup(ProgramIdentity.Normalize);
        var ids = pins.Select(p => p.Key).Concat(_monitor.Seen.Keys).Concat(_settings.Setup.Apps.Select(r => r.Id)).Distinct()
            .Where(id => _gate.Allows(_settings.Scope, id)).ToList();
        var known = AppRows.ToDictionary(r => r.Id);
        ListDiff.Apply(AppRows, ids.Select(id => known.GetValueOrDefault(id) ?? new AppNetItem(id, _limits, _icons, TogglePin, _report)).ToList(), r => r.Id);
        foreach (var row in AppRows)
        {
            row.IsPinned = pins.Contains(row.Id);
            row.KnowPath(PathOf(row.Id, pins[row.Id]));
        }
        OnPropertyChanged(nameof(HasApps));
    }

    /// Where a program lives: as the reader saw it, else as its rule or its pin keeps it; empty when not known.
    private string PathOf(string id, IEnumerable<string> pinned) =>
        _monitor.Seen.GetValueOrDefault(id) is { Length: > 0 } seen ? seen
        : _limits.Rule(id)?.Path is { Length: > 0 } ruled ? ruled
        : pinned.FirstOrDefault(Path.IsPathFullyQualified) ?? string.Empty;

    /// Pins a row to the top by its program's path (its identity while the path is not known), or unpins it.
    private void TogglePin(AppNetItem row)
    {
        var pins = _settings.Setup.Pinned;
        if (pins.RemoveAll(p => ProgramIdentity.Normalize(p) == row.Id) == 0) pins.Add(row.Path.Length > 0 ? row.Path : row.Id);
        _settings.Commit();
    }

    /// Every read while the group is open: new programs get rows, rows their speeds, download chips follow a change of what limits really do.
    private void ShowTraffic()
    {
        if (_monitor.Seen.Count != _seenShown) ListRows();
        if (_limits.Download != _downloadShown)
        {
            _downloadShown = _limits.Download;
            foreach (var row in AppRows) row.Load();
        }
        var traffic = _monitor.Apps.ToDictionary(a => a.Id);
        foreach (var row in AppRows)
        {
            row.NotRunning = row.IsPinned && !_monitor.Seen.ContainsKey(row.Id);
            var now = traffic.GetValueOrDefault(row.Id);
            (row.Down, row.Up) = row.NotRunning ? (Reading.Unknown, Reading.Unknown) : (Reading.SpeedWords(now?.Received ?? 0), Reading.SpeedWords(now?.Sent ?? 0));
        }
    }

    private void ShowStatus()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusTip));
        OnPropertyChanged(nameof(StatusFailed));
        OnPropertyChanged(nameof(OffersInstall));
        OnPropertyChanged(nameof(OffersServiceInstall));
    }

    /// Where the network work runs, or the service's state, changed (a worker): the group and its status line follow.
    private void OnLimitsChanged() => Dispatcher.UIThread.Post(() =>
    {
        PacketFilter.Load(_limits.PacketFilterOn);
        foreach (var row in AppRows) row.Load(); // the download chips follow the switch
        OnPropertyChanged(nameof(Unavailable));
        OnPropertyChanged(nameof(UnavailableLine));
        OnPropertyChanged(nameof(CanInstallService));
        Need();
        ShowStatus();
    });

    public void Dispose()
    {
        _limits.Changed -= OnLimitsChanged;
        _settings.Changed -= OnSettingsChanged;
        _monitor.Updated -= OnUpdated;
        _monitor.Need(null);
    }
}
