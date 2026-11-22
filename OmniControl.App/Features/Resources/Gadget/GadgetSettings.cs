using System.Text.Json;
using System.Text.Json.Serialization;
using OmniControl.App.Features.AppLists.Apps;
using OmniControl.Core.Engine.Weather;

namespace OmniControl.App.Features.Resources;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpeedUnit { Auto, Kilo, Mega }

/// How the gadget writes a temperature: the number only, a degree sign right after it, or the degree and its unit ("54", "54°", "54 °C").
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TemperatureSign { None, Degree, Unit }

/// Where a reading that does not fill the last column goes: centred in the gadget's height, larger over all lines, or in the top line.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OddReading { Centre, Larger, FillTop }

/// Where on the taskbar the gadget sits: left of the tray, or after the buttons at the taskbar's left end (<see cref="Core.Engine.TaskbarButtons"/>).
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GadgetPosition { Right, Left }

/// One part of the weather the gadget can show, in the order the gadget shows them.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeatherPart { Icon, Temperature, FeelsLike, RainChance, Wind, Humidity, PlaceName, HighLow }

/// How the weather's place is named on the gadget and in its tooltip: the city only, the name as found, or a name the user typed.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlaceNameStyle { Short, Full, Nickname }

/// One detail a reading's hover tooltip can show.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReadingDetail { FullName, Value, TodayRange, Graph, Source, WarnPoint }

/// One detail the weather's hover tooltip can show.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeatherDetail { PlaceSky, Temperature, FeelsLike, HighLow, RainChance, Wind, Humidity, Sun, NextHours, Provider }

/// Which rain chance the weather's tooltip gives: the coming hour's or today's highest.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RainWindow { NextHour, Today }

/// What hovering the gadget shows: a tooltip for the readings part and one for the weather part, each made of the chosen details.
public sealed class GadgetHover
{
    public bool Readings { get; set; } = true;

    /// True: hovering any reading lists every shown reading; false: only the one under the pointer.
    public bool AllReadings { get; set; }

    public List<ReadingDetail> ReadingShows { get; set; } = new() { ReadingDetail.FullName, ReadingDetail.Value, ReadingDetail.TodayRange, ReadingDetail.Source };

    public bool Weather { get; set; } = true;

    public List<WeatherDetail> WeatherShows { get; set; } = Enum.GetValues<WeatherDetail>().Except(new[] { WeatherDetail.Sun, WeatherDetail.NextHours }).ToList();

    public RainWindow Rain { get; set; } = RainWindow.NextHour;
}

/// One reading as the gadget shows it: shown, its label (<see cref="Gadget.GadgetLabel"/>), warn point (null: never) and colour, the room after it.
public sealed class GadgetReading
{
    public string Id { get; set; } = string.Empty;
    public bool Shown { get; set; }
    public string Label { get; set; } = string.Empty;
    public double? WarnAt { get; set; }
    public string Colour { get; set; } = string.Empty;

    /// Its words' colour; "" = automatic (the gadget's text colour, the taskbar's own unless chosen); the warn colour wins past the warn point.
    public string TextColour { get; set; } = string.Empty;
    public double GapAfter { get; set; }
}

/// The gadget's weather: provider, place (typed or Windows' location), units, interval and parts; the OpenWeatherMap key lives in Windows' credential store.
public sealed class GadgetWeather
{
    [JsonConverter(typeof(JsonStringEnumConverter<WeatherSource>))]
    public WeatherSource Source { get; set; } = WeatherSource.OpenMeteo;

    public string Place { get; set; } = string.Empty;
    public PlaceNameStyle NameStyle { get; set; } = PlaceNameStyle.Short;
    public string Nickname { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool UseLocation { get; set; }
    public bool Imperial { get; set; }
    public double UpdateMinutes { get; set; } = Timing.WeatherUpdateEvery.TotalMinutes;
    public List<WeatherPart> Shows { get; set; } = new() { WeatherPart.Icon, WeatherPart.Temperature, WeatherPart.PlaceName };

    /// How the gadget writes the weather's temperatures ("6", "6°", "6 °C").
    public TemperatureSign Temperatures { get; set; } = TemperatureSign.Degree;

    [JsonIgnore] public bool HasPlace => UseLocation || (Latitude is not null && Longitude is not null);
}

/// The taskbar gadget's look and behaviour (the Resources page's Taskbar gadget tab); a new setup's is the Classic layout (<see cref="Gadget.GadgetLayouts.NewLook"/>).
public sealed class GadgetSettings
{
    public const int MinLines = 1, MaxLines = 3,MinTextSize = 6, MaxTextSize = 16, MaxColumnGap = 40, MaxLineGap = 12, MaxGapAfter = 40, MinSpaceToTray = -16, MaxSpaceToTray = 40;

    /// The positions' words, in <see cref="GadgetPosition"/> order (the tab's Position card and the gadget's menu).
    public static readonly IReadOnlyList<string> PositionNames = Enum.GetNames<GadgetPosition>();

    /// The weather's row among the readings (it is not a reading of the PC, so it is not in <see cref="Reading.All"/>).
    public const string WeatherId = "weather";

    /// How much larger an odd reading drawn over all lines (<see cref="OddReading.Larger"/>) is than the rest.
    public const double OddLargerScale = 1.5;

    /// The colours the gadget offers: "" (the taskbar's own text colour) then the drawing colours.
    public static readonly IReadOnlyList<string> Colours = Palette.Drawing.Select(c => c.ToString()).Prepend(string.Empty).ToList();

    /// The names of <see cref="Colours"/>, in the same order ("Taskbar text", then the drawing colours' names).
    public static readonly IReadOnlyList<string> ColourNames = Palette.DrawingTitles.Prepend("Taskbar text").ToList();

    /// The names of a reading's text colours: <see cref="ColourNames"/> with "" as automatic.
    public static readonly IReadOnlyList<string> TextColourNames = ColourNames.Skip(1).Prepend("Automatic (taskbar text)").ToList();

    /// The font menus' name of <see cref="Font"/> "" (the app's own font).
    public const string DefaultFontName = "Default (Segoe UI)";

    /// How many fonts the gadget's right-click Font menu remembers.
    public const int MaxRecentFonts = 5;

    public bool Shown { get; set; }
    public int Lines { get; set; } = 2;
    public OddReading Odd { get; set; } = OddReading.Centre;
    public double TextSize { get; set; } = 9;
    public double ColumnGap { get; set; } = 3;
    public double LineGap { get; set; }

    /// The free space between the gadget's right edge and the tray, in pixels; below 0 it tucks into the padding Windows leaves around the tray's "^" button.
    public double SpaceToTray { get; set; }

    /// Which end of the taskbar; one for every layout (a layout keeps it, as it keeps shown or not).
    public GadgetPosition Position { get; set; }

    /// The font family's name, "" = <see cref="DefaultFontName"/>; one for every layout, like the position.
    public string Font { get; set; } = string.Empty;

    /// The fonts picked before, latest first (at most <see cref="MaxRecentFonts"/>; the right-click Font menu).
    public List<string> RecentFonts { get; set; } = new();

    /// The menus' name of <paramref name="font"/>.
    public static string FontName(string font) => font.Length > 0 ? font : DefaultFontName;

    /// Uses <paramref name="font"/> and remembers it first among <see cref="RecentFonts"/>.
    public void UseFont(string font)
    {
        Font = font;
        if (font.Length == 0) return;
        RecentFonts.RemoveAll(f => string.Equals(f, font, StringComparison.OrdinalIgnoreCase));
        RecentFonts.Insert(0, font);
        if (RecentFonts.Count > MaxRecentFonts) RecentFonts.RemoveRange(MaxRecentFonts, RecentFonts.Count - MaxRecentFonts);
    }

    public bool HoverOutline { get; set; }
    public bool SolidBackground { get; set; }
    public SpeedUnit Units { get; set; } = SpeedUnit.Auto;
    public bool Bits { get; set; }

    /// Whether speeds and loads show their unit (K/s, %); speeds keep the name of the old "Show units" switch so old files load.
    [JsonPropertyName("ShowUnits")] public bool ShowSpeedUnits { get; set; }
    public bool ShowPercentUnits { get; set; }

    /// How the PC's temperatures are written ("54", "54°", "54 °C").
    public TemperatureSign Temperatures { get; set; }

    /// The old on/off temperature unit, read from older files only: on is "°C", off none.
    [JsonPropertyName("ShowTemperatureUnits"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OldTemperatureUnits
    {
        get => null;
        set { if (value is { } on) Temperatures = on ? TemperatureSign.Unit : TemperatureSign.None; }
    }
    public string TextColour { get; set; } = string.Empty;
    public double ReadEverySeconds { get; set; } = Timing.ResourcesGadgetReadEvery.TotalSeconds;
    public string ClickAction { get; set; } = Tray.TrayFlyoutModel.ToggleActionId;
    /// True once a file saved with the old default click (Open Resources) was moved to the current default, so a later pick of it stays.
    public bool ClickDefaultMoved { get; set; }

    /// A file that still holds the old default click never chose it: it takes the current default, once.
    public void MoveOldClickDefault()
    {
        if (ClickDefaultMoved) return;
        ClickDefaultMoved = true;
        if (ClickAction == ResourcesModule.OpenActionId) ClickAction = new GadgetSettings().ClickAction;
    }

    public string DoubleClickAction { get; set; } = AppCatalogue.TaskManager.ActionId;

    /// The gadget's own double-click time in ms; 0 follows Windows' double-click time.
    public int DoubleClickMs { get; set; }
    public GadgetWeather Weather { get; set; } = new();
    public GadgetHover Hover { get; set; } = new();

    /// The readings in the gadget's order, the shown ones first (the master list's order until the user drags them), each with its own look.
    public List<GadgetReading> Readings { get; set; } = Reading.All.Select(Default).Append(WeatherRow()).ToList();

    /// The weather's row, or null in a file that lost it (<see cref="Complete"/> puts it back).
    [JsonIgnore] public GadgetReading? WeatherShown => Readings.FirstOrDefault(r => r.Id == WeatherId && r.Shown);

    /// Adds the readings a saved file does not have yet (a newer version's, or of <paramref name="readings"/>: the live list with this PC's
    /// disks) and keeps the shown ones first, in their order.
    public void Complete(IEnumerable<Reading>? readings = null)
    {
        Readings.AddRange((readings ?? Reading.All).Where(r => Readings.All(g => g.Id != r.Id)).Select(Default));
        if (Readings.All(g => g.Id != WeatherId)) Readings.Add(WeatherRow());
        Readings = Readings.OrderBy(r => !r.Shown).ToList();
    }

    /// The look of <paramref name="from"/> (a layout) put into these settings, keeping shown or not, the position, the font, the click actions, the hover
    /// details, the space to the tray, the weather's place and, hidden, the rows the layout lacks (this PC's disks).
    public void TakeLook(GadgetSettings from)
    {
        var keep = Copy();
        SettingsCopy.Into(this, from.Copy());
        (Shown, Position, Font, RecentFonts, ClickAction, ClickDefaultMoved, DoubleClickAction, DoubleClickMs, Hover, SpaceToTray) =
            (keep.Shown, keep.Position, keep.Font, keep.RecentFonts, keep.ClickAction, keep.ClickDefaultMoved, keep.DoubleClickAction, keep.DoubleClickMs, keep.Hover, keep.SpaceToTray);
        (keep.Weather.Shows, keep.Weather.Temperatures) = (Weather.Shows, Weather.Temperatures);
        Weather = keep.Weather;
        foreach (var row in keep.Readings.Where(k => Readings.All(r => r.Id != k.Id)))
        {
            row.Shown = false;
            Readings.Add(row);
        }
        Complete();
    }

    /// True when the look of these settings is the look of <paramref name="other"/> (what <see cref="TakeLook"/> copies).
    public bool SameLook(GadgetSettings other)
    {
        var copy = Copy();
        copy.TakeLook(other);
        return JsonSerializer.Serialize(copy) == JsonSerializer.Serialize(this);
    }

    public GadgetSettings Copy() => JsonSerializer.Deserialize<GadgetSettings>(JsonSerializer.Serialize(this))!;

    private static GadgetReading Default(Reading r) => new()
    {
        Id = r.Id, Shown = r.GadgetDefault, Label = Gadget.GadgetLabel.Of(Gadget.LabelStyle.Icons, r), WarnAt = r.WarnDefault, Colour = r.WarnDefault is null ? string.Empty : Palette.Danger.ToString(),
    };

    private static GadgetReading WeatherRow() => new() { Id = WeatherId };
}
