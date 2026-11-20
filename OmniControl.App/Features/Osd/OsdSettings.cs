namespace OmniControl.App.Features.Osd;

/// OSD preferences, stored as JSON in %APPDATA%\OmniControl\osd.json.
public sealed class OsdSettings
{
    public bool Enabled { get; set; } = true;
    public OsdPosition Position { get; set; } = OsdPosition.BottomCenter;

    /// The id of the user's named place in use (one of <see cref="Places"/>); null: the built-in <see cref="Position"/>.
    public string? CustomPlace { get; set; }

    /// The places the user named, in their order (the OSD place presets).
    public List<OsdPlace> Places { get; set; } = new();

    /// On which screens the pop-up shows; every screen by default.
    public OsdShowOn ShowOn { get; set; } = OsdShowOn.AllScreens;

    /// The screens (<see cref="OsdScreens.KeyOf"/>) shown on while <see cref="ShowOn"/> is <see cref="OsdShowOn.Chosen"/>.
    public List<string> Screens { get; set; } = new();

    /// Where the pop-up goes: the named place in use, else the built-in one.
    public OsdSpot CurrentSpot() =>
        CustomPlace is { } id && Places.FirstOrDefault(p => p.Id == id) is { } place ? place.ToSpot() : OsdPlacement.Of(Position);

    /// The name of <see cref="CurrentSpot"/>'s place, as the Position card shows it.
    public string CurrentPlaceName() =>
        CustomPlace is { } id && Places.FirstOrDefault(p => p.Id == id) is { } place ? place.Name : Describe(Position);
    public OsdStyle Style { get; set; } = OsdStyle.Pill;
    /// "Match program" (follow the program accent) or the name of an AccentService preset.
    public string Accent { get; set; } = MatchProgram;

    public const string MatchProgram = "Match program";

    /// The accent colour to draw with: the program accent unless a preset was chosen.
    public Color ResolveAccent() => ResolveAccent(Accent);

    /// The colour of an accent choice. Asked on every pop-up step, so it is a plain loop that allocates nothing.
    public static Color ResolveAccent(string name) => name == MatchProgram ? AccentService.Current : AccentService.Resolve(name);

    public string Background { get; set; } = Palette.CharcoalHex;

    /// How opaque the OSD is, 10-100 percent.
    public int Opacity { get; set; } = Percent.Max;

    public const int MinOpacity = 10;

    public static int ClampOpacity(int percent) => Math.Clamp(percent, MinOpacity, Percent.Max);

    /// How long the pop-up stays, in seconds, when no exception matches ("Everything else"; <see cref="OsdDuration"/>).
    public double Duration { get; set; } = OsdDuration.Default;

    /// The exceptions of the duration, per app or per event, in order: the first that matches wins.
    public List<OsdDurationRule> Durations { get; set; } = new();

    /// Elements the user excluded from the OSD (everything else is shown).
    public List<string> Excluded { get; set; } = new();

    /// Suppress Windows' own volume/brightness pop-up.
    public bool HideWindowsOsd { get; set; }

    private static readonly DataStore Store = DataStores.Osd;

    public static OsdSettings Load() => AppDataStore.Load<OsdSettings>(Store) ?? new();

    /// Saved a moment later off the UI thread (a slider drag writes the file once).
    public void Save() => AppDataStore.SaveSoon(Store, this);

    public static string Describe(OsdPosition p) => p switch
    {
        OsdPosition.TopLeft => "Top left",
        OsdPosition.TopCenter => "Top center",
        OsdPosition.TopRight => "Top right",
        OsdPosition.Center => "Center",
        OsdPosition.BottomLeft => "Bottom left",
        OsdPosition.BottomCenter => "Bottom center",
        _ => "Bottom right",
    };

    public static string Describe(OsdAnchor a) => a switch
    {
        OsdAnchor.TopLeft => "Top left",
        OsdAnchor.Top => "Top center",
        OsdAnchor.TopRight => "Top right",
        OsdAnchor.Left => "Left",
        OsdAnchor.Center => "Center",
        OsdAnchor.Right => "Right",
        OsdAnchor.BottomLeft => "Bottom left",
        OsdAnchor.Bottom => "Bottom center",
        _ => "Bottom right",
    };

    public static string Describe(OsdStyle s) => s switch
    {
        OsdStyle.Classic => "Classic",
        OsdStyle.Pill => "Compact pill",
        OsdStyle.BigNumber => "Big number",
        OsdStyle.Ring => "Ring",
        _ => "Vertical",
    };
}
