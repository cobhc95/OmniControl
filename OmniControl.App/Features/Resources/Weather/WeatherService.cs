using OmniControl.App.Features.Resources.Gadget;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;
using OmniControl.Core.Engine.Weather;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Resources.Weather;

/// The gadget's weather: asks the chosen provider only while the gadget and its weather are on and a place is set; never logs a place or a key.
public sealed class WeatherService : IDisposable
{
    public const string StateId = "resources.weather";
    private const string ToggleId = "resources.weather.toggle", KeyTarget = "OmniControl/weather/openweathermap";

    private readonly ResourcesSettings _settings;
    private readonly IStateRegistry _states;
    private readonly ICredentialStore _keys;
    private readonly WeatherClient _client = new(Timing.WeatherRequestTimeout, $"{AppInfo.Name}/{AppInfo.Version}");
    private readonly Timer _timer;
    private readonly IReadOnlyList<StateEntry> _values;
    private CancellationTokenSource? _running;
    private string _where = string.Empty, _plan = string.Empty;
    private TimeSpan _retry = Timing.WeatherRetryFirst;

    public WeatherService(ResourcesSettings settings, IActionRegistry actions, IStateRegistry states, ICredentialStore keys, string category)
    {
        (_settings, _states, _keys) = (settings, states, keys);
        _timer = new Timer(_ => Dispatcher.UIThread.Post(Start));
        const string how = "Asked by OmniControl's taskbar gadget from the chosen weather provider while the gadget and its weather are on.";
        StateEntry Level(string id, string name, Func<WeatherNow, double?> read, int min, int max, string unit, string icon) =>
            new($"{StateId}.{id}", name, category, StateKind.Level, () => Latest is { } now && read(now) is { } v ? StateValue.Of(Math.Round(v), At) : null, how,
                Min: min, Max: max, Unit: unit, Icon: icon, Keywords: "weather forecast outside");
        _values = new[]
        {
            Level("temperature", "Weather: temperature", n => Temperature(n.Temperature), WeatherWords.LowestTemperature, WeatherWords.HighestTemperature, "°", "temperature"),
            Level("feelslike", "Weather: feels like", n => Temperature(n.FeelsLike), WeatherWords.LowestTemperature, WeatherWords.HighestTemperature, "°", "temperature"),
            Level("rain", "Weather: rain chance", n => n.RainChance, 0, 100, "%", "umbrella"),
            Level("wind", "Weather: wind", n => Speed(n.WindKmh), 0, WeatherWords.HighestWind, string.Empty, "wind"),
            Level("humidity", "Weather: humidity", n => n.Humidity, 0, 100, "%", "droplet"),
            Level("high", "Weather: today's high", n => Temperature(n.High), WeatherWords.LowestTemperature, WeatherWords.HighestTemperature, "°", "temperature-sun"),
            Level("low", "Weather: today's low", n => Temperature(n.Low), WeatherWords.LowestTemperature, WeatherWords.HighestTemperature, "°", "temperature-snow"),
            new($"{StateId}.sky", "Weather: sky", category, StateKind.Choice, () => Latest is { } now ? StateValue.Of(WeatherWords.Sky(now.Sky), At) : null, how,
                Choices: () => Enum.GetValues<WeatherSky>().Select(WeatherWords.Sky).ToList(), Icon: "cloud", Keywords: "weather forecast outside condition"),
        };
        states.Register(_values.Prepend(new StateEntry(StateId, "Weather on the gadget", category, StateKind.OnOff, () => StateValue.Of(IsShown), "Shown or hidden here.",
            Keywords: "weather gadget taskbar forecast", Set: v => SetShown(v.Number is > 0), Icon: "cloud")));
        var family = new OnOffFamily(ToggleId, "Gadget weather", StateId);
        var toggle = new HotkeyAction(ToggleId, "Gadget weather: Toggle", category, "weather forecast gadget taskbar show hide temperature", _ => SetShown(!IsShown))
        {
            Family = family.Family, FamilyRole = OnOffFamily.ToggleRole, SuggestedIcons = new[] { "cloud" },
        };
        actions.Register(family.Siblings(toggle, SetShown).Prepend(toggle));
        settings.Changed += Follow;
        Follow();
    }

    private GadgetSettings Look => _settings.Setup.Gadget;

    private GadgetWeather Setup => Look.Weather;

    public bool IsShown => Look.WeatherShown is not null;

    /// The latest answer, metric (UI thread); null before the first one or after the place or provider changed.
    public WeatherNow? Latest { get; private set; }

    /// When <see cref="Latest"/> arrived.
    public DateTime? At { get; private set; }

    /// Why the last update failed (one sentence), or null.
    public string? Problem { get; private set; }

    /// An answer or a problem arrived (UI thread).
    public event Action? Updated;

    public void SetShown(bool on)
    {
        if (Look.Readings.FirstOrDefault(r => r.Id == GadgetSettings.WeatherId) is not { } row || row.Shown == on) return;
        row.Shown = on;
        _settings.Commit();
    }

    /// The one status line: what runs now, or why nothing does.
    public string Status
    {
        get
        {
            var provider = WeatherWords.Provider(Setup.Source);
            if (!IsShown) return "Weather is off.";
            if (!Setup.HasPlace) return "Pick a place to show the weather.";
            if (!Look.Shown) return $"{provider} · nothing runs while the gadget is off.";
            if (Problem is { } problem) return problem;
            return At is { } at ? $"{provider} · {WeatherWords.Ago(at)}" : $"Asking {provider}…";
        }
    }

    /// True while the last update failed (the status line is red).
    public bool Failed => IsShown && Problem is not null;

    public bool HasKey => _keys.Read(KeyTarget) is not null;

    /// Keeps the OpenWeatherMap key in Windows' credential store and asks again with it; false when Windows refused.
    public bool SaveKey(string key)
    {
        if (!_keys.Write(KeyTarget, key.Trim())) return false;
        Dispatcher.UIThread.Post(Start);
        return true;
    }

    /// The places whose name starts with <paramref name="text"/> (Open-Meteo's free geocoding, whatever the provider).
    public Task<IReadOnlyList<WeatherPlace>> FindPlacesAsync(string text, CancellationToken cancel) => _client.FindPlacesAsync(text, cancel);

    /// The weather row's cells: the <paramref name="shows"/> parts (live place and units), the row's label first, its gap after the last; each with the
    /// hover tooltip of the chosen details, none while the weather's hover switch is off. <paramref name="sample"/>: what is unknown (no answer yet,
    /// no place) shows a sample (the layout tiles).
    public IEnumerable<GadgetCell> Cells(GadgetReading row, IReadOnlyCollection<WeatherPart> shows, Color text, GadgetHover hover, bool sample = false)
    {
        var real = new WeatherWords(Latest, Setup.Imperial, Setup.Temperatures);
        var words = sample ? new WeatherWords(WeatherWords.Sampled(Latest), Setup.Imperial, Setup.Temperatures) : real;
        Func<IReadOnlyList<GadgetTipLine>>? tip = hover.Weather ? () => Tip(real, hover) : null; // a tooltip never shows the sample
        var parts = WeatherWords.Parts(shows).ToList();
        var place = WeatherWords.ShownName(Setup);
        if (sample && place.Length == 0) place = WeatherWords.SamplePlace;
        for (var i = 0; i < parts.Count; i++)
        {
            var (label, value, widest) = words.Cell(parts[i], place, shows);
            yield return new GadgetCell((i == 0 ? row.Label : string.Empty) + label, value, text, widest, i == parts.Count - 1 ? row.GapAfter : 0, tip);
        }
    }

    /// The chosen details; a failed update shows the place and why instead of the weather.
    private IReadOnlyList<GadgetTipLine> Tip(WeatherWords words, GadgetHover hover)
    {
        var place = WeatherWords.ShownName(Setup);
        var lines = Problem is { } problem ? new List<GadgetTipLine> { new(place), new(problem) } : words.Tip(place, hover.WeatherShows, hover.Rain).ToList();
        if (hover.WeatherShows.Contains(WeatherDetail.Provider))
            lines.Add(new GadgetTipLine($"{WeatherWords.Provider(Setup.Source)} · {(At is { } at ? WeatherWords.Ago(at) : "not asked yet")}", Muted: true));
        return lines;
    }

    private double? Temperature(double? celsius) => celsius is { } c ? WeatherWords.Temperature(c, Setup.Imperial) : null;

    private double? Speed(double? kmh) => kmh is { } k ? WeatherWords.Speed(k, Setup.Imperial) : null;

    /// Starts, moves or stops the updates after a change of the settings (UI thread); a new place or provider forgets the last answer.
    private void Follow()
    {
        _states.Notify(StateId);
        var setup = Setup;
        var where = $"{setup.Source}|{setup.UseLocation}|{setup.Latitude}|{setup.Longitude}";
        if (where != _where)
        {
            _where = where;
            (Latest, At, Problem) = (null, null, null);
            Arrived();
        }
        var needed = Look.Shown && IsShown && setup.HasPlace;
        var every = Every(setup);
        var plan = needed ? $"{where}|{every}" : string.Empty;
        if (plan == _plan) return; // nothing that decides the updates changed: the next one stays where it was
        (_plan, _retry) = (plan, Timing.WeatherRetryFirst);
        if (!needed)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _running?.Cancel();
            return;
        }
        var due = At is { } at ? at + every - DateTime.Now : TimeSpan.Zero;
        _timer.Change(due > TimeSpan.Zero ? due : TimeSpan.Zero, every);
    }

    private static TimeSpan Every(GadgetWeather setup) =>
        TimeSpan.FromMinutes(Math.Clamp(setup.UpdateMinutes, Timing.WeatherUpdateMin.TotalMinutes, Timing.WeatherUpdateMax.TotalMinutes));

    /// One update, with the settings as they are now (UI thread); the request runs on a worker and a newer one cancels it.
    private void Start()
    {
        if (!(Look.Shown && IsShown && Setup.HasPlace)) return;
        _running?.Cancel();
        var cancel = (_running = new CancellationTokenSource()).Token;
        var (source, useLocation, lat, lon) = (Setup.Source, Setup.UseLocation, Setup.Latitude, Setup.Longitude);
        _ = Task.Run(() => Guard.RunAsync("Weather update", async () =>
        {
            var (now, problem, retry) = await AskAsync(source, useLocation, lat, lon, cancel);
            if (!cancel.IsCancellationRequested) Dispatcher.UIThread.Post(() => Answered(now, problem, retry));
        }));
    }

    /// The answer or the one sentence that says why there is none, and whether asking again soon may help (the network was not reachable) (worker).
    /// Expected failures are worded, never logged.
    private async Task<(WeatherNow?, string?, bool)> AskAsync(WeatherSource source, bool useLocation, double? lat, double? lon, CancellationToken cancel)
    {
        var name = WeatherWords.Provider(source);
        string? key = null;
        if (source == WeatherSource.OpenWeatherMap && (key = _keys.Read(KeyTarget)) is null) return (null, "OpenWeatherMap needs your API key.", false);
        if (useLocation)
        {
            try
            {
                if (await WindowsLocation.FindAsync() is not { } here) return (null, "Windows location is off for desktop apps.", false);
                (lat, lon) = here;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                return (null, "Windows would not tell this PC's location.", false);
            }
        }
        try
        {
            return (await _client.NowAsync(source, lat!.Value, lon!.Value, key, cancel), null, false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return (null, $"{name} refused the API key.", false);
        }
        catch (HttpRequestException ex)
        {
            return (null, ex.StatusCode is { } code ? $"{name} answered {(int)code}; asking again soon." : $"Couldn't reach {name}; asking again soon.", true);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return (null, null, false); // a newer update or a change of settings took over; the caller drops this one
        }
        catch (TaskCanceledException)
        {
            return (null, $"{name} did not answer in time; asking again soon.", true);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return (null, $"{name} sent an answer OmniControl could not read.", false);
        }
    }

    /// A request that could not get through is tried again sooner than the next update, waiting longer each time (a PC that just signed in or
    /// woke has no network for a while).
    private void Answered(WeatherNow? now, string? problem, bool retry)
    {
        if (now is not null) (Latest, At, _retry) = (now, DateTime.Now, Timing.WeatherRetryFirst);
        else if (retry && _plan.Length > 0)
        {
            var every = Every(Setup);
            _timer.Change(_retry < every ? _retry : every, every);
            _retry = _retry * 2 < every ? _retry * 2 : every;
        }
        Problem = problem;
        Arrived();
    }

    private void Arrived()
    {
        foreach (var value in _values) _states.Notify(value.Id);
        Updated?.Invoke();
    }

    public void Dispose()
    {
        _settings.Changed -= Follow;
        _timer.Dispose();
        _running?.Cancel();
        _client.Dispose();
        _states.Unregister(_values.Select(v => v.Id).Append(StateId));
    }
}
