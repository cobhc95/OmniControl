using System.Globalization;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Resources.Gadget;

/// What the Ready-made layouts gallery says about a layout: its one-line description, its category (null: the user's own) and whether it is one of the
/// favourite tiles the folded gallery shows.
public sealed record LayoutAbout(string Description, string? Category, bool Favourite);

/// The gadget's layouts as a preset group: the built-in ones, then the user's, each the look only (<see cref="GadgetSettings.TakeLook"/>).
public sealed class GadgetLayouts : ListPresetProvider<GadgetLayoutPreset>
{
    public const string GroupId = "resources.gadget.layouts";

    /// The built-in layouts' categories, in the gallery's order (its filter chips and sections).
    public const string Everyday = "Everyday", Gaming = "Gaming and heat", Network = "Network", Weather = "Weather", Minimal = "Minimal";

    public static readonly IReadOnlyList<string> Categories = new[] { Everyday, Gaming, Network, Weather, Minimal };

    private const string Classic = "gadget.classicicons";

    /// The text size of the Big numbers layout.
    private const double BigTextSize = 12;

    private readonly ResourcesSettings _settings;

    private sealed record BuiltInLayout(string Id, string Name, string Description, string Category, bool Favourite, GadgetSettings Look);

    /// THE built-in layouts: name, description, category and whether the folded gallery shows it as a tile. Ids never change (saved choices and
    /// shortcuts follow them).
    private static readonly IReadOnlyList<BuiltInLayout> BuiltIn = new BuiltInLayout[]
    {
        new(Classic, "Classic", "Network, CPU, hottest sensor and weather", Everyday, true, Made(g =>
        {
            Show(g, "upload", "download", "cpu", "hottest", GadgetSettings.WeatherId);
            Plain(g);
            WiderColumns(g);
            g.Weather.Shows = new() { WeatherPart.Icon, WeatherPart.Temperature, WeatherPart.PlaceName };
        })),
        new("gadget.classicmemory", "Classic with memory", "Network, CPU, hottest sensor, memory and weather", Everyday, true, Made(g =>
        {
            Show(g, "upload", "download", "cpu", "hottest", "memory", GadgetSettings.WeatherId);
            Plain(g);
            WiderColumns(g);
            Label(g, "memory", GadgetLabel.Token("device-sd-card"));
            g.Weather.Shows = new() { WeatherPart.Icon, WeatherPart.Temperature };
        })),
        new("gadget.essentials", "Essentials", "Network, CPU and heat", Everyday, true, Made(g =>
        {
            Show(g, "upload", "download", "cpu", "cputemp");
            Label(g, "cputemp", GadgetLabel.Token("temperature", GadgetLabel.WarmColour));
        })),
        new("gadget.classic", "Classic text", "Letters, no icons", Everyday, false, Made(g =>
        {
            Show(g, "upload", "download", "cpu", "cputemp");
            Labels(g, LabelStyle.Text, Reading.Find);
            g.ColumnGap = 14;
        })),
        new("gadget.workstation", "Workstation", "CPU, memory, disk and heat", Everyday, false, Made(g =>
        {
            Show(g, "cpu", "memory", "disk", "cputemp");
            Label(g, "cputemp", GadgetLabel.Token("temperature", GadgetLabel.WarmColour));
            g.ShowPercentUnits = true;
        })),
        new("gadget.systemload", "System load", "CPU, GPU, memory and disk", Everyday, false, Made(g =>
        {
            Show(g, "cpu", "gpu", "memory", "disk");
            g.ShowPercentUnits = true;
        })),
        new("gadget.gaming", "Gaming", "CPU and GPU heat and load", Gaming, true, Made(g => Show(g, "cputemp", "gputemp", "cpu", "gpu"))),
        new("gadget.temperatures", "Thermals", "Every temperature", Gaming, false, Made(g => Show(g, "cputemp", "gputemp", "hottest"))),
        new("gadget.hotornot", "Hot or not", "The hottest sensor and the CPU", Gaming, false, Made(g =>
        {
            Show(g, "hottest", "cputemp");
            Label(g, "hottest", GadgetLabel.Token("flame", GadgetLabel.HotColour));
            g.Temperatures = TemperatureSign.Unit;
        })),
        new("gadget.streamer", "Streamer", "Upload first, CPU and GPU load", Gaming, false, Made(g =>
        {
            Show(g, "upload", "cpu", "gpu");
            Label(g, "upload", GadgetLabel.Token("cloud-upload", GadgetLabel.UploadColour));
        })),
        new("gadget.network","Network", "Upload and download", Network, true, Made(g =>
        {
            Show(g, "upload", "download");
            Label(g, "upload", GadgetLabel.Token("cloud-upload", GadgetLabel.UploadColour));
            Label(g, "download", GadgetLabel.Token("cloud-download", GadgetLabel.DownloadColour));
        })),
        new("gadget.networktiny", "Network tiny", "Small text, chevrons", Network, false, Made(g =>
        {
            Show(g, "upload", "download");
            Label(g, "upload", GadgetLabel.Token("chevron-up"));
            Label(g, "download", GadgetLabel.Token("chevron-down"));
            g.TextSize = GadgetSettings.MinTextSize + 1;
        })),
        new("gadget.downloadwatch", "Download watcher", "Download first, upload after it", Network, false, Made(g =>
        {
            Show(g, "download", "upload");
            Label(g, "download", GadgetLabel.Token("cloud-download", GadgetLabel.DownloadColour));
            Label(g, "upload", GadgetLabel.Token("chevron-up"));
            g.Lines = 1;
        })),
        new("gadget.weather", "With weather", "Essentials plus the weather", Weather, false, Made(g =>
        {
            Show(g, "upload", "download", "cpu", "cputemp", GadgetSettings.WeatherId);
            Label(g, "cputemp", GadgetLabel.Token("temperature", GadgetLabel.WarmColour));
        })),
        new("gadget.weatheronly", "Weather only", "Sky, temperature, feels like and rain", Weather, false, Made(g =>
        {
            Show(g, GadgetSettings.WeatherId);
            g.Weather.Shows = new() { WeatherPart.Icon, WeatherPart.Temperature, WeatherPart.FeelsLike, WeatherPart.RainChance };
        })),
        new("gadget.forecast", "Forecast", "Weather with high, low and rain", Weather, false, Made(g =>
        {
            Show(g, GadgetSettings.WeatherId);
            g.Weather.Shows = new() { WeatherPart.Icon, WeatherPart.Temperature, WeatherPart.RainChance, WeatherPart.HighLow };
        })),
        new("gadget.oneline", "Minimal line", "One line, no colour", Minimal, false, Made(g =>
        {
            Show(g, "upload", "download", "cpu", "cputemp");
            Plain(g);
            g.Lines = 1;
        })),
        new("gadget.bignumbers", "Big numbers", "One line, larger text", Minimal, false, Made(g =>
        {
            Show(g, "upload", "download", "cpu", "cputemp");
            Plain(g);
            (g.Lines, g.TextSize) = (1, BigTextSize);
        })),
        new("gadget.colourful", "Colourful", "Every icon tinted", Minimal, false, Made(g =>
        {
            Show(g, "upload", "download", "cpu", "gpu", "cputemp", "gputemp");
            Label(g, "cpu", GadgetLabel.Token("cpu", GadgetLabel.CpuColour));
            Label(g, "cputemp", GadgetLabel.Token("temperature", GadgetLabel.WarmColour));
        })),
    };

    public GadgetLayouts(ResourcesSettings settings) : base(new PresetList<GadgetLayoutPreset>(settings.Layouts, settings.Save), BuiltIn.Count)
    {
        _settings = settings;
        settings.Changed += NotifyChanged; // the live look may now be (or no longer be) a layout
    }

    /// A new gadget's look (and Restore defaults'): the Classic layout.
    public static GadgetSettings NewLook() => BuiltIn.First(b => b.Id == Classic).Look.Copy();

    private GadgetSettings Look => _settings.Setup.Gadget;

    public override PresetWords Words { get; } = new("Apply", "Apply gadget layout", "+ Save current as a layout",
        "Keeps the gadget's readings, labels, lines, spacing, text and numbers; not whether it is shown, its click actions or the weather's place");

    protected override IReadOnlyList<PresetEntry> MakeEntries() =>
        BuiltIn.Select(b => new PresetEntry(b.Id, b.Name) { InUse = Look.SameLook(b.Look), BuiltIn = true, CanRename = false, CanDelete = false, CanMove = false })
            .Concat(List.Items.Select(p => new PresetEntry(p.Id, p.Name) { InUse = Look.SameLook(p.Look) })).ToList();

    /// The gallery's words for a layout: a built-in one's own, or "Saved" and the day for the user's.
    public LayoutAbout About(string id) => BuiltIn.FirstOrDefault(b => b.Id == id) is { } b
        ? new LayoutAbout(b.Description, b.Category, b.Favourite)
        : new LayoutAbout(List.Items.FirstOrDefault(p => p.Id == id)?.Saved is { } saved ? "Saved " + saved.ToString("d", CultureInfo.CurrentCulture) : "Your own layout", null, false);

    /// The favourite layouts (the folded gallery's tiles), each with whether it is the live look.
    public IReadOnlyList<PresetEntry> FavouriteEntries() => Entries.Where(e => About(e.Id).Favourite).ToList();

    /// The name of the layout the live look is, or "Custom" when it is none.
    public string CurrentName => Entries.FirstOrDefault(e => e.InUse)?.Name ?? "Custom";

    /// The look a layout holds (the layout tiles draw it); null when it is gone.
    public GadgetSettings? LookOf(string id) => BuiltIn.FirstOrDefault(b => b.Id == id)?.Look ?? List.Items.FirstOrDefault(p => p.Id == id)?.Look;

    public override string? Apply(string id)
    {
        if (LookOf(id) is not { } look) return "That layout is gone.";
        Look.TakeLook(look);
        _settings.Commit();
        return null;
    }

    public override PresetChange Add()
    {
        var name = PresetRules.FirstFree("Layout", Entries.Select(e => e.Name).ToList());
        var preset = new GadgetLayoutPreset { Name = name, Look = Look.Copy(), Saved = DateTime.Now };
        List.Save(preset);
        return PresetChange.Made(preset.Id);
    }

    private static GadgetSettings Made(Action<GadgetSettings> shape)
    {
        var look = new GadgetSettings();
        shape(look);
        look.ShowSpeedUnits = look.Readings.Any(r => r.Shown && Reading.Find(r.Id)?.Kind == ReadingKind.Speed); // every layout with a speed shows its unit
        look.Complete();
        return look;
    }

    /// Shows exactly these rows, in this order.
    private static void Show(GadgetSettings look, params string[] ids)
    {
        foreach (var row in look.Readings) row.Shown = ids.Contains(row.Id);
        look.Readings = look.Readings.OrderBy(r => Array.IndexOf(ids, r.Id) is var i && i >= 0 ? i : ids.Length).ToList();
    }

    /// Gives one reading's row its own label.
    private static void Label(GadgetSettings look, string id, string label) => look.Readings.First(r => r.Id == id).Label = label;

    /// Uncoloured arrows and icons for the network and CPU temperature rows (Classic and the one-line layouts).
    /// Half a digit's width (about 0.55 em) more than the default column gap, at the look's text size (pt to px at 96 dpi).
    private static void WiderColumns(GadgetSettings look) =>
        look.ColumnGap += Math.Round(look.TextSize * 96 / 72 * 0.55 / 2);

    private static void Plain(GadgetSettings look)
    {
        Label(look, "upload", GadgetLabel.Token("arrow-up"));
        Label(look, "download", GadgetLabel.Token("arrow-down"));
        Label(look, "cputemp", GadgetLabel.Token("temperature"));
    }

    /// Gives every reading's row its label in this style (<paramref name="find"/>: the built-in readings, or the live list with the disks).
    public static void Labels(GadgetSettings look, LabelStyle style, Func<string, Reading?> find)
    {
        foreach (var row in look.Readings)
            if (find(row.Id) is { } reading) row.Label = GadgetLabel.Of(style, reading);
    }
}
