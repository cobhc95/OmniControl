namespace OmniControl.App.Features.Settings.Pages;

/// App settings' Features group (THE feature switch, <see cref="FeatureSwitches"/>): a half-width switch card per feature with its description,
/// dimmed with one line while it does not run; the header's "N of M on", the "Ready-made: name" chip (Setup's choices) and the Turn all on pill;
/// one muted line under the cards.
public sealed partial class AppSettingsPageViewModel
{
    private const string FeaturesName = "Features";
    private const string OffNote = "Off: its page, actions and checks are not running";

    private readonly FeatureSwitches _features;
    private readonly IReadOnlyList<(FeatureInfo Feature, ToggleItem Card)> _featureCards;

    public ObservableCollection<object> FeatureItems { get; } = new();

    public ItemGroup FeatureGroup { get; }

    /// The header's "Ready-made: name" chip.
    public MenuChoiceItem ReadyMade { get; }

    public IRelayCommand TurnAllOnCommand { get; }

    public string FeaturesNote => "Turning a feature off stops it at once and hides its page; your settings for it are kept. Network traffic control and Equalizer APO drivers "
        + "stay installed until you remove them from their own pages or the uninstaller.";

    private IEnumerable<SearchEntry> FeatureSearchEntries() =>
        _featureCards.Select(c => Entry(c.Card, FeaturesName, $"feature features turn on off disable enable run {c.Feature.Description}"))
            .Append(Entry(ReadyMade, FeaturesName, "ready-made features everything sound only monitoring only custom choice"));

    private IReadOnlyList<(FeatureInfo, ToggleItem)> MakeFeatureCards() =>
        FeatureList.All.Select(f => (f, new ToggleItem(f.Name, _features.IsOn(f.Id), on => _features.Set(f.Id, on)) { Half = true })).ToList();

    /// The cards, the count, the chip and the pill as the switch and what runs are now (after any change, here or by Restore defaults).
    private void ShowFeatures()
    {
        foreach (var (feature, card) in _featureCards)
        {
            var on = _features.IsOn(feature.Id);
            var runs = _features.Runs(feature.Id);
            card.Load(on);
            card.Dimmed = !runs;
            card.Note = on == runs ? (on ? feature.Description : OffNote)
                : !feature.Live ? "Takes effect after restart"
                : "Couldn't start; the errors log says why";
        }
        FeatureGroup.Summary = $"{_featureCards.Count(c => c.Card.IsOn)} of {_featureCards.Count} on";
        ReadyMade.Refresh();
        TurnAllOnCommand.NotifyCanExecuteChanged();
    }

    /// Everything, Sound only and Monitoring only (each naming its features), and Custom, the one in force chosen.
    private IReadOnlyList<MenuRowItem> ReadyMadeRows()
    {
        var now = FeatureList.ReadyMadeOf(_features.Off);
        return FeatureList.ReadyMade.Select(r => new MenuRowItem(r.Name, string.Empty, r.Name == now, string.Empty, new RelayCommand(() => _features.SetOff(FeatureList.OffIn(r.On))),
                Detail: r.On.Count == FeatureList.All.Count ? "Every feature" : string.Join(", ", r.On.Select(id => FeatureList.Find(id)!.Name))))
            .Append(new MenuRowItem(FeatureList.Custom, string.Empty, now == FeatureList.Custom, string.Empty, new RelayCommand(() => { }), Detail: "The features you switch on below"))
            .ToList();
    }
}
