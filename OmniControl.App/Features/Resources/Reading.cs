using System.Globalization;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources;

public enum ReadingKind { Percent, Speed, Temperature }

/// THE live list of readings: the built-in ones (<see cref="Reading.All"/>) and each physical disk's (<see cref="Reading.OfDisk"/>), owned by the
/// poller (UI thread).
public interface IReadingList
{
    IReadOnlyList<Reading> Readings { get; }

    Reading? Find(string id);

    /// A disk was added or removed (UI thread).
    event Action? ReadingsChanged;
}

/// THE master list of readings: each is a state, a card value, a gadget row and a menu line, and carries its gadget defaults, its read part, its page card,
/// what it is read from (the adapter or sensor's name, when the read names one) and the icons its label editor and icon picker suggest first.
public sealed record Reading(string Id, string Name, string Chip, string Icon, ReadingKind Kind, SampleParts Part, Func<ResourceSample, double?> Read, string GadgetLabel,
    string GadgetIcons, bool GadgetDefault = false, double? WarnDefault = null, (string Label, int Order)? Card = null,
    Func<ResourceSample, string?>? Source = null)
{
    /// A temperature from which a sensor is called warm (the Hottest sensor card's caution tag).
    public const double WarmCelsius = 70;

    /// The ids of the readings whose temperature sensor can be chosen (<see cref="ResourcesSetup.SensorOf"/>), and the fixed range of the Hottest
    /// sensor's small graph, in Celsius.
    public const string HottestId = "hottest", CpuTempId = "cputemp", GpuTempId = "gputemp";
    public const double TemperatureGraphMin = 25, TemperatureGraphMax = 100;

    private const double Kilo = 1024;

    /// The most whole digits a speed shows before Auto moves to the next unit, so its widest value is "999".
    private const double SpeedDigitsTop = 1000;

    /// The icons suggested first for its label, best first (the label editor puts the group holding the first one at the top).
    public IReadOnlyList<string> SuggestedIcons { get; init; } = Array.Empty<string>();

    /// The temperature sensor the value came from, for a reading whose sensor can be chosen (<see cref="ResourcesSetup.SensorOf"/>); null: it cannot.
    public Func<ResourceSample, TemperatureSensor?>? Sensor { get; init; }

    private static readonly string[] Thermometers = { "temperature", "thermometer", "temperature-celsius" };

    /// The GPU's icon in its fixed colour (the GPU usage and temperature labels).
    private static readonly string Gpu = Gadget.GadgetLabel.Token("device-desktop-analytics", Gadget.GadgetLabel.GpuColour);

    public static readonly IReadOnlyList<Reading> All = new Reading[]
    {
        new("upload", "Upload", "↑ Upload", "upload", ReadingKind.Speed, SampleParts.Network, s => s.Upload, "↑:", Gadget.GadgetLabel.Token("upload", Gadget.GadgetLabel.UploadColour), GadgetDefault: true, Card: ("Upload", 5),
            Source: s => s.Adapter) { SuggestedIcons = new[] { "upload", "arrow-up", "cloud-upload" } },
        new("download", "Download", "↓ Download", "download", ReadingKind.Speed, SampleParts.Network, s => s.Download, "↓:", Gadget.GadgetLabel.Token("download", Gadget.GadgetLabel.DownloadColour), GadgetDefault: true,
            Card: ("Download", 4), Source: s => s.Adapter) { SuggestedIcons = new[] { "download", "arrow-down", "cloud-download" } },
        new("cpu", "CPU usage", "CPU", "cpu", ReadingKind.Percent, SampleParts.None, s => s.Cpu, "C:", "{cpu}", GadgetDefault: true, WarnDefault: 90, Card: ("CPU", 0))
            { SuggestedIcons = new[] { "cpu", "cpu-2" } },
        new("gpu", "GPU usage", "GPU", "device-desktop-analytics", ReadingKind.Percent, SampleParts.Gpu, s => s.Gpu, "G:", Gpu, GadgetDefault: true,
            Card: ("GPU", 1)) { SuggestedIcons = new[] { "device-desktop-analytics", "cpu-2" } },
        new("memory", "Memory", "Memory", "memory-stick", ReadingKind.Percent, SampleParts.None, s => s.MemoryPercent, "M:", "{memory-stick}", Card: ("Memory", 2))
            { SuggestedIcons = new[] { "memory-stick", "brain", "database" } },
        new(CpuTempId, "CPU temperature", "CPU °C", "temperature", ReadingKind.Temperature, SampleParts.CpuTemp, s => s.CpuTemp, "T:", "{cpu}" + Gadget.GadgetLabel.Token("temperature", Gadget.GadgetLabel.WarmColour),
            GadgetDefault: true, WarnDefault: 85, Source: s => s.CpuTempSensor?.Name) { SuggestedIcons = Thermometers.Append("cpu").ToArray(), Sensor = s => s.CpuTempSensor },
        new(GpuTempId, "GPU temperature", "GPU °C", "temperature", ReadingKind.Temperature, SampleParts.GpuTemp, s => s.GpuTemp, "T:",
            Gpu + Gadget.GadgetLabel.Token("temperature", Gadget.GadgetLabel.HotColour), GadgetDefault: true, WarnDefault: 85, Source: s => s.GpuTempSensor?.Name)
            { SuggestedIcons = Thermometers.Append("device-desktop-analytics").ToArray(), Sensor = s => s.GpuTempSensor },
        new("disk", "Disk activity", "Disk", "disc", ReadingKind.Percent, SampleParts.Disk, s => s.Disk, "D:", "{disc}")
            { SuggestedIcons = new[] { "disc", "database" } },
        new(HottestId, "Hottest sensor", "Hottest", "flame", ReadingKind.Temperature, SampleParts.Hottest, s => s.Hottest, "H:", "{flame}",
            Card: ("Hottest sensor", 3), Source: s => s.HottestName) { SuggestedIcons = Thermometers.Prepend("flame").ToArray(), Sensor = s => s.HottestSensor },
    };

    /// A built-in reading; with each physical disk's own: <see cref="IReadingList.Find"/>.
    public static Reading? Find(string id) => All.FirstOrDefault(r => r.Id == id);

    /// A physical disk's own readings, after the built-in ones in the live list (<see cref="IReadingList"/>): its active time and its temperature
    /// (unknown, "—", when the drive reports none). Their ids follow the disk, not its number, so a row keeps its settings.
    public static IEnumerable<Reading> OfDisk(PhysicalDisk disk)
    {
        DiskSample? Of(ResourceSample s) => s.Disks.FirstOrDefault(d => d.Number == disk.Number);
        var name = $"Disk {disk.Number}";
        yield return new($"disk.{disk.Id}", $"{name} activity ({disk.Model})", name, "disc", ReadingKind.Percent, SampleParts.Disk, s => Of(s)?.Activity,
            $"D{disk.Number}:", "{disc}") { SuggestedIcons = new[] { "disc", "database" } };
        yield return new($"disktemp.{disk.Id}", $"{name} temperature ({disk.Model})", name + " °C", "temperature", ReadingKind.Temperature, SampleParts.DiskTemp,
            s => Of(s)?.Temperature, $"T{disk.Number}:", "{disc}" + Gadget.GadgetLabel.Token("temperature", Gadget.GadgetLabel.WarmColour), WarnDefault: WarmCelsius)
            { SuggestedIcons = Thermometers.Append("disc").ToArray() };
    }

    /// The parts of a read these readings need together.
    public static SampleParts PartsOf(IEnumerable<Reading> readings) => readings.Aggregate(SampleParts.None, (all, r) => all | r.Part);

    /// The id of its state in the master list of states.
    public string StateId => "resources." + Id;

    /// The unit of its state and of a typed warn point: speeds in KB/s.
    public string Unit => Kind switch { ReadingKind.Speed => "KB/s", ReadingKind.Temperature => "°C", _ => "%" };

    /// The highest value its state can have (a speed is capped at 10 GB/s).
    public int Max => Kind switch { ReadingKind.Speed => 10_000_000, ReadingKind.Temperature => 150, _ => 100 };

    /// The value in its <see cref="Unit"/> (a speed's bytes per second as KB/s).
    public double? InUnit(ResourceSample sample) => Read(sample) is { } v ? Kind == ReadingKind.Speed ? v / Kilo : v : null;

    /// A value in its <see cref="Unit"/> as words: "90 %", "500 KB/s" (a warn point).
    public string UnitWords(double value) => $"{value:0} {Unit}";

    /// The top of its small graph: a load's 100; 0 (the largest value shown) for speeds and temperatures.
    public double GraphMax => Kind == ReadingKind.Percent ? Max : 0;

    /// The page's words for a value: "23 %", "71 °C", "4.1 MB/s", "—" when unknown.
    public string Words(double? value) => value is not { } v ? Unknown : Kind switch
    {
        ReadingKind.Speed => SpeedWords(v),
        ReadingKind.Temperature => Celsius(v),
        _ => Percent(v),
    };

    private static string Percent(double value) => $"{value:0} %";

    private static string Whole(double value) => value.ToString("0", CultureInfo.CurrentCulture);

    /// A temperature as the page writes it: "71 °C".
    public static string Celsius(double value) => $"{value:0} °C";

    /// The gadget's words for a value with its settings: a speed in its unit ("0.6 K/s"), a load or temperature as a whole number, with its unit when the look asks ("13 %"; a temperature is always Celsius).
    public string GadgetWords(double? value, GadgetSettings look) => value is not { } v ? Unknown : Kind == ReadingKind.Speed
        ? Speed(v, look.Units, look.Bits, null, SpeedSuffix(look))
        : Kind == ReadingKind.Temperature ? Temperature(Whole(v), look.Temperatures) : look.ShowPercentUnits ? Percent(v) : Whole(v);

    /// The temperature styles' names, in <see cref="TemperatureSign"/> order (the Numbers card's chip menu and the Weather group's choice).
    public static readonly IReadOnlyList<string> SignWords = new[] { "None", "°", "°C" };

    /// A written temperature number in the gadget's style: "54", "54°" or "54 °C" (<paramref name="scale"/> F for Fahrenheit).
    public static string Temperature(string number, TemperatureSign sign, char scale = 'C') => sign switch
    {
        TemperatureSign.Degree => number + "°",
        TemperatureSign.Unit => $"{number} °{scale}",
        _ => number,
    };

    public const string Unknown = "—";

    /// A typical value as <see cref="Read"/> gives it, shown by the preview and the layout tiles while the reading is unknown or 0 (never "—" or "0" in a sample).
    public double Sample => Kind switch { ReadingKind.Speed => 1.2 * Kilo, ReadingKind.Temperature => 54, _ => 4 };

    /// The value a sample look shows: the latest when it is known and not 0, else <see cref="Sample"/>.
    public double SampleOr(double? latest) => latest is { } v && v != 0 ? v : Sample;

    /// The gadget's widest realistic words for this reading at the look's units ("88.8 K/s", "100 %"), the width a cell keeps free
    /// so values do not jiggle (a speed never shows more than three digits, so "888.8" is never reserved).
    public string GadgetWidest(GadgetSettings look) => Kind == ReadingKind.Speed
        ? SpeedText(88.8, look.Units == SpeedUnit.Mega, null, SpeedSuffix(look))
        : GadgetWords(100, look);

    /// A speed as the page writes it: "0 KB/s", "0.4 KB/s", "320 KB/s", "4.1 MB/s" (one decimal below 10 KB/s and between 1 and 10 MB/s, so a
    /// program's trickle of a few hundred bytes a second never reads as 0).
    public static string SpeedWords(double bytesPerSecond) =>
        Speed(bytesPerSecond, SpeedUnit.Auto, bits: false, bytesPerSecond is > 0 and < 10 * Kilo || bytesPerSecond is >= Kilo * Kilo and < 10 * Kilo * Kilo ? 1 : 0, "B/s");

    private static string? SpeedSuffix(GadgetSettings look) => look.ShowSpeedUnits ? (look.Bits ? "b/s" : "/s") : null;

    /// A speed in kilo or mega (bytes per 1024, bits per 1000), <paramref name="suffix"/> after the K or M (null: no unit). Auto moves to mega
    /// once the kilo words would need a fourth whole digit. <paramref name="decimals"/> null = the gadget's three-digit rule (see <see cref="SpeedText"/>).
    private static string Speed(double bytesPerSecond, SpeedUnit unit, bool bits, int? decimals, string? suffix)
    {
        double step = bits ? 1000 : Kilo, value = (bits ? bytesPerSecond * 8 : bytesPerSecond) / step;
        var mega = unit == SpeedUnit.Mega || (unit == SpeedUnit.Auto && Math.Round(value, decimals ?? 0) >= SpeedDigitsTop);
        return SpeedText(mega ? value / step : value, mega, decimals, suffix);
    }

    /// Fixed <paramref name="decimals"/>, or (null) three digits: kilo has one decimal under 100 and none from 100 up; mega always has three
    /// digits (two decimals under 10, one under 100, none from 100).
    private static string SpeedText(double value, bool mega, int? decimals, string? suffix)
    {
        var places = decimals ?? (mega && Math.Round(value, 2) < 10 ? 2 : Math.Round(value, 1) < 100 ? 1 : 0);
        var number = value.ToString("F" + places, CultureInfo.CurrentCulture);
        return suffix is null ? number : $"{number} {(mega ? "M" : "K")}{suffix}";
    }
}
