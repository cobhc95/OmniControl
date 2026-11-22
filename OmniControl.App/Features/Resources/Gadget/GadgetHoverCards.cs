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

    private static readonly SearchCard ReadingsCard = GadgetSettingsModel.Card("Readings", "hover tooltip details") with { Note = "Shown when you hover the readings part" },
        ScopeCard = GadgetSettingsModel.Card("Hover shows", "hover tooltip details") with { Menu = ScopeWords.Select(s => s.Words).ToList() },
        ReadingShowsCard = GadgetSettingsModel.Card("Readings show", "hover tooltip details") with { Choices = Enum.GetValues<ReadingDetail>().Select(ReadingTips.DetailName).ToList() },
        WeatherCard = GadgetSettingsModel.Card("Weather", "hover tooltip details forecast") with { Note = "Shown when you hover the weather part" },
        RainCard = GadgetSettingsModel.Card("Rain chance", "hover tooltip details forecast") with { Menu = Enum.GetValues<RainWindow>().Select(WeatherWords.RainWindowName).ToList() },
        WeatherShowsCard = GadgetSettingsModel.Card("Weather shows", "hover tooltip details forecast") with { Choices = Enum.GetValues<WeatherDetail>().Select(WeatherWords.DetailName).ToList() };

    /// The cards global search offers (the page's word source reads them without making the items).
    public static IReadOnlyList<SearchCard> Cards { get; } = new[] { ReadingsCard, ScopeCard, ReadingShowsCard, WeatherCard, RainCard, WeatherShowsCard };

    public GadgetHoverCards(ResourcesSettings settings)
    {
        _settings = settings;
        _readings = new ToggleItem(ReadingsCard, Hover.Readings, on => Change(h => h.Readings = on)) { Half = true };
        _scope = new MenuChoiceItem(ScopeCard, () => ScopeWords.First(s => s.All == Hover.AllReadings).Words,
            () => ScopeWords.Select(s => Row(s.Words, Hover.AllReadings == s.All, h => h.AllReadings = s.All)).ToList()) { Half = true };
        _readingChips = Chips<ReadingDetail>(ReadingTips.DetailName, h => h.ReadingShows);
        _weather = new ToggleItem(WeatherCard, Hover.Weather, on => Change(h => h.Weather = on)) { Half = true };
        _rain = new MenuChoiceItem(RainCard, () => WeatherWords.RainWindowName(Hover.Rain),
            () => Enum.GetValues<RainWindow>().Select(w => Row(WeatherWords.RainWindowName(w), Hover.Rain == w, h => h.Rain = w)).ToList()) { Half = true };
        _weatherChips = Chips<WeatherDetail>(WeatherWords.DetailName, h => h.WeatherShows);
        Group = new ItemGroup("Resources/GadgetHover", "Hover details", new object[]
        {
            _readings, _scope, new ChoiceChipsItem(ReadingShowsCard, _readingChips),
            _weather, _rain, new ChoiceChipsItem(WeatherShowsCard, _weatherChips),
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
