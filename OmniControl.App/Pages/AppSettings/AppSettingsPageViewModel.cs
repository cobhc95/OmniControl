using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.AppSettingsPage;

public sealed class AppSettingsPageViewModel : PageViewModel
{
    private readonly AppSettings _settings = AppSettings.Current;

    public override string Title => "App settings";
    public override string Icon => "M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z M10,2 H14 L14.6,4.6 L16.6,5.6 L19,4.3 L21.7,7 L20.4,9.4 L21.4,11.4 L24,12 V16 L21.4,16.6 L20.4,18.6 L21.7,21 L19,23.7 L16.6,22.4 L14.6,23.4 L14,26 H10 L9.4,23.4 L7.4,22.4 L5,23.7 L2.3,21 L3.6,18.6 L2.6,16.6 L0,16 V12 L2.6,11.4 L3.6,9.4 L2.3,7 L5,4.3 L7.4,5.6 L9.4,4.6 Z";
    public override string Keywords => "settings appearance color colour mode theme accent";

    private readonly ToggleItem[] _toggles;

    public GalleryItem Theme { get; }
    public GalleryItem Accent { get; }

    /// <summary>The settings as cards, so the keyboard model matches the other pages.</summary>
    public IReadOnlyList<object> Items { get; }

    public override IEnumerable<SearchEntry> SearchEntries => new[]
    {
        new SearchEntry { Title = "Appearance", Category = "App settings", Editor = Theme, Keywords = "light dark color mode system" },
        new SearchEntry { Title = "Appearance", Category = "App settings", Editor = Accent, Keywords = "accent color colour highlight orange blue green" },
        new SearchEntry { Title = "Startup", Category = "App settings", Editor = Items[2], Keywords = "start boot login windows launch" },
        new SearchEntry { Title = "Startup", Category = "App settings", Editor = Items[3], Keywords = "start minimized hidden background tray" },
        new SearchEntry { Title = "Window", Category = "App settings", Editor = Items[4], Keywords = "close tray exit quit notification area minimize" },
    };

    public AppSettingsPageViewModel(HotkeyRegistry hotkeys)
    {
        var themes = Enum.GetNames<ThemeChoice>();
        Theme = new GalleryItem("Theme", themes, (int)_settings.Theme, () => ThemeSwatches.Build((int)_settings.Theme), i => OnThemeChosen(themes[i]));
        Apply(_settings.Theme);

        var names = AccentService.Names;
        Accent = new GalleryItem("Accent color", names, Math.Max(0, names.ToList().IndexOf(_settings.Accent)),
            () => Swatches.Build(names.Select(AccentService.Resolve).ToList(), names.ToList().IndexOf(AccentService.Name)),
            i =>
            {
                AccentService.Set(names[i]);
                _settings.Accent = names[i];
                _settings.Save();
            });
        AccentService.Changed += Accent.Refresh;

        var startWithWindows = new ToggleItem("Start with Windows", StartupRegistration.IsEnabled, StartupRegistration.Set) { Half = true };
        var startMinimised = new ToggleItem("Start minimised", _settings.StartMinimised, on => { _settings.StartMinimised = on; _settings.Save(); }) { Half = true };
        var closeToTray = new ToggleItem("Minimise to tray on close", _settings.CloseToTray, on =>
        {
            _settings.CloseToTray = on;
            _settings.Save();
            AppLifecycle.SetTrayEnabled(on);
        }) { Half = true };

        Items = new object[] { Theme, Accent, startWithWindows, startMinimised, closeToTray };
        _toggles = new[] { startWithWindows, startMinimised, closeToTray };

        hotkeys.Register(new HotkeyAction("app.show", "Show OmniControl window", "App settings", "window open show tray restore", _ => AppLifecycle.RequestShow()));
    }

    private void OnThemeChosen(string name)
    {
        var choice = Enum.Parse<ThemeChoice>(name);
        Apply(choice);
        _settings.Theme = choice;
        _settings.Save();
    }

    private static void Apply(ThemeChoice choice)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = choice switch
            {
                ThemeChoice.Light => ThemeVariant.Light,
                ThemeChoice.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
    }
}
