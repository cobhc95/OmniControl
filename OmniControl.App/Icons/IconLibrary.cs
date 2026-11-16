using OmniControl.App.Services;

namespace OmniControl.App.Icons;

/// <summary>One icon of the library: its id (what is saved: on a tray element, a device, an action's chosen icon), its name and the words it is found by.</summary>
public sealed record LibraryIcon(string Id, string Name, string Keywords)
{
    /// <summary>The key of its drawing in Styles/Icons.axaml.</summary>
    public string Resource => IconLibrary.ResourcePrefix + Id;
}

/// <summary>
/// THE master list of built-in icons (docs/mockups/icon-library.html): 100 outline icons from Tabler Icons (MIT, NOTICE.txt), drawn by
/// <see cref="Controls.IconGlyph"/> from their path data in Styles/Icons.axaml. Every icon picker, suggestion and default reads this list;
/// the user's own icons are kept by <see cref="IconStore"/>. Pure: no UI, so the search and the suggestions are tested without a window.
/// </summary>
public static class IconLibrary
{
    public const string ResourcePrefix = "Icon.";

    /// <summary>How many suggestions the picker shows for an entry.</summary>
    public const int SuggestionCount = 3;

    // Ids named in code (the defaults of entries that have no icon of their own).
    public const string Plug = "plug", Bulb = "bulb", Sliders = "sliders", Mic = "mic", Moon = "moon", Rotate = "rotate", Area = "area",
        Window = "window", Screen = "screen", Desktop = "desktop", Video = "video";

    public static IReadOnlyList<LibraryIcon> All { get; } = new LibraryIcon[]
    {
        new("bulb", "Bulb", "light lamp lighting smart"),
        new("bulb-off", "Bulb off", "light lamp dark unlit"),
        new("lamp", "Lamp", "light desk floor lighting"),
        new("plug", "Plug", "socket outlet power smart"),
        new("strip", "Power strip", "plug socket extension outlet"),
        new("speaker", "Speaker", "sound audio music box"),
        new("tv", "TV", "television screen"),
        new("fan", "Fan", "air cool breeze ventilator"),
        new("air-con", "Air conditioner", "cool climate ac cold"),
        new("heater", "Heater", "heat warm fire radiator"),
        new("kettle", "Kettle", "tea cup hot drink"),
        new("coffee", "Coffee maker", "coffee drink mug"),
        new("camera", "Camera", "photo picture"),
        new("router", "Router", "network wifi internet modem"),
        new("thermometer", "Thermometer", "temperature heat climate"),
        new("desktop", "Computer", "pc monitor desktop screen"),
        new("laptop", "Laptop", "notebook computer"),
        new("phone", "Phone", "mobile smartphone cell"),
        new("switch", "Switch", "toggle on off wall"),
        new("washer", "Washing machine", "laundry wash"),
        new("printer", "Printer", "print paper"),
        new("volume", "Volume", "sound speaker loud audio"),
        new("volume-low", "Volume low", "sound speaker quiet audio"),
        new("mute", "Mute", "sound silent speaker off audio"),
        new("mic", "Microphone", "mic input record voice"),
        new("mic-off", "Microphone off", "mic mute silent input"),
        new("headphones", "Headphones", "headset audio listen"),
        new("music", "Music", "note song audio"),
        new("sliders", "Sliders", "effects fx equaliser eq adjust settings"),
        new("eq", "Equaliser", "effects fx sliders eq adjust"),
        new("wave", "Wave", "sound signal audio frequency"),
        new("sun", "Sun", "brightness light day"),
        new("sun-low", "Brightness low", "dim brightness light"),
        new("moon", "Moon", "night dark sleep"),
        new("sun-moon", "Day and night", "theme light dark auto"),
        new("contrast", "Contrast", "display picture"),
        new("brightness", "Brightness", "display contrast half"),
        new("rotate", "Rotate", "turn orientation portrait landscape"),
        new("eye", "Eye", "view see show"),
        new("palette", "Colours", "colour color paint theme"),
        new("play", "Play", "media start music video"),
        new("pause", "Pause", "media hold music video"),
        new("next", "Next track", "media skip forward music"),
        new("prev", "Previous track", "media back music"),
        new("stop", "Stop", "media end"),
        new("record", "Record", "media capture dot"),
        new("video", "Video", "movie camera film record"),
        new("movie", "Film", "movie video cinema"),
        new("power", "Power", "on off shut down"),
        new("lock", "Lock", "secure locked"),
        new("unlock", "Unlock", "open unlocked"),
        new("sleep", "Sleep", "rest standby night"),
        new("restart", "Restart", "reload again refresh"),
        new("wifi", "Wi-Fi", "wireless network internet"),
        new("bluetooth", "Bluetooth", "wireless pair"),
        new("battery", "Battery", "charge power level"),
        new("charging", "Charging", "battery charge plug"),
        new("keyboard", "Keyboard", "keys type"),
        new("mouse", "Mouse", "pointer click"),
        new("gamepad", "Gamepad", "game controller play"),
        new("folder", "Folder", "files directory"),
        new("app", "App", "program window application"),
        new("terminal", "Terminal", "command console script"),
        new("settings", "Settings", "cog gear options preferences"),
        new("search", "Search", "find magnifier"),
        new("trash", "Trash", "delete remove bin"),
        new("clipboard", "Clipboard", "copy paste"),
        new("area", "Area", "screenshot region crop select"),
        new("window", "Window", "screenshot active window"),
        new("screen", "Screen", "screenshot full screen capture"),
        new("scan", "Scan", "screenshot capture frame"),
        new("qr", "QR code", "code scan"),
        new("photo", "Picture", "image photo screenshot"),
        new("star", "Star", "favourite favorite"),
        new("heart", "Heart", "love like favourite"),
        new("bolt", "Bolt", "lightning flash power energy"),
        new("home", "Home", "house"),
        new("bed", "Bed", "sleep bedroom night"),
        new("sofa", "Sofa", "couch living room"),
        new("door", "Door", "enter exit room"),
        new("clock", "Clock", "time"),
        new("timer", "Timer", "alarm countdown time"),
        new("hourglass", "Hourglass", "wait time"),
        new("bell", "Bell", "notification alert"),
        new("cloud", "Cloud", "weather online"),
        new("user", "User", "person account"),
        new("users", "People", "group users team"),
        new("world", "Globe", "web internet world browser"),
        new("mail", "Mail", "email letter message"),
        new("message", "Message", "chat text"),
        new("calendar", "Calendar", "date day"),
        new("pin", "Place", "location map pin"),
        new("leaf", "Leaf", "plant eco nature"),
        new("droplet", "Water", "drop humidity"),
        new("snowflake", "Snow", "cold winter freeze"),
        new("rocket", "Rocket", "launch start fast"),
        new("gift", "Gift", "present"),
        new("link", "Link", "chain url"),
        new("pencil", "Pencil", "edit write draw"),
        new("code", "Code", "developer brackets"),
    };

    /// <summary>
    /// Values saved before the library existed, kept working: the glyph resources entries named (Styles/Brushes.axaml then) and the device
    /// icon ids, read as the library icon that replaced them. A device id that is also a library id needs no line.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Earlier = new Dictionary<string, string>
    {
        ["MicIcon"] = Mic, ["MoonIcon"] = Moon, ["FxIcon"] = Sliders, ["RotateIcon"] = Rotate, ["ShotAreaIcon"] = Area, ["ShotWindowIcon"] = Window,
        ["ShotScreenIcon"] = Screen, ["ShotAllScreensIcon"] = Desktop, ["VideoCameraIcon"] = Video, ["DeviceBulbIcon"] = Bulb, ["DevicePlugIcon"] = Plug,
        ["DeviceLampIcon"] = "lamp", ["DeviceFanIcon"] = "fan", ["DeviceTvIcon"] = "tv", ["DeviceDesktopIcon"] = Desktop, ["DeviceCoffeeIcon"] = "coffee",
        ["DeviceThermometerIcon"] = "thermometer", ["DeviceSpeakerIcon"] = "speaker", ["DeviceStripIcon"] = "strip", ["DeviceHeaterIcon"] = "heater",
        ["DeviceSwitchIcon"] = "switch",
    };

    private static readonly Dictionary<string, LibraryIcon> ById = All.ToDictionary(i => i.Id);

    /// <summary>The library icon with this id (an earlier value is read as its replacement); null when it is none.</summary>
    public static LibraryIcon? Find(string? id) =>
        id is null ? null : ById.GetValueOrDefault(id) ?? (Earlier.TryGetValue(id, out var now) ? ById.GetValueOrDefault(now) : null);

    /// <summary>A saved icon value as it is meant now: an earlier value becomes its library id; anything else (a library id, a user icon) is kept.</summary>
    public static string? Current(string? value) => value is not null && Earlier.TryGetValue(value, out var now) ? now : value;

    /// <summary>The icons whose name, keywords or id hold every typed word (the one search rule, <see cref="TextMatch"/>); all for an empty query.</summary>
    public static IReadOnlyList<LibraryIcon> Search(string query) => All.Where(i => TextMatch.Any(query, i.Name, i.Keywords, i.Id)).ToList();

    /// <summary>The icon an entry shows when the user chose none: its first suggestion that is in the library, else its own icon.</summary>
    public static string? DefaultOf(IReadOnlyList<string>? suggested, string? own) =>
        suggested?.Select(Current).FirstOrDefault(id => Find(id) is not null) ?? Current(own);

    /// <summary>
    /// The picker's "Suggested for this" (at most <see cref="SuggestionCount"/>): the entry's own suggestions, then its own icon, then, when
    /// that leaves room, the library icons found by the words of its name (longest words first). Each once; only library icons.
    /// </summary>
    public static IReadOnlyList<string> Suggestions(IReadOnlyList<string>? suggested, string? own, string? name)
    {
        var found = (suggested ?? Array.Empty<string>()).Append(own).Select(Current).Where(id => Find(id) is not null).Select(id => Find(id)!.Id).ToList();
        foreach (var word in NameWords(name))
            found.AddRange(Search(word).Select(i => i.Id));
        return found.Distinct().Take(SuggestionCount).ToList();
    }

    private static IEnumerable<string> NameWords(string? name) =>
        (name ?? string.Empty).Split(new[] { ' ', ':', '-', '(', ')', ',', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3).OrderByDescending(w => w.Length);
}
