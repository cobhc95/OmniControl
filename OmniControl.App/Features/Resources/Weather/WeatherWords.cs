using System.Globalization;
using OmniControl.App.Features.Resources.Gadget;
using OmniControl.Core.Engine.Weather;

namespace OmniControl.App.Features.Resources.Weather;

/// How the weather is written: values in the chosen units, each Shows part as a cell (temperatures in the gadget's <paramref name="sign"/> style), the tooltip, the providers' and skies' names and icons. Pure.
public sealed class WeatherWords(WeatherNow? now, bool imperial, TemperatureSign sign)
{
    public const int LowestTemperature = -80, HighestTemperature = 140, HighestWind = 400;
    public const string MyLocation = "My location";
    private const double MphPerKmh = 0.621371;

    /// The place a sample names while none is set.
    public const string SamplePlace = "Oslo";

    /// A typical day (metric), the sample of the layout tiles.
    private static readonly WeatherNow Sample = new(12, 10, 10, 14, 70, 15, 8, WeatherSky.PartlyCloudy, true);

    /// <paramref name="now"/> with every value it does not have taken from <see cref="Sample"/>; the sample when there is no answer.
    public static WeatherNow Sampled(WeatherNow? now) => now is null ? Sample : now with
    {
        FeelsLike = now.FeelsLike ?? Sample.FeelsLike, RainChance = now.RainChance ?? Sample.RainChance, WindKmh = now.WindKmh ?? Sample.WindKmh,
        Humidity = now.Humidity ?? Sample.Humidity, High = now.High ?? Sample.High, Low = now.Low ?? Sample.Low,
    };

    /// The place as the gadget names it: Windows' location, the nickname, the city only, or the whole name found.
    public static string ShownName(GadgetWeather setup) => setup.UseLocation ? MyLocation
        : setup.NameStyle == PlaceNameStyle.Nickname && setup.Nickname.Trim().Length > 0 ? setup.Nickname.Trim()
        : setup.NameStyle == PlaceNameStyle.Full ? setup.Place : setup.Place.Split(',')[0].Trim();

    public static string NameStyleWords(PlaceNameStyle style) => style switch
    {
        PlaceNameStyle.Full => "Full",
        PlaceNameStyle.Nickname => "Nickname",
        _ => "Short",
    };

    public static string Provider(WeatherSource source) => source switch
    {
        WeatherSource.MetNorway => "MET Norway (yr.no)",
        WeatherSource.OpenWeatherMap => "OpenWeatherMap",
        _ => "Open-Meteo",
    };

    public static string Sky(WeatherSky sky) => sky switch
    {
        WeatherSky.Clear => "Clear",
        WeatherSky.PartlyCloudy => "Partly cloudy",
        WeatherSky.Cloudy => "Cloudy",
        WeatherSky.Fog => "Fog",
        WeatherSky.Drizzle => "Drizzle",
        WeatherSky.Rain => "Rain",
        WeatherSky.Snow => "Snow",
        _ => "Thunderstorm",
    };

    /// The icon library's drawing of the sky (the moon on a clear night).
    public static string SkyIcon(WeatherSky sky, bool day) => sky switch
    {
        WeatherSky.Clear => day ? "sun" : "moon",
        WeatherSky.PartlyCloudy or WeatherSky.Cloudy => "cloud",
        WeatherSky.Fog => "cloud-fog",
        WeatherSky.Snow => "cloud-snow",
        WeatherSky.Storm => "cloud-storm",
        _ => "cloud-rain",
    };

    public static string PartName(WeatherPart part) => part switch
    {
        WeatherPart.FeelsLike => "Feels like",
        WeatherPart.RainChance => "Rain chance",
        WeatherPart.PlaceName => "Place name",
        WeatherPart.HighLow => "High / low",
        _ => part.ToString(),
    };

    public static double Temperature(double celsius, bool imperial) => imperial ? celsius * 9 / 5 + 32 : celsius;

    public static double Speed(double kmh, bool imperial) => imperial ? kmh * MphPerKmh : kmh;

    /// "just now", "4 min ago", "2 h ago".
    public static string Ago(DateTime at)
    {
        var gone = DateTime.Now - at;
        return gone.TotalMinutes < 1 ? "just now" : gone.TotalHours < 1 ? $"{(int)gone.TotalMinutes} min ago" : $"{(int)gone.TotalHours} h ago";
    }

    /// The parts shown, in the gadget's order, the icon and the temperature as one cell.
    public static IEnumerable<WeatherPart> Parts(IReadOnlyCollection<WeatherPart> shows) =>
        Enum.GetValues<WeatherPart>().Where(p => shows.Contains(p) && !(p == WeatherPart.Temperature && shows.Contains(WeatherPart.Icon)));

    private string Degrees(double? celsius) => celsius is { } c ? $"{Temperature(c, imperial).ToString("0", CultureInfo.CurrentCulture)}°" : Reading.Unknown;

    /// A temperature as a gadget cell writes it, and the widest it keeps room for.
    private string CellDegrees(double? celsius) => celsius is { } c ? Signed(Temperature(c, imperial).ToString("0", CultureInfo.CurrentCulture)) : Reading.Unknown;

    private string Signed(string number) => Reading.Temperature(number, sign, imperial ? 'F' : 'C');

    private string WidestDegrees => Signed("-00");

    private static string Percent(double? value) => value is { } v ? $"{v:0}%" : Reading.Unknown;

    private string Wind(double? kmh) => kmh is { } k ? $"{Speed(k, imperial):0} {(imperial ? "mph" : "km/h")}" : Reading.Unknown;

    private string SkyToken => GadgetLabel.Token(now is { } n ? SkyIcon(n.Sky, n.Day) : "cloud");

    private string HighLow => $"H {Degrees(now?.High)} L {Degrees(now?.Low)}";

    private string CellHighLow => $"H {CellDegrees(now?.High)} L {CellDegrees(now?.Low)}";

    /// One part as a gadget cell: its label (<see cref="GadgetLabel"/>), its value words and the widest words it keeps room for.
    public (string Label, string Value, string Widest) Cell(WeatherPart part, string place, IReadOnlyCollection<WeatherPart> shows) => part switch
    {
        WeatherPart.Icon when shows.Contains(WeatherPart.Temperature) => (SkyToken, CellDegrees(now?.Temperature), WidestDegrees),
        WeatherPart.Icon => (SkyToken, string.Empty, string.Empty),
        WeatherPart.Temperature => (string.Empty, CellDegrees(now?.Temperature), WidestDegrees),
        WeatherPart.FeelsLike => ("feels", CellDegrees(now?.FeelsLike), WidestDegrees),
        WeatherPart.RainChance => (GadgetLabel.Token("umbrella"), Percent(now?.RainChance), "100%"),
        WeatherPart.Wind => (GadgetLabel.Token("wind"), Wind(now?.WindKmh), "000 km/h"),
        WeatherPart.Humidity => (GadgetLabel.Token("droplet"), Percent(now?.Humidity), "100%"),
        WeatherPart.PlaceName => (string.Empty, place, place),
        _ => (string.Empty, CellHighLow, $"H {WidestDegrees} L {WidestDegrees}"),
    };

    public static string DetailName(WeatherDetail detail) => detail switch
    {
        WeatherDetail.PlaceSky => "Place and sky",
        WeatherDetail.FeelsLike => "Feels like",
        WeatherDetail.HighLow => "High / low",
        WeatherDetail.RainChance => "Rain chance",
        WeatherDetail.Sun => "Sunrise / sunset",
        WeatherDetail.NextHours => "Next hours",
        WeatherDetail.Provider => "Provider and time",
        _ => detail.ToString(),
    };

    public static string RainWindowName(RainWindow window) => window == RainWindow.Today ? "Today" : "Next hour";

    /// The tooltip's lines before the provider's, each part only when chosen and given: "Place · sky", "12° (feels 10°) · H 14° L 8°",
    /// "Rain 70% next hour · Wind 18 km/h · Humidity 60%", "Sunrise 07:12 · Sunset 16:05", "15:00 6° · 16:00 5° · 17:00 5°".
    public IEnumerable<GadgetTipLine> Tip(string place, IReadOnlyCollection<WeatherDetail> shows, RainWindow rain)
    {
        if (shows.Contains(WeatherDetail.PlaceSky)) yield return new GadgetTipLine(now is null ? place : $"{place} · {Sky(now.Sky)}");
        if (now is null) yield break;
        var feels = shows.Contains(WeatherDetail.FeelsLike) && now.FeelsLike is not null;
        var temperatures = Joined(
            shows.Contains(WeatherDetail.Temperature) ? Degrees(now.Temperature) + (feels ? $" (feels {Degrees(now.FeelsLike)})" : string.Empty)
                : feels ? $"Feels {Degrees(now.FeelsLike)}" : null,
            shows.Contains(WeatherDetail.HighLow) && now.High is not null ? HighLow : null);
        if (temperatures is not null) yield return new GadgetTipLine(temperatures);
        var chance = rain == RainWindow.Today ? now.RainChance : now.RainNextHour;
        var more = Joined(
            shows.Contains(WeatherDetail.RainChance) && chance is not null ? $"Rain {Percent(chance)} {RainWindowName(rain).ToLower(CultureInfo.CurrentCulture)}" : null,
            shows.Contains(WeatherDetail.Wind) && now.WindKmh is not null ? $"Wind {Wind(now.WindKmh)}" : null,
            shows.Contains(WeatherDetail.Humidity) && now.Humidity is not null ? $"Humidity {Percent(now.Humidity)}" : null);
        if (more is not null) yield return new GadgetTipLine(more);
        if (shows.Contains(WeatherDetail.Sun) && now.Sunrise is { } rise && now.Sunset is { } set) yield return new GadgetTipLine($"Sunrise {Clock(rise)} · Sunset {Clock(set)}");
        if (shows.Contains(WeatherDetail.NextHours) && now.Hours.Count > 0)
            yield return new GadgetTipLine(string.Join(" · ", now.Hours.Select(h => $"{Clock(h.At)} {Degrees(h.Temperature)}")));
    }

    private static string? Joined(params string?[] parts) => parts.OfType<string>().ToList() is { Count: > 0 } list ? string.Join(" · ", list) : null;

    private static string Clock(DateTime at) => at.ToString("t", CultureInfo.CurrentCulture);
}
