using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace OmniControl.Core.Engine.Weather;

public enum WeatherSource { OpenMeteo, MetNorway, OpenWeatherMap }

public enum WeatherSky { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Storm }

/// The weather now, always metric (°C, km/h, %); null where the provider gives no such value. The extras come from the same answer when it has them.
public sealed record WeatherNow(double Temperature, double? FeelsLike, double? RainChance, double? WindKmh, double? Humidity, double? High, double? Low,
    WeatherSky Sky, bool Day)
{
    /// The chance of rain in the coming hour (Open-Meteo's hourly forecast only).
    public double? RainNextHour { get; init; }

    /// Sunrise and sunset today, on the place's clock (Open-Meteo, OpenWeatherMap).
    public DateTime? Sunrise { get; init; }

    public DateTime? Sunset { get; init; }

    /// The next <see cref="WeatherClient.HourCount"/> hours (Open-Meteo, MET Norway); empty when the answer has none.
    public IReadOnlyList<WeatherHour> Hours { get; init; } = Array.Empty<WeatherHour>();
}

/// One coming hour: its time on the clock and its temperature.
public sealed record WeatherHour(DateTime At, double Temperature);

/// A place the geocoding search found: its name, its region and country, and where it is.
public sealed record WeatherPlace(string Name, string Region, double Latitude, double Longitude);

/// THE weather requests: one plain JSON GET per answer over one <see cref="HttpClient"/>, from Open-Meteo, MET Norway or OpenWeatherMap,
/// and Open-Meteo's free geocoding for a typed place. Places are sent rounded to two decimals (about 1 km); nothing here logs a place or a key.
/// Failures surface as the request's own exception (<see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/>,
/// <see cref="JsonException"/>, <see cref="KeyNotFoundException"/>, <see cref="InvalidOperationException"/>) for the caller to word.
public sealed class WeatherClient : IDisposable
{
    /// How many coming hours an answer carries.
    public const int HourCount = 3;

    private const int PlaceCount = 6, DayHours = 24;
    private readonly HttpClient _http;

    /// <param name="userAgent">"Product/version": MET Norway refuses requests without one.</param>
    public WeatherClient(TimeSpan timeout, string userAgent)
    {
        _http = new HttpClient { Timeout = timeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public async Task<WeatherNow> NowAsync(WeatherSource source, double latitude, double longitude, string? key, CancellationToken cancel)
    {
        var (lat, lon) = (Coord(latitude), Coord(longitude));
        return source switch
        {
            WeatherSource.MetNorway => MetNorway(await GetAsync($"https://api.met.no/weatherapi/locationforecast/2.0/compact?lat={lat}&lon={lon}", cancel)),
            WeatherSource.OpenWeatherMap => OpenWeatherMap(await GetAsync(
                $"https://api.openweathermap.org/data/2.5/weather?lat={lat}&lon={lon}&units=metric&appid={Uri.EscapeDataString(key ?? string.Empty)}", cancel)),
            _ => OpenMeteo(await GetAsync($"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}"
                + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,weather_code,is_day"
                + "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset&forecast_days=1"
                + $"&hourly=temperature_2m,precipitation_probability&forecast_hours={HourCount + 1}&timezone=auto", cancel)),
        };
    }

    /// The places whose name starts with <paramref name="name"/> (Open-Meteo geocoding), best first.
    public async Task<IReadOnlyList<WeatherPlace>> FindPlacesAsync(string name, CancellationToken cancel)
    {
        using var doc = await GetAsync($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count={PlaceCount}"
            + $"&language={CultureInfo.CurrentUICulture.TwoLetterISOLanguageName}&format=json", cancel);
        if (!doc.RootElement.TryGetProperty("results", out var results)) return Array.Empty<WeatherPlace>();
        return results.EnumerateArray().Select(r => new WeatherPlace(Text(r, "name"),
            string.Join(", ", new[] { Text(r, "admin1"), Text(r, "country") }.Where(s => s.Length > 0)),
            r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble())).ToList();
    }

    private async Task<JsonDocument> GetAsync(string url, CancellationToken cancel)
    {
        using var response = await _http.GetAsync(url, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancel).ConfigureAwait(false);
    }

    private static WeatherNow OpenMeteo(JsonDocument doc)
    {
        using (doc)
        {
            var now = doc.RootElement.GetProperty("current");
            var day = doc.RootElement.GetProperty("daily");
            // forecast_hours starts the hourly lists at the current hour; each value covers the hour before its time, so index 1 is the coming hour.
            var hourly = doc.RootElement.TryGetProperty("hourly", out var h) ? h : default;
            return new WeatherNow(now.GetProperty("temperature_2m").GetDouble(), Number(now, "apparent_temperature"), At(day, "precipitation_probability_max", 0),
                Number(now, "wind_speed_10m"), Number(now, "relative_humidity_2m"), At(day, "temperature_2m_max", 0), At(day, "temperature_2m_min", 0),
                Wmo(now.GetProperty("weather_code").GetInt32()), Number(now, "is_day") != 0)
            {
                RainNextHour = At(hourly, "precipitation_probability", 1),
                Sunrise = Clock(day, "sunrise", 0),
                Sunset = Clock(day, "sunset", 0),
                Hours = Enumerable.Range(1, HourCount).Select(i => (Clock(hourly, "time", i), At(hourly, "temperature_2m", i)))
                    .Where(x => x.Item1 is not null && x.Item2 is not null).Select(x => new WeatherHour(x.Item1!.Value, x.Item2!.Value)).ToList(),
            };
        }
    }

    private static WeatherNow MetNorway(JsonDocument doc)
    {
        using (doc)
        {
            var series = doc.RootElement.GetProperty("properties").GetProperty("timeseries").EnumerateArray().Take(DayHours).ToList();
            var data = series[0].GetProperty("data");
            var now = data.GetProperty("instant").GetProperty("details");
            var symbol = data.TryGetProperty("next_1_hours", out var hour) ? hour.GetProperty("summary").GetProperty("symbol_code").GetString() ?? string.Empty : string.Empty;
            var temps = series.Select(s => Number(s.GetProperty("data").GetProperty("instant").GetProperty("details"), "air_temperature")).OfType<double>().ToList();
            return new WeatherNow(now.GetProperty("air_temperature").GetDouble(), null, null, Number(now, "wind_speed") * MetresPerSecondToKmh,
                Number(now, "relative_humidity"), temps.Count > 0 ? temps.Max() : null, temps.Count > 0 ? temps.Min() : null, MetSymbol(symbol), !symbol.EndsWith("_night", StringComparison.Ordinal))
            {
                // The series is hourly at first; its times are UTC, shown on this PC's clock.
                Hours = series.Skip(1).Take(HourCount).Select(s => (Time: DateTime.TryParse(Text(s, "time"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : (DateTime?)null,
                        Temp: Number(s.GetProperty("data").GetProperty("instant").GetProperty("details"), "air_temperature")))
                    .Where(x => x.Time is not null && x.Temp is not null).Select(x => new WeatherHour(x.Time!.Value, x.Temp!.Value)).ToList(),
            };
        }
    }

    private static WeatherNow OpenWeatherMap(JsonDocument doc)
    {
        using (doc)
        {
            var main = doc.RootElement.GetProperty("main");
            var weather = doc.RootElement.GetProperty("weather")[0];
            return new WeatherNow(main.GetProperty("temp").GetDouble(), Number(main, "feels_like"), null,
                doc.RootElement.TryGetProperty("wind", out var wind) ? Number(wind, "speed") * MetresPerSecondToKmh : null,
                Number(main, "humidity"), Number(main, "temp_max"), Number(main, "temp_min"), OwmId(weather.GetProperty("id").GetInt32()),
                !Text(weather, "icon").EndsWith('n'))
            {
                Sunrise = Unix(doc.RootElement, "sunrise"),
                Sunset = Unix(doc.RootElement, "sunset"),
            };
        }
    }

    /// An OpenWeatherMap time in sys (Unix seconds) on the place's clock (its "timezone" offset in seconds).
    private static DateTime? Unix(JsonElement root, string name) =>
        root.TryGetProperty("sys", out var sys) && Number(sys, name) is { } seconds
            ? DateTimeOffset.FromUnixTimeSeconds((long)seconds).ToOffset(TimeSpan.FromSeconds(Number(root, "timezone") ?? 0)).DateTime
            : null;

    private const double MetresPerSecondToKmh = 3.6;

    private static string Coord(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static double? Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    /// Item <paramref name="index"/> of a list (null when the list, or the object holding it, is missing or too short).
    private static JsonElement? Item(JsonElement e, string name, int index) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > index ? v[index] : null;

    private static double? At(JsonElement e, string name, int index) => Item(e, name, index) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;

    /// An Open-Meteo local time ("2026-11-19T07:12") from a list.
    private static DateTime? Clock(JsonElement e, string name, int index) =>
        Item(e, name, index) is { ValueKind: JsonValueKind.String } v && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;

    private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    /// WMO weather interpretation codes (Open-Meteo).
    private static WeatherSky Wmo(int code) => code switch
    {
        0 => WeatherSky.Clear,
        1 or 2 => WeatherSky.PartlyCloudy,
        3 => WeatherSky.Cloudy,
        45 or 48 => WeatherSky.Fog,
        >= 51 and <= 57 => WeatherSky.Drizzle,
        >= 71 and <= 77 or 85 or 86 => WeatherSky.Snow,
        >= 95 => WeatherSky.Storm,
        _ => WeatherSky.Rain,
    };

    /// MET Norway's symbol codes ("lightrainshowers_day", "partlycloudy_night", "heavysnowandthunder").
    private static WeatherSky MetSymbol(string symbol) =>
        symbol.Contains("thunder") ? WeatherSky.Storm
        : symbol.Contains("snow") || symbol.Contains("sleet") ? WeatherSky.Snow
        : symbol.Contains("rain") ? WeatherSky.Rain
        : symbol.Contains("fog") ? WeatherSky.Fog
        : symbol.StartsWith("partlycloudy", StringComparison.Ordinal) || symbol.StartsWith("fair", StringComparison.Ordinal) ? WeatherSky.PartlyCloudy
        : symbol.StartsWith("cloudy", StringComparison.Ordinal) ? WeatherSky.Cloudy
        : WeatherSky.Clear;

    /// OpenWeatherMap's condition ids.
    private static WeatherSky OwmId(int id) => id switch
    {
        < 300 => WeatherSky.Storm,
        < 400 => WeatherSky.Drizzle,
        < 600 => WeatherSky.Rain,
        < 700 => WeatherSky.Snow,
        < 800 => WeatherSky.Fog,
        800 => WeatherSky.Clear,
        801 or 802 => WeatherSky.PartlyCloudy,
        _ => WeatherSky.Cloudy,
    };

    public void Dispose() => _http.Dispose();
}
