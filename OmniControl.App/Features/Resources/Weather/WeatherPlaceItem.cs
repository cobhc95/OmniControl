using OmniControl.Core.Engine.Weather;

namespace OmniControl.App.Features.Resources.Weather;

/// The weather's Place card: a searched place or Windows' location, and for OpenWeatherMap the API key (kept in Windows' credential store).
public sealed partial class WeatherPlaceItem : ObservableObject, IEditorItem, IDisposable
{
    private readonly WeatherService _weather;
    private readonly Action<Action<GadgetWeather>> _change;
    private readonly Func<GadgetWeather> _setup;
    private readonly DispatcherTimer _pause = new() { Interval = Timing.WeatherSearchPause };
    private CancellationTokenSource? _search;
    private bool _loading;
    private string? _place;

    public WeatherPlaceItem(WeatherService weather, Func<GadgetWeather> setup, Action<Action<GadgetWeather>> change)
    {
        (_weather, _setup, _change) = (weather, setup, change);
        _pause.Tick += (_, _) =>
        {
            _pause.Stop();
            _ = Guard.RunAsync("Weather place search", SearchAsync);
        };
        UseLocationCommand = new RelayCommand(() => _change(w => (w.UseLocation, w.Place) = (true, string.Empty)));
        SaveKeyCommand = new RelayCommand(SaveKey);
        HasKey = weather.HasKey;
        Load();
    }

    public string Label => "Place";

    public IRelayCommand UseLocationCommand { get; }
    public IRelayCommand SaveKeyCommand { get; }

    /// The box's words: the place in use, or what is being typed.
    [ObservableProperty] private string _query = string.Empty;

    /// The "Name shown" chip's words and the nickname box (only used when the style is Nickname).
    [ObservableProperty] private string _nameStyleWords = string.Empty;
    [ObservableProperty] private bool _isNickname;
    [ObservableProperty] private string _nickname = string.Empty;

    public IReadOnlyList<MenuRowItem> NameStyleRows => Enum.GetValues<PlaceNameStyle>().Select(style => new MenuRowItem(WeatherWords.NameStyleWords(style), string.Empty,
        _setup().NameStyle == style, string.Empty, new RelayCommand(() => _change(w => w.NameStyle = style)))).ToList();

    [ObservableProperty] private bool _useLocation;
    [ObservableProperty] private bool _needsKey;
    [ObservableProperty] private string _keyText = string.Empty;
    [ObservableProperty] private bool _hasKey;
    [ObservableProperty] private DateTime? _keySavedAt;

    /// The search's answer as menu rows (the view opens the menu when they arrive).
    [ObservableProperty] private IReadOnlyList<MenuRowItem> _results = Array.Empty<MenuRowItem>();

    /// Shows the settings as they are now.
    public void Load()
    {
        var setup = _setup();
        var place = setup.UseLocation ? WeatherWords.MyLocation : setup.Place;
        _loading = true;
        if (place != _place) (Query, _place) = (place, place); // a change elsewhere never overwrites what is being typed

        (UseLocation, NeedsKey) = (setup.UseLocation, setup.Source == WeatherSource.OpenWeatherMap);
        (NameStyleWords, IsNickname) = (WeatherWords.NameStyleWords(setup.NameStyle), setup.NameStyle == PlaceNameStyle.Nickname);
        if (setup.Nickname != Nickname) Nickname = setup.Nickname;
        _loading = false;
    }

    partial void OnNicknameChanged(string value)
    {
        if (!_loading) _change(w => w.Nickname = value);
    }

    partial void OnQueryChanged(string value)
    {
        if (_loading) return;
        _pause.Stop();
        if (value.Trim().Length > 0) _pause.Start();
    }

    private async Task SearchAsync()
    {
        _search?.Cancel();
        var cancel = (_search = new CancellationTokenSource()).Token;
        var text = Query.Trim();
        IReadOnlyList<MenuRowItem> rows;
        try
        {
            var places = await _weather.FindPlacesAsync(text, cancel);
            rows = places.Count == 0 ? new[] { Note("No place of that name found.") } : places.Select(Row).ToList();
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            rows = new[] { Note("Couldn't search for places; check the connection.") };
        }
        if (!cancel.IsCancellationRequested) Results = rows;
    }

    private MenuRowItem Row(WeatherPlace place) => new(place.Name, string.Empty, false, string.Empty, new RelayCommand(() =>
    {
        var country = place.Region.Split(", ").Last();
        _change(w => (w.Place, w.Latitude, w.Longitude, w.UseLocation) = (country.Length > 0 ? $"{place.Name}, {country}" : place.Name, place.Latitude, place.Longitude, false));
    }), Detail: place.Region);

    private static MenuRowItem Note(string text) => MenuRowItem.Header(text);

    private void SaveKey()
    {
        if (KeyText.Trim().Length == 0 || !_weather.SaveKey(KeyText)) return;
        (KeyText, HasKey, KeySavedAt) = (string.Empty, true, DateTime.Now);
    }

    public void Dispose()
    {
        _pause.Stop();
        _search?.Cancel();
    }
}
