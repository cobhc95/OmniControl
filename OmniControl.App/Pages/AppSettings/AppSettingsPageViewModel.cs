using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.Core.Engine;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.AppSettingsPage;

/// <summary>The App settings page: appearance, startup, the close button, the plug-in folders (AppSettingsPageViewModel.Plugins.cs), About.</summary>
public sealed partial class AppSettingsPageViewModel : PageViewModel
{
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly ToggleItem _startWithWindows;
    private readonly ChoiceItem _whenClosing;
    private readonly ToggleItem _startMinimised;
    private readonly ButtonItem _about;
    private bool _readingStartup;

    /// <summary>One plain sentence under the list when Windows has the start-up entry switched off or refused a change; empty otherwise.</summary>
    [ObservableProperty] private string _startupNote = string.Empty;

    public override string Title => "App settings";
    public override string Icon => "M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z M10,2 H14 L14.6,4.6 L16.6,5.6 L19,4.3 L21.7,7 L20.4,9.4 L21.4,11.4 L24,12 V16 L21.4,16.6 L20.4,18.6 L21.7,21 L19,23.7 L16.6,22.4 L14.6,23.4 L14,26 H10 L9.4,23.4 L7.4,22.4 L5,23.7 L2.3,21 L3.6,18.6 L2.6,16.6 L0,16 V12 L2.6,11.4 L3.6,9.4 L2.3,7 L5,4.3 L7.4,5.6 L9.4,4.6 Z";
    public override string Keywords => "settings appearance color colour mode theme accent plug-in plugin folders vst";

    public GalleryItem Theme { get; }
    public GalleryItem Accent { get; }

    /// <summary>The settings as cards, so the keyboard model matches the other pages (the plug-in folder rows come and go).</summary>
    public ObservableCollection<object> Items { get; } = new();

    public override IEnumerable<SearchEntry> SearchEntries => new[]
    {
        Entry(Theme, "Appearance", "light dark color mode system"),
        Entry(Accent, "Appearance", "accent color colour highlight orange blue green"),
        Entry(_startWithWindows, "Startup", "start boot login windows launch"),
        Entry(_startMinimised, "Startup", "start minimized hidden background tray"),
        Entry(_whenClosing, "Window", "close tray exit quit notification area minimize x button ask remember"),
    }.Concat(PluginSearchEntries()).Append(Entry(_about, "About", "about credits licence license gpl open source version notice"));

    /// <param name="openAbout">Opens the About and credits window.</param>
    /// <param name="scanPlugins">Scans the plug-in folders (the user's own included) and remembers what it found; gives the number found.</param>
    /// <param name="scanResult">The sentence saying what the last scan found.</param>
    public AppSettingsPageViewModel(HotkeyRegistry hotkeys, Action openAbout, Func<IProgress<int>?, CancellationToken, Task<int>> scanPlugins,
        Func<string> scanResult)
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

        _startWithWindows = new ToggleItem("Start with Windows", StartupRegistration.State == StartupState.On, OnStartWithWindowsChosen) { Half = true };
        _startMinimised = new ToggleItem("Start minimised", _settings.StartMinimised, on => { _settings.StartMinimised = on; _settings.Save(); }) { Half = true };
        var labels = CloseBehaviors.All.Select(e => e.Label).ToList();
        _whenClosing = new ChoiceItem("When I close the window", labels, CloseBehaviors.LabelOf(_settings.CloseBehavior), label =>
        {
            _settings.CloseBehavior = CloseBehaviors.Find(label);
            _settings.Save();
        });

        _about = new ButtonItem("About and credits", "Open", "Version, licence (GPL-3.0-or-later) and the open source projects OmniControl uses", openAbout);

        foreach (var item in new object[] { Theme, Accent, _startWithWindows, _startMinimised, _whenClosing }) Items.Add(item);
        _addFolder = new FileItem("Plug-in folders", FileKind.OfFolder("Choose a folder with plug-ins"), AddFolder) { EmptyText = "Add a folder the plug-in scan also searches" };
        _scan = new ButtonItem("Plug-in scan", "Scan now", ScanIdleText, StartScan);
        _scanPlugins = scanPlugins;
        _scanResult = scanResult;
        Items.Add(_addFolder);
        foreach (var folder in _settings.PluginFolders) Items.Add(FolderRow(folder));
        Items.Add(_scan);
        Items.Add(_about);
        ReadStartup();
        hotkeys.Register(new HotkeyAction("app.show", "Show OmniControl window", Title, "window open show tray restore", _ => AppLifecycle.RequestShow()));
        hotkeys.Register(new HotkeyAction("app.plugins.scan", "Scan for plug-ins", Title, "plug-in plugin vst vst2 vst3 scan search folders",
            _ => StartScan()));
    }

    /// <summary>Windows may have changed the start-up entry (Task Manager), and the close dialog may have remembered a choice.</summary>
    public override void OnShown()
    {
        ReadStartup();
        _whenClosing.Load(_whenClosing.Options, CloseBehaviors.LabelOf(_settings.CloseBehavior));
    }

    private void OnStartWithWindowsChosen(bool on)
    {
        if (_readingStartup) return;
        var allowed = StartupRegistration.Set(on);
        ReadStartup();
        if (!allowed) StartupNote = "Windows did not allow that change.";
    }

    /// <summary>Shows what Windows really has: on, off, or an entry the user switched off in Task Manager.</summary>
    private void ReadStartup()
    {
        var state = StartupRegistration.State;
        _readingStartup = true;
        _startWithWindows.IsOn = state == StartupState.On;
        _readingStartup = false;
        StartupNote = state == StartupState.DisabledByUser
            ? "Windows has OmniControl switched off in Task Manager (Startup apps). Turning this on switches it back on."
            : string.Empty;
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
