namespace OmniControl.App.Shared.Services;

/// The App settings that "Restore defaults" puts back (pure): theme, accent and its intensity (the custom colour is kept), background colour, the tray icon's look (kept by the tray, so its values and defaults
/// are given by the page), start with Windows (what Windows has, not a stored value), start minimised, what closing does, the daily update check,
/// how often the hardware is checked, how groups start and the user's plug-in folders. Everything else in <see cref="AppSettings"/> (the Volume view,
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
    public CloseBehavior CloseBehavior { get; set; }
    public List<string> PluginFolders { get; set; } = new();
    public bool UpdateCheckDaily { get; set; }
    public GroupsOpenChoice GroupsOpen { get; set; }
    public HardwareCheck HardwareCheck { get; set; }

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
        CloseBehavior = settings.CloseBehavior,
        PluginFolders = settings.PluginFolders.ToList(),
        UpdateCheckDaily = settings.Updates.Daily,
        GroupsOpen = settings.GroupsOpen,
        HardwareCheck = settings.HardwareCheck,
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
        return $"Theme {d.Theme}, accent {d.Accent} at intensity {AccentService.IntensityWords(d.AccentIntensity)}, background {d.Background}, {trayWords}Start with Windows off, Start minimised off, Check once a day on, When closing \"{CloseBehaviors.LabelOf(d.CloseBehavior)}\", Groups \"{Services.GroupsOpen.LabelOf(d.GroupsOpen)}\", Check hardware every \"{HardwareChecks.LabelOf(d.HardwareCheck)}\", "
            + "and your plug-in folders removed (what the scan found is kept).";
    }
}
