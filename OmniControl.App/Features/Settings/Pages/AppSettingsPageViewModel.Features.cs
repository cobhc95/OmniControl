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

    /// What switching off keeps; the drivers only of the features installed here (one left out by Setup put none here).
    public string FeaturesNote
    {
        get
        {
            var drivers = FeatureList.All.Where(f => f.Driver is not null && _features.Installed(f.Id)).Select(f => f.Driver).ToList();
            const string kept = "Turning a feature off hides its page and keeps your settings for it";
            return drivers.Count == 0 ? $"{kept}."
                : $"{kept}; {string.Join(" and ", drivers)} stay{(drivers.Count == 1 ? "s" : string.Empty)} installed until you remove "
                  + $"{(drivers.Count == 1 ? "it on its page" : "them on their pages")} or with the uninstaller.";
        }
    }

    private IEnumerable<SearchEntry> FeatureSearchEntries() =>
        _featureCards.Select(c => Entry(c.Card, FeaturesName, $"feature features turn on off disable enable run {c.Feature.Description}"))
            .Append(Entry(ReadyMade, FeaturesName, "ready-made features everything sound only monitoring only none custom choice"));

    private IReadOnlyList<(FeatureInfo, ToggleItem)> MakeFeatureCards() =>
        FeatureList.All.Select(f => (f, new ToggleItem(f.Name, _features.IsOn(f.Id), on => _features.Set(f.Id, on)) { Half = true, Unavailable = !_features.Installed(f.Id) })).ToList();

    /// The cards, the count, the chip and the pill as the switch and what runs are now (after any change, here or by Restore defaults).
    private void ShowFeatures()
    {
        foreach (var (feature, card) in _featureCards)
        {
            var on = _features.IsOn(feature.Id);
            var runs = _features.Runs(feature.Id);
            card.Load(on);
            card.Dimmed = !runs;
            card.Note = card.Unavailable ? "Not installed: run Setup to add it"
                : on && !runs && _features.Failed(feature.Id) ? "Couldn't start; the errors log says why"
                : on != runs && !feature.Live ? "Takes effect after restart"
                : on ? feature.Description : OffNote;
        }
        // What runs now, of what is installed: a switch waiting for a restart or a feature that failed is not counted.
        FeatureGroup.Summary = $"{_featureCards.Count(c => _features.Runs(c.Feature.Id))} of {_featureCards.Count(c => !c.Card.Unavailable)} on";
        ReadyMade.Refresh();
        TurnAllOnCommand.NotifyCanExecuteChanged();
    }

    /// Everything, Sound only and Monitoring only (each naming its features), and Custom, the one in force chosen.
    private IReadOnlyList<MenuRowItem> ReadyMadeRows()
    {
        var now = FeatureList.ReadyMadeOf(_features.Off);
        return FeatureList.ReadyMade.Select(r => new MenuRowItem(r.Name, string.Empty, r.Name == now, string.Empty, new RelayCommand(() => _features.SetOff(FeatureList.OffIn(r.On))),
                Detail: r.On.Count == FeatureList.All.Count ? "Every feature" : r.On.Count == 0 ? "Every feature off" : string.Join(", ", r.On.Select(id => FeatureList.Find(id)!.Name))))
            .Append(new MenuRowItem(FeatureList.Custom, string.Empty, now == FeatureList.Custom, string.Empty, new RelayCommand(() => { }), Detail: "The features you switch on below"))
            .ToList();
    }
}
