using System.ComponentModel;
using System.Globalization;
using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Resources.Gadget;
using OmniControl.App.Shared.Presets;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources.Pages;

/// The Resources page: preset picker and two tabs, Readings (Live readings, Network traffic control) and Taskbar gadget (<see cref="GadgetSettingsModel"/>);
/// it asks the poller for readings only while on screen.
public sealed class ResourcesPageViewModel : PageViewModel, IDisposable
{
    private readonly ResourceMonitor _monitor;
    private readonly ResourcesSettings _settings;
    private readonly NetworkLimits? _limits; // null while Network traffic control is off: no Network traffic control group
    private readonly AppScopeGate _gate;
    private readonly ProviderChoice? _presets; // null with _gadget while the Resources feature is off (Network traffic control alone)
    private readonly Dictionary<string, LiveCard> _cards = new(); // by the id of the reading each shows; made once, kept while its reading is there
    private readonly ObservableCollection<object> _liveItems = new();
    private readonly NetworkAdapterMenu _adapter;
    private readonly SensorMenu _hottest;
    private readonly GadgetSettingsModel? _gadget;
    private readonly ProcessIcons _icons = new();
    private readonly Action<string> _report;
    private bool _onScreen, _listAdapters;
    private int _seenShown = -1;
    private NetworkLimits.DownloadState? _downloadShown;

    public const string PageTitle = "Resources", ReadingsTab = "Readings", GadgetTab = "Taskbar gadget", NetworkGroupName = "Network traffic control";

    public static readonly PageInfo Info = new(PageTitle, "M4,20 H20 V22 H4 Z M5,12 H8 V19 H5 Z M10.5,6 H13.5 V19 H10.5 Z M16,9 H19 V19 H16 Z", NavGroups.System,
        "resources cpu gpu memory ram disk temperature network upload download speed traffic taskbar gadget limit throttle block firewall packet filter");

    public override bool ShowsOwnHeader => true; // the preset picker sits on the title line

    /// The Resources feature's parts of the page: the title line's preset picker and the Taskbar gadget tab (the page disposes both).
    internal sealed record ReadingParts(ProviderChoice Presets, GadgetSettingsModel Gadget);

    public ChoiceItem? PresetChoice => _presets?.Choice;
    public override PageTabs Tabs { get; }

    /// The Resources feature runs: Live readings, the preset picker and the Taskbar gadget tab show.
    public bool ShowsLive => _gadget is not null;
    public bool ShowsReadings => Tabs.Current == ReadingsTab;

    /// The Taskbar gadget tab's content once opened (null before, so its view is built only when first wanted), then kept and hidden with the tab.
    public GadgetSettingsModel? Gadget => _gadgetBuilt ? _gadget : null;
    private bool _gadgetBuilt;
    public ItemGroup Live { get; }
    public ChoiceItem ReadEvery { get; }
    public CardGroup Apps { get; }
    public AppScopeModel AppsScope { get; }
    public ObservableCollection<AppNetItem> AppRows { get; } = new();
    public bool HasApps => AppRows.Count > 0;

    /// Network traffic control runs: its group shows.
    public bool ShowsTraffic => _limits is not null;

    /// The Packet filter switch on the Network traffic control group's header: off takes Windows Packet Filter out of all traffic (for a conflict with other software).
    public ToggleItem PacketFilter { get; }
    private const string FilterKeywords = "packet filter windows packet filter winpkfilter ndisrd driver network adapter download limit conflict vpn tailscale off";

    /// The one status line under the programs: the network service, then what really runs for each kind of rule (red while the service or
    /// download limits have failed).
    public string Status => _limits is not { } limits ? string.Empty
        : $"{limits.Service.Line} · {limits.Download.Line} · Upload limits use Windows' throttle rules · Blocks use Windows Firewall"
        + (_monitor.AppsProblem is { } problem ? $" · {problem}" : string.Empty);

    public string StatusTip => _limits is { } limits ? $"{limits.Service.Tip} {limits.Download.Tip}".Trim() : string.Empty;

    public bool StatusFailed => _limits is { } limits && (limits.Service.Failed || limits.Download.Failed);

    /// The status line's "Install Windows Packet Filter…" link shows only after its install failed, as a retry.
    public bool OffersInstall => _limits?.Download.OffersInstall == true;

    public IRelayCommand InstallPacketFilterCommand { get; }

    /// The status line's network service "Install…" link: not installed, or failing.
    public bool OffersServiceInstall => _limits?.Service.OffersInstall == true;

    /// Neither the network service nor OmniControl as administrator can do the work: the group shows only <see cref="UnavailableLine"/> and,
    /// on an installed copy, Install….
    public bool Unavailable => _limits is { Available: false };
    public string UnavailableLine => _limits?.UnavailableLine ?? string.Empty;
    public bool CanInstallService => _limits?.CanInstallService == true;
    public IRelayCommand InstallServiceCommand { get; }
    public string InstallServiceTip => NetworkLimits.InstallServiceTip;

    public override IEnumerable<SearchEntry> SearchEntries =>
        (ShowsLive ? new[] { Entry(ReadEvery, ReadEvery.Label, "resources readings") } : Array.Empty<SearchEntry>())
            .Concat(ShowsTraffic ? new[] { Entry(PacketFilter, PacketFilter.Label, FilterKeywords, Apps.Name) } : Array.Empty<SearchEntry>())
            .Concat((_gadget?.SearchItems ?? Enumerable.Empty<IEditorItem>()).Select(i => Entry(i, i.Label, "resources taskbar gadget", GadgetTab)));

    /// <param name="limits">Network traffic control; null while it is off.</param>
    /// <param name="readings">The Resources feature's parts; null while it is off.</param>
    internal ResourcesPageViewModel(ResourceMonitor monitor, ResourcesSettings settings, NetworkLimits? limits, ReadingParts? readings,
        AppScopes scopes, AppScopeGate gate, Action<string> report) : base(Info)
    {
        (_monitor, _settings, _limits, _gate, _presets, _gadget, _report) = (monitor, settings, limits, gate, readings?.Presets, readings?.Gadget, report);
        Tabs = readings is null ? new(ReadingsTab) : new(ReadingsTab, GadgetTab);
        Tabs.PropertyChanged += OnTabChanged;
        InstallPacketFilterCommand = new RelayCommand(() =>
        {
            limits?.InstallPacketFilter();
            ShowStatus();
        });
        InstallServiceCommand = new AsyncRelayCommand(() => limits is null ? Task.CompletedTask : Guard.RunAsync("Install the network service", limits.InstallServiceAsync));
        if (limits is not null) limits.Changed += OnLimitsChanged;

        _adapter = new NetworkAdapterMenu(monitor, settings, open =>
        {
            _listAdapters = open;
            Need();
        });
        _hottest = new SensorMenu(monitor, settings, Reading.Find(Reading.HottestId)!, "Hottest (auto)", null);
        ListCards();
        Live = new ItemGroup("Resources/Live", "Live readings", _liveItems)
        {
            ExpandedByDefault = true, Summary = string.Empty,
            Tip = "Readings come from Windows itself (GPU and disk load as Task Manager shows them, the graphics card's and drives' own temperatures); LibreHardwareMonitor for the CPU package temperature (it needs the PawnIO driver, else the ACPI thermal zone is read) and for what Windows does not give.",
        };
        ReadEvery = new ChoiceItem("Read every", Timing.ResourcesReadChoices.Select(Every).ToList(), Every(settings.Setup.ReadEverySeconds), picked =>
        {
            settings.Setup.ReadEverySeconds = Timing.ResourcesReadChoices.First(s => Every(s) == picked);
            settings.Commit();
        });

        Apps = new CardGroup("Resources/Apps", NetworkGroupName, string.Empty)
        {
            ExpandedByDefault = true,
            Restore = () => limits?.SetPacketFilter(true),
            RestoreTip = "Turns Windows Packet Filter back on.",
        };
        AppsScope = scopes.Group(NetworkGroupName,() => settings.Scope, scope =>
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
        PacketFilter = new ToggleItem("Packet filter", limits?.PacketFilterOn == true, on => limits?.SetPacketFilter(on))
        {
            Tip = "Download limits use Windows Packet Filter. Turn it off if another program has network trouble.",
        };

        settings.Changed += OnSettingsChanged;
        monitor.Updated += OnUpdated;
        monitor.ReadingsChanged += OnReadingsChanged;
        ShowApps();
    }

    /// A Live readings card, the reading it shows, and the temperature reading its sensor chip is for and that chip's menu.
    private sealed record LiveCard(ReadingCard Card, Reading Reading, Reading? TemperatureReading, SensorMenu? Temperature);

    /// The cards in reading order (each graphics adapter's in the GPU card's place while there are several, the disks' last); a card whose
    /// reading stays keeps its graph.
    private void ListCards()
    {
        var readings = _monitor.Readings.Where(r => r.Card is not null).OrderBy(r => r.Card!.Value.Order).ToList();
        var eachGpu = readings.Any(r => r.Part.HasFlag(SampleParts.EachGpu));
        var shown = readings.Where(r => !(eachGpu && r.Id == Reading.GpuId)).ToList();
        foreach (var gone in _cards.Keys.Where(id => shown.All(r => r.Id != id)).ToList()) _cards.Remove(gone);
        ListDiff.Apply(_liveItems, shown.Select(r => (object)(_cards.TryGetValue(r.Id, out var known) ? known : _cards[r.Id] = Card(r)).Card).ToList(), c => c);
    }

    /// The CPU, GPU and disk cards carry their temperature as its sensor chip (<see cref="Reading.TemperatureId"/>).
    private LiveCard Card(Reading r)
    {
        if (r.Id == Reading.HottestId)
            return new(new ReadingCard(r.Card!.Value.Label, r.Icon, Reading.TemperatureGraphMax, Reading.TemperatureGraphMin)
                { Sensor = _hottest.Item, FootGraph = true, WarnAt = Reading.WarmCelsius }, r, null, null);
        var t = r.TemperatureId is { } id ? _monitor.Find(id) : null;
        var temperature = t is null ? null : new SensorMenu(_monitor, _settings, t, "Auto", _ => t.Words(t.Read(_monitor.Latest)), SensorMenu.TipFor(t));
        return new(new ReadingCard(r.Card!.Value.Label, r.Icon, r.GraphMax) { Adapter = r.Kind == ReadingKind.Speed ? _adapter.Item : null, Sensor = temperature?.Item },
            r, t, temperature);
    }

    private IEnumerable<SensorMenu> SensorMenus => _cards.Values.Select(c => c.Temperature).OfType<SensorMenu>().Prepend(_hottest);

    private void OnReadingsChanged()
    {
        ListCards();
        OnUpdated();
    }

    private static string Every(double seconds) => $"Read every {seconds.ToString("0.#", CultureInfo.CurrentCulture)} s";

    /// A card's value, sparkline and fact (memory used of total, the hottest sensor's title and hardware); a disk's sensor chip only while
    /// its drive reports a temperature or a sensor is chosen for it.
    private void Show(LiveCard live, ResourceSample s)
    {
        var (card, reading) = (live.Card, live.Reading);
        if (reading.Part == SampleParts.Disk && live.TemperatureReading is { } temperature)
            card.SensorShown = live.Temperature!.Chosen is not null || temperature.Read(s) is not null;
        card.Value = reading.Id == "memory" && s.MemoryUsedGb is { } used && s.MemoryTotalGb is { } total ? $"{used:0.0} / {total:0} GB" : reading.Words(reading.Read(s));
        card.History = _monitor.History(reading.Id);
        card.Tag = reading.Id == "memory" && s.MemoryPercent is { } p ? new TagItem(reading.Words(p)) : null;
        if (reading.Id != Reading.HottestId) return;
        var chosen = _hottest.Chosen?.Name;
        card.Label = chosen ?? reading.Card!.Value.Label;
        card.Note = s.HottestName is { } hardware && hardware != chosen ? hardware : _monitor.SensorProblem ?? string.Empty;
    }

    private void OnSettingsChanged()
    {
        ReadEvery.Load(ReadEvery.Options, Every(_settings.Setup.ReadEverySeconds));
        _adapter.Refresh();
        foreach (var menu in SensorMenus) menu.Refresh();
        Need();
        ShowApps();
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageTabs.Current)) return;
        OnPropertyChanged(nameof(ShowsReadings));
        Need();
        if (ShowsReadings && _onScreen) OnUpdated();
        // The tab strip shows the change first; the gadget tab's view is built once, right after that frame.
        if (!ShowsReadings && !_gadgetBuilt) Dispatcher.UIThread.Post(() =>
        {
            if (!ShowsReadings) SetGadgetBuilt(true);
        }, DispatcherPriority.Background);
    }

    private void SetGadgetBuilt(bool built)
    {
        if (built == _gadgetBuilt) return;
        _gadgetBuilt = built;
        OnPropertyChanged(nameof(Gadget));
    }

    public override void OnOnScreenChanged(bool onScreen)
    {
        _onScreen = onScreen;
        if (!onScreen && ShowsReadings) SetGadgetBuilt(false); // let the hidden gadget view go with the page
        Need();
        if (!onScreen) return;
        OnUpdated();
        _limits?.Recheck();
    }

    /// The readings tab asks the poller at the page's interval while it is on screen; the gadget tab holds the gadget's own reads.
    private void Need()
    {
        var every = _onScreen && ShowsReadings ? TimeSpan.FromSeconds(_settings.Setup.ReadEverySeconds) : (TimeSpan?)null;
        _monitor.Need(ResourceMonitor.PageNeed, ShowsLive ? every : null, SampleParts.All | (_listAdapters ? SampleParts.Adapters : 0));
        _monitor.Need(ResourceMonitor.AppsNeed, ShowsTraffic && Apps.IsExpanded && !Unavailable ? every : null);
        _gadget?.SetOnScreen(_onScreen && !ShowsReadings);
    }

    private void OnUpdated()
    {
        if (!_onScreen || !ShowsReadings) return;
        if (ShowsLive) foreach (var card in _cards.Values) Show(card, _monitor.Latest);
        _adapter.Refresh();
        foreach (var menu in SensorMenus) menu.Refresh();
        if (ShowsTraffic && Apps.IsExpanded) ShowTraffic();
        ShowStatus();
    }

    /// Opens the Network traffic control group (the gadget's "Network traffic control…").
    public void RevealApps()
    {
        Apps.IsExpanded = true;
        Need();
    }

    /// One row per program pinned, seen this session or holding a rule, inside the Apps scope: the pinned first in pin order, then the rest
    /// in the order first seen, each showing its rule as saved.
    private void ShowApps()
    {
        if (_limits is null) return;
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
        ListDiff.Apply(AppRows, ids.Select(id => known.GetValueOrDefault(id) ?? new AppNetItem(id, _limits!, _icons, TogglePin, _report)).ToList(), r => r.Id);
        foreach (var row in AppRows)
        {
            row.IsPinned = pins.Contains(row.Id);
            row.KnowPath(PathOf(row.Id, pins[row.Id]));
        }
        OnPropertyChanged(nameof(HasApps));
    }

    /// Where a program lives: as the poller saw it, else as its rule or its pin keeps it; empty when not known.
    private string PathOf(string id, IEnumerable<string> pinned) =>
        _monitor.Seen.GetValueOrDefault(id) is { Length: > 0 } seen ? seen
        : _limits?.Rule(id)?.Path is { Length: > 0 } ruled ? ruled
        : pinned.FirstOrDefault(Path.IsPathFullyQualified) ?? string.Empty;

    /// Pins a row to the top by its program's path (its identity while the path is not known), or unpins it.
    private void TogglePin(AppNetItem row)
    {
        var pins = _settings.Setup.Pinned;
        if (pins.RemoveAll(p => ProgramIdentity.Normalize(p) == row.Id) == 0) pins.Add(row.Path.Length > 0 ? row.Path : row.Id);
        _settings.Commit();
    }

    /// Every read while Network traffic control is open: new programs get rows, rows their speeds, download chips follow a change of what limits really do.
    private void ShowTraffic()
    {
        if (_monitor.Seen.Count != _seenShown) ListRows();
        if (_limits!.Download != _downloadShown)
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
        PacketFilter.Load(_limits!.PacketFilterOn);
        foreach (var row in AppRows) row.Load(); // the download chips follow the switch
        OnPropertyChanged(nameof(Unavailable));
        OnPropertyChanged(nameof(UnavailableLine));
        OnPropertyChanged(nameof(CanInstallService));
        Need();
        ShowStatus();
    });

    public void Dispose()
    {
        if (_limits is not null) _limits.Changed -= OnLimitsChanged;
        _settings.Changed -= OnSettingsChanged;
        _monitor.Updated -= OnUpdated;
        _monitor.ReadingsChanged -= OnReadingsChanged;
        Tabs.PropertyChanged -= OnTabChanged;
        _monitor.Need(ResourceMonitor.PageNeed, null);
        _monitor.Need(ResourceMonitor.AppsNeed, null);
        _presets?.Dispose();
        Live.Dispose();
        _gadget?.Dispose();
    }
}
