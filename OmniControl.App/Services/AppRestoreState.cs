namespace OmniControl.App.Services;

/// <summary>
/// The App settings that "Restore defaults" puts back (pure): theme, accent, start with Windows (what Windows has, not a stored value),
/// start minimised, what closing does and the user's plug-in folders. Everything else in <see cref="AppSettings"/> (the Volume view,
/// the first-run choice, effect-editor layouts, which groups are open) is left alone.
/// </summary>
public sealed class AppRestoreState
{
    public ThemeChoice Theme { get; set; }
    public string Accent { get; set; } = string.Empty;
    public bool StartWithWindows { get; set; }
    public bool StartMinimised { get; set; }
    public CloseBehavior CloseBehavior { get; set; }
    public List<string> PluginFolders { get; set; } = new();

    public static AppRestoreState Of(AppSettings settings, bool startWithWindows) => new()
    {
        Theme = settings.Theme,
        Accent = settings.Accent,
        StartWithWindows = startWithWindows,
        StartMinimised = settings.StartMinimised,
        CloseBehavior = settings.CloseBehavior,
        PluginFolders = settings.PluginFolders.ToList(),
    };

    /// <summary>The defaults are whatever a new <see cref="AppSettings"/> holds, with Start with Windows off.</summary>
    public static AppRestoreState Defaults() => Of(new AppSettings(), false);

    /// <summary>The one sentence the question and the page's scope use.</summary>
    public static string Scope()
    {
        var d = Defaults();
        return $"Theme {d.Theme}, accent {d.Accent}, Start with Windows off, Start minimised off, When closing \"{CloseBehaviors.LabelOf(d.CloseBehavior)}\", "
            + "and your plug-in folders removed (what the scan found is kept).";
    }
}
