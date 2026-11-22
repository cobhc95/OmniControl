using OmniControl.App.Features.Resources.Weather;

namespace OmniControl.App.Features.Resources.Gadget;

/// The Hover details group of the Taskbar gadget tab: what hovering the readings part and the weather part shows; every change is saved and shown at once.
public sealed class GadgetHoverCards : IDisposable
{
    private static readonly (bool All, string Words)[] ScopeWords = { (false, "Only the reading under the pointer"), (true, "All shown readings") };

    private readonly ResourcesSettings _settings;
    private readonly ToggleItem _readings, _weather;
    private readonly MenuChoiceItem _scope, _rain;
    private readonly IReadOnlyList<ChipItem> _readingChips, _weatherChips;

    private const string ReadingsLabel = "Readings", ScopeLabel = "Hover shows", ReadingShowsLabel = "Readings show", WeatherLabel = "Weather", RainLabel = "Rain chance", WeatherShowsLabel = "Weather shows";

    /// The labels of the cards global search offers (the page's word source reads them without making the cards).
    public static IReadOnlyList<string> SearchLabels { get; } = new[] { ReadingsLabel, ScopeLabel, ReadingShowsLabel, WeatherLabel, RainLabel, WeatherShowsLabel };

    public GadgetHoverCards(ResourcesSettings settings)
    {
        _settings = settings;
        _readings = new ToggleItem(ReadingsLabel, Hover.Readings, on => Change(h => h.Readings = on)) { Half = true, Note = "Shown when you hover the readings part" };
        _scope = new MenuChoiceItem(ScopeLabel, () => ScopeWords.First(s => s.All == Hover.AllReadings).Words,
            () => ScopeWords.Select(s => Row(s.Words, Hover.AllReadings == s.All, h => h.AllReadings = s.All)).ToList()) { Half = true };
        _readingChips = Chips<ReadingDetail>(ReadingTips.DetailName, h => h.ReadingShows);
        _weather = new ToggleItem(WeatherLabel, Hover.Weather, on => Change(h => h.Weather = on)) { Half = true, Note = "Shown when you hover the weather part" };
        _rain = new MenuChoiceItem(RainLabel, () => WeatherWords.RainWindowName(Hover.Rain),
            () => Enum.GetValues<RainWindow>().Select(w => Row(WeatherWords.RainWindowName(w), Hover.Rain == w, h => h.Rain = w)).ToList()) { Half = true };
        _weatherChips = Chips<WeatherDetail>(WeatherWords.DetailName, h => h.WeatherShows);
        Group = new ItemGroup("Resources/GadgetHover", "Hover details", new object[]
        {
            _readings, _scope, new ChoiceChipsItem(ReadingShowsLabel, _readingChips),
            _weather, _rain, new ChoiceChipsItem(WeatherShowsLabel, _weatherChips),
        }) { ExpandedByDefault = true, Summary = string.Empty };
        Load();
    }

    private GadgetHover Hover => _settings.Setup.Gadget.Hover;

    public ItemGroup Group { get; }

    /// The muted line under the group.
    public string Note => "Either switch off: hovering that part shows nothing.";

    private MenuRowItem Row(string words, bool ticked, Action<GadgetHover> pick) => new(words, string.Empty, ticked, string.Empty, new RelayCommand(() => Change(pick)));

    /// One chip per detail, on while the list holds it; a click adds or takes it out.
    private IReadOnlyList<ChipItem> Chips<T>(Func<T, string> name, Func<GadgetHover, List<T>> list) where T : struct, Enum =>
        Enum.GetValues<T>().Select(d => new ChipItem(name(d), new RelayCommand(() => Change(h =>
        {
            if (!list(h).Remove(d)) list(h).Add(d);
        })), null) { Key = d.ToString() }).ToList();

    private void Change(Action<GadgetHover> change)
    {
        change(Hover);
        _settings.Commit();
    }

    /// Shows the settings as they are now (a change here, a preset or an import).
    public void Load()
    {
        var hover = Hover;
        _readings.Load(hover.Readings);
        _scope.Refresh();
        foreach (var chip in _readingChips) chip.IsSelected = hover.ReadingShows.Any(d => d.ToString() == chip.Key);
        _weather.Load(hover.Weather);
        _rain.Refresh();
        foreach (var chip in _weatherChips) chip.IsSelected = hover.WeatherShows.Any(d => d.ToString() == chip.Key);
    }

    public void Dispose() => Group.Dispose();
}
