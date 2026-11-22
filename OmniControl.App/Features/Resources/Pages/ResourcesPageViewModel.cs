using System.ComponentModel;
using System.Globalization;
using OmniControl.App.Features.Resources.Gadget;
using OmniControl.App.Shared.Presets;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources.Pages;

/// The Resources page: preset picker and two tabs, Readings (Live readings) and Taskbar gadget (<see cref="GadgetSettingsModel"/>);
/// it asks the poller for readings only while on screen.
public sealed class ResourcesPageViewModel : PageViewModel, IDisposable
{
    private readonly IReadingList _monitor;
    private readonly ResourcesSettings _settings;
    private readonly ProviderChoice _presets;
    private readonly Dictionary<string, LiveCard> _cards = new(); // by the id of the reading each shows; made once, kept while its reading is there
    private readonly ObservableCollection<object> _liveItems = new();
    private readonly NetworkAdapterMenu _adapter;
    private readonly SensorMenu _hottest;
    private readonly GadgetSettingsModel _gadget;
    private bool _onScreen;

    public const string PageTitle = "Resources", ReadingsTab = "Readings", GadgetTab = "Taskbar gadget";

    public static readonly PageInfo Info = new(PageTitle, "M4,20 H20 V22 H4 Z M5,12 H8 V19 H5 Z M10.5,6 H13.5 V19 H10.5 Z M16,9 H19 V19 H16 Z", NavGroups.System,
        "resources cpu gpu graphics memory ram disk drive temperature network adapter upload download speed taskbar gadget");

    public override bool ShowsOwnHeader => true; // the preset picker sits on the title line

    public ChoiceItem PresetChoice => _presets.Choice;
    public override PageTabs Tabs { get; } = new(ReadingsTab, GadgetTab);

    public bool ShowsReadings => Tabs.Current == ReadingsTab;

    /// The Taskbar gadget tab's content once opened (null before, so its view is built only when first wanted), then kept and hidden with the tab.
    public GadgetSettingsModel? Gadget => _gadgetBuilt ? _gadget : null;
    private bool _gadgetBuilt;
    public ItemGroup Live { get; }
    public ChoiceItem ReadEvery { get; }

    public override IEnumerable<SearchEntry> SearchEntries => new[] { Entry(ReadEvery, ReadEveryCard) }
        .Concat(_gadget.SearchItems.Select(i => Entry(i, GadgetCard(i.Label))));

    private static readonly SearchCard ReadEveryCard = new("Read every", "resources readings");

    private static SearchCard GadgetCard(string label) => new(label, "resources taskbar gadget", GadgetTab);

    /// The page's search words (<c>FeaturePage.Words</c>).
    public static IEnumerable<string> Words() => GadgetSettingsModel.SearchLabels.Select(l => GadgetCard(l).Words).Prepend(ReadEveryCard.Words);

    /// <param name="presets">The title line's preset picker and <paramref name="gadget"/> the Taskbar gadget tab (the page disposes both).</param>
    internal ResourcesPageViewModel(IReadingList monitor, ResourcesSettings settings, ProviderChoice presets, GadgetSettingsModel gadget) : base(Info)
    {
        (_monitor, _settings, _presets, _gadget) = (monitor, settings, presets, gadget);
        Tabs.PropertyChanged += OnTabChanged;

        _adapter = new NetworkAdapterMenu(monitor, settings);

        _hottest = new SensorMenu(monitor, settings, Reading.Find(Reading.HottestId)!, "Hottest (auto)", null);
        ListCards();
        Live = new ItemGroup("Resources/Live", "Live readings", _liveItems)
        {
            ExpandedByDefault = true, Summary = string.Empty,
            Tip = "Readings come from Windows itself (GPU and disk load as Task Manager shows them, the graphics card's and drives' own temperatures); LibreHardwareMonitor for the CPU package temperature (it needs the PawnIO driver, else the ACPI thermal zone is read) and for what Windows does not give.",
        };
        ReadEvery = new ChoiceItem(ReadEveryCard.Label, Timing.ResourcesReadChoices.Select(Every).ToList(), Every(settings.Setup.ReadEverySeconds), picked =>
        {
            settings.Setup.ReadEverySeconds = Timing.ResourcesReadChoices.First(s => Every(s) == picked);
            settings.Commit();
        });

        settings.Changed += OnSettingsChanged;
        monitor.Updated += OnUpdated;
        monitor.ReadingsChanged += OnReadingsChanged;
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
    }

    /// The readings tab asks the poller at the page's interval while it is on screen; the gadget tab holds the gadget's own reads.
    private void Need()
    {
        var every = _onScreen && ShowsReadings ? TimeSpan.FromSeconds(_settings.Setup.ReadEverySeconds) : (TimeSpan?)null;
        _monitor.Need(ResourceMonitor.PageNeed, every, SampleParts.All);
        _gadget.SetOnScreen(_onScreen && !ShowsReadings);
    }

    private void OnUpdated()
    {
        if (!_onScreen || !ShowsReadings) return;
        foreach (var card in _cards.Values) Show(card, _monitor.Latest);
        _adapter.Refresh();
        foreach (var menu in SensorMenus) menu.Refresh();
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _monitor.Updated -= OnUpdated;
        _monitor.ReadingsChanged -= OnReadingsChanged;
        Tabs.PropertyChanged -= OnTabChanged;
        _monitor.Need(ResourceMonitor.PageNeed, null);
        _presets.Dispose();
        Live.Dispose();
        _gadget.Dispose();
    }
}