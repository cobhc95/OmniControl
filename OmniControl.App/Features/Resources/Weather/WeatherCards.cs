using OmniControl.Core.Engine.Weather;

namespace OmniControl.App.Features.Resources.Weather;

/// The Weather group of the Taskbar gadget tab: switch, Place, Provider, Units, Temperatures, Update every, Shows and the status line; every change applies at once.
public sealed partial class WeatherCards : ObservableObject, IDisposable
{
    private static readonly string[] UnitWords = { "°C, km/h", "°F, mph" };
    private readonly ResourcesSettings _settings;
    private readonly WeatherService _weather;
    private readonly WeatherPlaceItem _place;
    private readonly MenuChoiceItem _provider;
    private readonly ChoiceItem _units, _temperatures;
    private readonly SliderItem _every;
    private readonly IReadOnlyList<ChipItem> _shows;

    private const string ProviderLabel = "Provider", UnitsLabel = "Units", TemperaturesLabel = "Temperatures", EveryLabel = "Update every", ShowsLabel = "Shows";

    /// The labels of the cards global search offers (the page's word source reads them without making the cards).
    public static IReadOnlyList<string> SearchLabels { get; } = new[] { WeatherPlaceItem.PlaceLabel, ProviderLabel, UnitsLabel, TemperaturesLabel, EveryLabel, ShowsLabel };

    public WeatherCards(ResourcesSettings settings, WeatherService weather)
    {
        (_settings, _weather) = (settings, weather);
        _place = new WeatherPlaceItem(weather, () => Setup, Change);
        _provider = new MenuChoiceItem(ProviderLabel, () => WeatherWords.Provider(Setup.Source), ProviderRows) { Half = true };
        _units = new ChoiceItem(UnitsLabel, UnitWords, UnitWords[Setup.Imperial ? 1 : 0], u => Change(w => w.Imperial = u == UnitWords[1])) { Half = true };
        _temperatures = new ChoiceItem(TemperaturesLabel, Reading.SignWords, Reading.SignWords[(int)Setup.Temperatures],
            w => Change(s => s.Temperatures = (TemperatureSign)Reading.SignWords.ToList().IndexOf(w))) { Half = true };
        _every = new SliderItem(EveryLabel, Minutes, v => Apply(w => w.UpdateMinutes = v), _ => { },
            (int)Timing.WeatherUpdateMin.TotalMinutes, (int)Timing.WeatherUpdateMax.TotalMinutes, " min", (int)Timing.WeatherUpdateEvery.TotalMinutes) { Half = true };
        _shows = Enum.GetValues<WeatherPart>().Select(p => new ChipItem(WeatherWords.PartName(p), new RelayCommand(() => Change(w =>
        {
            if (!w.Shows.Remove(p)) w.Shows.Add(p);
        })), null) { Key = p.ToString() }).ToList();
        Group = new ItemGroup("Resources/GadgetWeather", "Weather", new object[]
        {
            _place, _provider, _units, _temperatures, _every,
            new ChoiceChipsItem(ShowsLabel, _shows) { Note = "One small request per update; nothing runs while the gadget is off." },
        }) { ExpandedByDefault = true, Summary = string.Empty, Icon = "cloud" };
        Group.UseSwitch(new GroupSwitch(() => weather.IsShown, () => weather.SetShown(!weather.IsShown)));
        settings.Changed += Load;
        weather.Updated += ShowStatus;
        Load();
    }

    private GadgetWeather Setup => _settings.Setup.Gadget.Weather;

    private int Minutes => (int)Math.Round(Setup.UpdateMinutes);

    public ItemGroup Group { get; }

    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _failed;

    private IReadOnlyList<MenuRowItem> ProviderRows() => Enum.GetValues<WeatherSource>().Select(source => new MenuRowItem(WeatherWords.Provider(source), string.Empty,
        Setup.Source == source, string.Empty, new RelayCommand(() => Change(w => w.Source = source)),
        Tags: source == WeatherSource.OpenWeatherMap ? null : new[] { new TagItem("free · no key", TagTone.Good) },
        Detail: source == WeatherSource.OpenWeatherMap ? "needs your API key" : string.Empty)).ToList();

    private OperationResult Apply(Action<GadgetWeather> change)
    {
        Change(change);
        return OperationResult.Ok();
    }

    private void Change(Action<GadgetWeather> change)
    {
        change(Setup);
        _settings.Commit();
    }

    private void Load()
    {
        _place.Load();
        _provider.Refresh();
        _units.Load(UnitWords, UnitWords[Setup.Imperial ? 1 : 0]);
        _temperatures.Load(Reading.SignWords, Reading.SignWords[(int)Setup.Temperatures]);
        _every.Load(Minutes);
        foreach (var chip in _shows) chip.IsSelected = Setup.Shows.Any(p => p.ToString() == chip.Key);
        Group.RefreshMaster();
        ShowStatus();
    }

    private void ShowStatus() => (Status, Failed) = (_weather.Status, _weather.Failed);

    public void Dispose()
    {
        _settings.Changed -= Load;
        _weather.Updated -= ShowStatus;
        _place.Dispose();
        Group.Dispose();
    }
}
