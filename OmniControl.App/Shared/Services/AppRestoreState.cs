namespace OmniControl.App.Shared.Services;

/// The App settings that "Restore defaults" puts back (pure): theme, accent and its intensity (the custom colour is kept), background colour, GPU drawing, the tray icon's look (kept by the tray, so its values and defaults
/// are given by the page), start with Windows (what Windows has, not a stored value), start minimised, what closing does, the daily update check,
/// how often the hardware is checked, how long the closed window stays ready before its memory is freed, whether pages you leave are freed, how groups start, the features switched off and the user's plug-in folders. Everything else in <see cref="AppSettings"/> (the Volume view,
/// the first-run choice, effect-editor layouts, which groups are remembered open) is left alone.
public sealed class AppRestoreState
{
    public ThemeChoice Theme { get; set; }
    public string Accent { get; set; } = string.Empty;
    public int AccentIntensity { get; set; }
    public string Background { get; set; } = string.Empty;
    public string? TrayIcon { get; set; }
    public string? TrayIconColour { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartMinimised { get; set; }
    public bool GpuDrawing { get; set; }
    public CloseBehavior CloseBehavior { get; set; }
    public List<string> PluginFolders { get; set; } = new();
    public bool UpdateCheckDaily { get; set; }
    public GroupsOpenChoice GroupsOpen { get; set; }
    public HardwareCheck HardwareCheck { get; set; }
    public UiRelease UiRelease { get; set; }
    public bool FreeLeftPages { get; set; }
    public List<string> FeaturesOff { get; set; } = new();

    public static AppRestoreState Of(AppSettings settings, bool startWithWindows, (string? Icon, string? Colour) tray = default) => new()
    {
        Theme = settings.Theme,
        Accent = settings.Accent,
        AccentIntensity = settings.AccentIntensity,
        Background = settings.Background,
        TrayIcon = tray.Icon,
        TrayIconColour = tray.Colour,
        StartWithWindows = startWithWindows,
        StartMinimised = settings.StartMinimised,
        GpuDrawing = settings.GpuDrawing,
        CloseBehavior = settings.CloseBehavior,
        PluginFolders = settings.PluginFolders.ToList(),
        UpdateCheckDaily = settings.Updates.Daily,
        GroupsOpen = settings.GroupsOpen,
        HardwareCheck = settings.HardwareCheck,
        UiRelease = settings.UiRelease,
        FreeLeftPages = settings.FreeLeftPages,
        FeaturesOff = settings.FeaturesOff.ToList(),
    };

    /// The defaults are whatever a new <see cref="AppSettings"/> holds, with Start with Windows off and the tray's default look.
    public static AppRestoreState Defaults() => Defaults(default);

    public static AppRestoreState Defaults((string? Icon, string? Colour) tray) => Of(new AppSettings(), false, tray);

    /// The one sentence the question and the page's scope use.
    public static string Scope() => Scope(string.Empty);

    /// <param name="trayWords">The tray icon's default look in words ("Tray icon Logo outline in Follow Windows, "), or empty.</param>
    public static string Scope(string trayWords)
    {
        var d = Defaults();
        return $"Theme {d.Theme}, accent {d.Accent} at intensity {AccentService.IntensityWords(d.AccentIntensity)}, background {d.Background}, {trayWords}Use GPU drawing off,Start with Windows off, Start minimised off, Check once a day on, When closing \"{CloseBehaviors.LabelOf(d.CloseBehavior)}\", Groups \"{Services.GroupsOpen.LabelOf(d.GroupsOpen)}\", Check hardware every \"{HardwareChecks.LabelOf(d.HardwareCheck)}\", Free memory after closing \"{UiReleases.LabelOf(d.UiRelease)}\", Free pages you leave off, "
            + "every feature on, and your plug-in folders removed (what the scan found is kept).";
    }
}
