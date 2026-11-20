using Avalonia.Styling;
using OmniControl.App.Shared.Contracts;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Settings.Pages.Backup;
using OmniControl.App.Features.Tray;
using OmniControl.App.Shared.Services.Updates;

namespace OmniControl.App.Features.Settings.Pages;

/// The App settings page, five collapsible groups: Appearance; Start and close; Your data (presets, backup and restore, the Confirmations,
/// Restore all defaults); Plug-ins (AppSettingsPageViewModel.Plugins.cs, with Keep OmniControl's effects in place); Updates and about (AppSettingsPageViewModel.Updates.cs).
public sealed partial class AppSettingsPageViewModel : PageViewModel
{
    private readonly AppSettings _settings;
    private readonly ToggleItem _startWithWindows;
    private readonly ChoiceItem _whenClosing;
    private readonly ChoiceItem _groupsOpen;
    private readonly ChoiceItem _hardwareCheck;
    private readonly HardwareWatches _hardware;
    private readonly ToggleItem _startMinimised;
    private readonly ButtonItem _about;
    private readonly ButtonItem _restoreAllCard;
    private readonly ButtonItem _presets;
    private readonly ButtonItem _backup;
    private readonly MenuChoiceItem _importFromPc;
    private bool _readingStartup;

    /// One plain sentence under the list when Windows has the start-up entry switched off or refused a change; empty otherwise.
    [ObservableProperty] private string _startupNote = string.Empty;

    public const string Name = "App settings";

    public override string Title => Name;
    public override string Icon => "M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z M10,2 H14 L14.6,4.6 L16.6,5.6 L19,4.3 L21.7,7 L20.4,9.4 L21.4,11.4 L24,12 V16 L21.4,16.6 L20.4,18.6 L21.7,21 L19,23.7 L16.6,22.4 L14.6,23.4 L14,26 H10 L9.4,23.4 L7.4,22.4 L5,23.7 L2.3,21 L3.6,18.6 L2.6,16.6 L0,16 V12 L2.6,11.4 L3.6,9.4 L2.3,7 L5,4.3 L7.4,5.6 L9.4,4.6 Z";
    public override string Keywords => "settings appearance color colour mode theme accent tray icon plug-in plugin folders vst backup restore export import wipe confirm confirmation ask again";

    public GalleryItem Theme { get; }
    public GalleryItem Accent { get; }
    public GalleryItem Background { get; }

    /// The cards of each group, so the keyboard model matches the other pages (the folder and confirmation rows come and go).
    public ObservableCollection<object> AppearanceItems { get; } = new();
    public ObservableCollection<object> StartItems { get; } = new();
    public ObservableCollection<object> DataItems { get; } = new();
    public ObservableCollection<object> PluginItems { get; } = new();
    public ObservableCollection<object> UpdateItems { get; } = new();

    public ItemGroup Appearance { get; }
    public ItemGroup StartAndClose { get; }
    public ItemGroup YourData { get; }
    public ItemGroup PlugIns { get; }
    public ItemGroup UpdatesAndAbout { get; }

    public override IEnumerable<SearchEntry> SearchEntries => new[]
    {
        Entry(Theme, AppearanceName, "light dark color mode system"),
        Entry(Accent, AppearanceName, "accent color colour highlight orange blue green white windows follow system custom picker hex hue saturation brightness"),
        Entry(_accentIntensity, AppearanceName, "accent intensity strength softer stronger lighter darker shade vivid"),
        Entry(Background, AppearanceName, "background colour color surface carbon slate blue black oled plum dark grey gray windows follow"),
        Entry(_trayIcon, AppearanceName, "tray icon notification area logo outline sliders command key look choose"),
        Entry(_trayColour, AppearanceName, "tray icon colour color white black follow windows taskbar accent"),
        Entry(_groupsOpen, AppearanceName, "groups open collapsed collapse expand remember last state sections panels page opens"),
        Entry(_startWithWindows, StartName, "start boot login windows launch"),
        Entry(_startMinimised, StartName, "start minimized hidden background tray"),
        Entry(_whenClosing, StartName, "close tray exit quit notification area minimize x button ask remember"),
        Entry(_hardwareCheck, StartName, "hardware check refresh monitors displays sound devices touchpad undetected missing remote desktop safety periodic every minutes"),
        Entry(_presets, DataName, "presets manage layers display monitor equaliser compressor shortcut order rename"),
        Entry(_backup, DataName, "backup restore export import save load move computer wipe reset factory delete all settings"),
        Entry(_importFromPc, DataName, "import settings another pc computer laptop copy paired other pcs move"),
        Entry(_restoreAllCard, DataName, "reset default restore every page all settings"),
    }.Concat(ConfirmationSearchEntries()).Concat(PluginSearchEntries()).Concat(UpdateSearchEntries())
     .Append(Entry(_about, UpdatesName, "about credits licence license gpl open source version notice"));

    private const string AppearanceName = "Appearance", StartName = "Start and close", DataName = "Your data", PluginsName = "Plug-ins", UpdatesName = "Updates and about";

    /// <param name="openAbout">Opens the About and credits window.</param>
    /// <param name="scanPlugins">Scans the plug-in folders (the user's own included) and remembers what it found; gives the number found.</param>
    /// <param name="scanResult">The sentence saying what the last scan found.</param>
    /// <param name="openPresets">Opens the preset manager at its first section with presets.</param>
    /// <param name="confirmations">The prompts the user chose not to be asked about again (the Confirmations cards of Your data).</param>
    /// <param name="backup">Export, import and wipe of every saved store (the Backup and restore card).</param>
    /// <param name="updates">The update check (the Updates and about group).</param>
    /// <param name="report">Says one plain sentence in the window's footer (a web page that would not open).</param>
    /// <param name="showShortcuts">Opens the Keyboard shortcuts window (F1); registered as an action so it stays reachable from search and the Actions and hotkeys window.</param>
    /// <param name="mixer">Holds "Keep OmniControl's effects in place" (the Plug-ins group's switch, also in every FX window's Options).</param>
    /// <param name="hardware">The master list of hardware watches: the "Check hardware every" card sets how often its periodic safety check runs.</param>
    /// <param name="settings">The app's settings in use, which this page edits.</param>
    public AppSettingsPageViewModel(IActionRegistry hotkeys, Func<RunChain?> chainNow, Action openAbout, Func<IProgress<int>?, CancellationToken, Task<int>> scanPlugins,
        Func<string> scanResult, Action openPresets, BackupService backup, ConfirmationMemory confirmations, UpdateService updates, Action<string> report, Action showShortcuts,
        Features.Volume.Audio.MixerService mixer, HardwareWatches hardware, AppSettings settings, ITrayIconLook trayIcon)
    {
        _settings = settings;
        _confirmations = confirmations;
        _hardware = hardware;
        var themes = Enum.GetNames<ThemeChoice>();
        Theme = new GalleryItem("Theme", themes, (int)_settings.Theme, () => ThemeSwatches.Build((int)_settings.Theme), i => OnThemeChosen(themes[i])) { Half = true };
        Apply(_settings.Theme);

        Accent = MakeAccentCards(report);

        var backgrounds = SurfaceService.Names;
        Background = new GalleryItem("Background colour", backgrounds, SurfaceService.IndexOf(_settings.Background),
            () => Swatches.Build(backgrounds.Select(n => (IBrush)new SolidColorBrush(SurfaceService.Swatch(n))).ToList(), SurfaceService.IndexOf(SurfaceService.Name),
                edged: true, marked: SurfaceService.IndexOf(SurfaceService.FollowName)),
            i =>
            {
                SurfaceService.Set(backgrounds[i]);
                _settings.Background = backgrounds[i];
                _settings.Save();
            })
        {
            Tip = "The colour of pages, cards, windows, pop-downs and the tray panel. Follow Windows uses Windows' own tones.",
            Half = true,
        };
        SurfaceService.Changed += Background.Refresh;

        var groupLabels = GroupsOpen.All.Select(e => e.Label).ToList();
        _groupsOpen = new ChoiceItem("Groups when a page opens", groupLabels, GroupsOpen.LabelOf(_settings.GroupsOpen), label => GroupsOpen.Set(GroupsOpen.Find(label)))
        {
            Half = true,
        };
        GroupsOpen.Changed += ShowGroupsOpen;
        MakeTrayIconCards(trayIcon);

        _startWithWindows = new ToggleItem("Start with Windows", StartupRegistration.State == StartupState.On, OnStartWithWindowsChosen) { Half = true };
        _startMinimised = new ToggleItem("Start minimised", _settings.StartMinimised, on => { _settings.StartMinimised = on; _settings.Save(); }) { Half = true };
        var labels = CloseBehaviors.All.Select(e => e.Label).ToList();
        _whenClosing = new ChoiceItem("When closing", labels, CloseBehaviors.LabelOf(_settings.CloseBehavior), label =>
        {
            _settings.CloseBehavior = CloseBehaviors.Find(label);
            _settings.Save();
        });

        var checkLabels = HardwareChecks.All.Select(e => e.Label).ToList();
        _hardwareCheck = new ChoiceItem("Check hardware every", checkLabels, HardwareChecks.LabelOf(_hardware.Check), label => _hardware.SetCheck(HardwareChecks.Find(label)));

        _about = new ButtonItem("About and credits", "Open", "Version, licence, credits", openAbout);
        _presets = new ButtonItem("Presets", "Open", "Every preset of the program: order, names and shortcuts", openPresets);

        _backup = new ButtonItem("Backup and restore", "Export...", "Settings, presets, shortcuts, rules, scripts, custom actions, app lists",
            () => _ = backup.ExportAsync())
        {
            Extra = new[] { new CardAction("Import...", () => _ = backup.ImportAsync()), new CardAction("Wipe all...", () => _ = backup.WipeAsync(), danger: true) },
        };

        _importFromPc = new MenuChoiceItem("Import from another PC", () => "Choose a PC", () => ImportPcRows(backup))
        {
            Tip = "Copy chosen areas of a paired PC's settings here. It has to be on and connected.",
        };

        _addFolder = new FileItem("Plug-in folders", FileKind.OfFolder("Choose a folder with plug-ins"), AddFolder) { EmptyText = "Add a folder to scan" };
        _scan = new ButtonItem("Plug-in scan", "Scan now", ScanIdleText, StartScan);
        _scanPlugins = scanPlugins;
        _keepFx = KeepFxCard(mixer);
        _scanResult = scanResult;
        _resetAllConfirmations = new ButtonItem("Confirmations", "Reset all", string.Empty, confirmations.ResetAll);
        _restoreAllCard = new ButtonItem("Restore all defaults", "Restore all", "Puts every page back to its defaults after one question; you can undo it", () => _ = RestoreAll?.RestoreAsync());
        MakeUpdateCards(updates, report);

        // Each group is made from its fixed cards (its closed header lists their names), then the cards that come and go are added.
        Appearance = Fill(AppearanceItems, "AppSettings/Appearance", AppearanceName, Theme, Background, Accent, _accentIntensity, _groupsOpen, _trayIcon, _trayColour);
        StartAndClose = Fill(StartItems, "AppSettings/Start", StartName, _startWithWindows, _startMinimised, _whenClosing, _hardwareCheck);
        YourData = Fill(DataItems, "AppSettings/Data", DataName, _presets, _backup, _importFromPc, _resetAllConfirmations, _restoreAllCard);
        PlugIns = Fill(PluginItems, "AppSettings/Plugins", PluginsName, _addFolder, _scan, _keepFx);
        UpdatesAndAbout = Fill(UpdateItems, "AppSettings/Updates", UpdatesName, _updateDaily, _version, _latest, _about);
        foreach (var folder in _settings.PluginFolders) PluginItems.Insert(PluginItems.IndexOf(_scan), FolderRow(folder));
        ReloadConfirmations();
        confirmations.Changed += ReloadConfirmations;
        ReadStartup();
        hotkeys.Register(new HotkeyAction("app.show", "Open OmniControl", Title, "show window open tray restore", _ => AppLifecycle.RequestShow($"the action Open OmniControl, run by {RunChain.Describe(chainNow())}")) { NeverBlock = true, SuggestedIcons = new[] { "app", "home" } });
        hotkeys.Register(new HotkeyAction("app.toggle", "Show or hide OmniControl", Title, "show hide toggle window tray minimise",
            _ => AppLifecycle.RequestToggle($"the action Show or hide OmniControl, run by {RunChain.Describe(chainNow())}"))
            { NeverBlock = true, DefaultKey = "Alt+X", SuggestedIcons = new[] { "app", "home" } });
        hotkeys.Register(new HotkeyAction("app.shortcuts", "Keyboard shortcuts", Title, "keyboard shortcuts keys help list f1", _ => showShortcuts()) { SuggestedIcons = new[] { "keyboard", "help" } });
        hotkeys.Register(new HotkeyAction("app.quit", "Quit OmniControl", Title, "exit quit close stop tray", _ => AppLifecycle.RequestQuit()) { SuggestedIcons = new[] { "power", "lock" } });
        hotkeys.Register(new HotkeyAction("app.updates.check", "Check for updates", Title, "update updates upgrade new version release github check", _ => CheckNow())
        {
            SuggestedIcons = new[] { "download", "refresh" },
        });
        hotkeys.Register(new HotkeyAction("app.plugins.scan", "Scan for plug-ins", Title, "plug-in plugin vst vst2 vst3 scan search folders",
            _ => StartScan()));
    }

    /// The menu of the Import from another PC card: the paired PCs that are connected now (asked every time it opens), each opens the import window.
    private static IReadOnlyList<MenuRowItem> ImportPcRows(BackupService backup)
    {
        var pcs = backup.OnlinePcs();
        if (pcs.Count == 0) return new[] { MenuRowItem.Header("No paired PC is online") };
        return pcs.Select(pc => new MenuRowItem(pc.Name, string.Empty, false, $"Import settings from {pc.Name}…", new RelayCommand(() => backup.ImportFromPc(pc.Id)), Icon: "download")).ToList();
    }

    private static ItemGroup Fill(ObservableCollection<object> items, string key, string name, params object[] cards)
    {
        foreach (var card in cards) items.Add(card);
        return ItemGroup.Summarised(key, name, items);
    }

    /// Windows may have changed the start-up entry (Task Manager), and the close dialog may have remembered a choice.
    public override void OnShown()
    {
        ReadStartup();
        _whenClosing.Load(_whenClosing.Options, CloseBehaviors.LabelOf(_settings.CloseBehavior));
        RefreshUpdates();
    }

    private void OnStartWithWindowsChosen(bool on)
    {
        if (_readingStartup) return;
        var allowed = StartupRegistration.Set(on);
        ReadStartup();
        if (!allowed) StartupNote = "Windows refused that change.";
    }

    /// Shows what Windows really has: on, off, or an entry the user switched off in Task Manager.
    private void ReadStartup()
    {
        var state = StartupRegistration.State;
        _readingStartup = true;
        _startWithWindows.IsOn = state == StartupState.On;
        _readingStartup = false;
        StartupNote = state == StartupState.DisabledByUser
            ? "Switched off in Task Manager > Startup apps; turning this on re-enables it."
            : string.Empty;
    }

    /// Takes settings.json from disk as it is now (an import wrote it): the theme, accent and background colour are applied, the cards show the values and the
    /// hardware check runs as chosen. The features' setting slots read the same settings, so they follow.
    public bool ReloadFromDisk()
    {
        SettingsCopy.Into(_settings, AppSettings.Load());
        Apply(_settings.Theme);
        Theme.Load((int)_settings.Theme);
        SurfaceService.Set(_settings.Background);
        Background.Load(SurfaceService.IndexOf(_settings.Background));
        ReloadAccent();
        ShowGroupsOpen();
        _startMinimised.Load(_settings.StartMinimised);
        _whenClosing.Load(_whenClosing.Options, CloseBehaviors.LabelOf(_settings.CloseBehavior));
        _hardware.ApplyCheck();
        _hardwareCheck.Load(_hardwareCheck.Options, HardwareChecks.LabelOf(_hardware.Check));
        return true;
    }

    /// The choice was changed elsewhere (the sidebar chip, Restore defaults): the card shows it without applying it again.
    private void ShowGroupsOpen() => _groupsOpen.Load(_groupsOpen.Options, GroupsOpen.LabelOf(_settings.GroupsOpen));

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
