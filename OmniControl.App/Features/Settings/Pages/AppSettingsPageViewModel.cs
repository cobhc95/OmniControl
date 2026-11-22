using OmniControl.App.Features.Settings.Pages.Backup;
using OmniControl.App.Features.Tray;
using OmniControl.App.Shared.Services.Updates;

namespace OmniControl.App.Features.Settings.Pages;

/// The App settings page, six collapsible groups: Appearance; Start and close; Features (AppSettingsPageViewModel.Features.cs); Your data (presets, backup and restore, import, removing
/// the network service, the Confirmations, Restore all defaults); Plug-ins (AppSettingsPageViewModel.Plugins.cs, with Keep OmniControl's effects in place); Updates and about (AppSettingsPageViewModel.Updates.cs).
public sealed partial class AppSettingsPageViewModel : PageViewModel, IDisposable
{
    private readonly AppSettings _settings;
    private readonly ToggleItem _startWithWindows;
    private readonly ChoiceItem _whenClosing;
    private readonly ChoiceItem _groupsOpen;
    private readonly ChoiceItem _hardwareCheck, _uiRelease;
    private readonly HardwareWatches _hardware;
    private readonly ToggleItem _startMinimised;
    private readonly ToggleItem _gpuDrawing;
    private readonly bool _gpuDrawingAtStart;
    private readonly ButtonItem _about;
    private readonly ButtonItem _restoreAllCard;
    private readonly ButtonItem _presets;
    private readonly ButtonItem _backup;
    private readonly MenuChoiceItem _importFromPc;
    private readonly ButtonItem _networkService;
    private bool _readingStartup;

    /// One plain sentence under the list when Windows has the start-up entry switched off or refused a change; empty otherwise.
    [ObservableProperty] private string _startupNote = string.Empty;

    public const string Name = "App settings";

    public static readonly PageInfo Info = new(Name,
        "M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z M10,2 H14 L14.6,4.6 L16.6,5.6 L19,4.3 L21.7,7 L20.4,9.4 L21.4,11.4 L24,12 V16 L21.4,16.6 L20.4,18.6 L21.7,21 L19,23.7 L16.6,22.4 L14.6,23.4 L14,26 H10 L9.4,23.4 L7.4,22.4 L5,23.7 L2.3,21 L3.6,18.6 L2.6,16.6 L0,16 V12 L2.6,11.4 L3.6,9.4 L2.3,7 L5,4.3 L7.4,5.6 L9.4,4.6 Z",
        Keywords: "settings appearance color colour mode theme accent tray icon plug-in plugin folders vst backup restore export import wipe confirm confirmation ask again", RestoreKey: "app");

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

    /// Sound and effects runs: the Plug-ins group shows.
    public bool HasPlugins => _plugins is not null;

    private const string AppearanceName = "Appearance", StartName = "Start and close", DataName = "Your data", PluginsName = "Plug-ins", UpdatesName = "Updates and about";

    /// <param name="openAbout">Opens the About and credits window.</param>
    /// <param name="plugins">The plug-ins found on this PC: Scan now scans the folders (the user's own included) and says what it found; null while
    /// Sound and effects is off (no Plug-ins group).</param>
    /// <param name="openPresets">Opens the preset manager at its first section with presets.</param>
    /// <param name="confirmations">The prompts the user chose not to be asked about again (the Confirmations cards of Your data).</param>
    /// <param name="backup">Export, import and wipe of every saved store (the Backup and restore card).</param>
    /// <param name="updates">The update check (the Updates and about group).</param>
    /// <param name="report">Says one plain sentence in the window's footer (a web page that would not open).</param>
    /// <param name="mixer">Holds "Keep OmniControl's effects in place" (the Plug-ins group's switch, also in every FX window's Options); null with <paramref name="plugins"/>.</param>
    /// <param name="hardware">The master list of hardware watches: the "Check hardware every" card sets how often its periodic safety check runs.</param>
    /// <param name="settings">The app's settings in use, which this page edits.</param>
    /// <param name="features">THE feature switch (the Features group).</param>
    public AppSettingsPageViewModel(Action openAbout, Features.Volume.Fx.Plugins.PluginLibrary? plugins, Action openPresets, BackupService backup,
        ConfirmationMemory confirmations, UpdateService updates, Action<string> report, Features.Volume.Audio.MixerService? mixer, HardwareWatches hardware,
        AppSettings settings, ITrayIconLook trayIcon, FeatureSwitches features) : base(Info)
    {
        _settings = settings;
        _confirmations = confirmations;
        _hardware = hardware;
        _features = features;
        _plugins = plugins;
        var themes = Enum.GetNames<ThemeChoice>();
        Theme = new GalleryItem(ThemeCard.Label, themes, (int)_settings.Theme, i => (ThemeSwatch.Fill(i), SwatchMark.None), i => OnThemeChosen(themes[i])) { Half = true };

        Accent = MakeAccentCards(report);

        var backgrounds = SurfaceService.Names;
        Background = new GalleryItem(BackgroundCard.Label, backgrounds, SurfaceService.IndexOf(_settings.Background),
            i => (new SolidColorBrush(SurfaceService.Swatch(backgrounds[i])), backgrounds[i] == SurfaceService.FollowName ? SwatchMark.Windows : SwatchMark.None),
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
        Follow(h => SurfaceService.Changed += h, h => SurfaceService.Changed -= h, Background.Refresh);

        var groupLabels = GroupsOpen.All.Select(e => e.Label).ToList();
        _groupsOpen = new ChoiceItem(GroupsOpenCard.Label, groupLabels, GroupsOpen.LabelOf(_settings.GroupsOpen), label => GroupsOpen.Set(GroupsOpen.Find(label)))
        {
            Half = true,
        };
        Follow(h => GroupsOpen.Changed += h, h => GroupsOpen.Changed -= h, ShowGroupsOpen);
        MakeTrayIconCards(trayIcon);

        _startWithWindows = new ToggleItem(StartWithWindowsCard.Label, StartupRegistration.State == StartupState.On, OnStartWithWindowsChosen) { Half = true };
        _startMinimised = new ToggleItem(StartMinimisedCard.Label, _settings.StartMinimised, on => { _settings.StartMinimised = on; _settings.Save(); }) { Half = true };
        // The page is made after start-up and only it changes the setting (a restore restarts), so what it reads now is what drew this run.
        _gpuDrawingAtStart = _settings.GpuDrawing;
        _gpuDrawing = new ToggleItem(GpuCard.Label, _settings.GpuDrawing, on =>
        {
            _settings.GpuDrawing = on;
            _settings.Save();
            ShowGpuDrawingNote();
        }) { Half = true };
        ShowGpuDrawingNote();
        var labels = CloseBehaviors.All.Select(e => e.Label).ToList();
        _whenClosing = new ChoiceItem(WhenClosingCard.Label, labels, CloseBehaviors.LabelOf(_settings.CloseBehavior), label =>
        {
            _settings.CloseBehavior = CloseBehaviors.Find(label);
            _settings.Save();
        });

        var checkLabels = HardwareChecks.All.Select(e => e.Label).ToList();
        _hardwareCheck = new ChoiceItem(HardwareCheckCard.Label, checkLabels, HardwareChecks.LabelOf(_hardware.Check), label => _hardware.SetCheck(HardwareChecks.Find(label)));

        var releaseLabels = UiReleases.All.Select(e => e.Label).ToList();
        _uiRelease = new ChoiceItem(UiReleaseCard.Label, releaseLabels, UiReleases.LabelOf(_settings.UiRelease), label =>
        {
            _settings.UiRelease = UiReleases.Find(label);
            _settings.Save();
        }) { Tip = "How long the closed window stays ready to reopen instantly before OmniControl frees its memory." };

        _about = new ButtonItem(AboutCard.Label, "Open", "Version, licence, credits", openAbout);
        _presets = new ButtonItem(PresetsCard.Label, "Open", "Every preset of the program: order, names and shortcuts", openPresets);

        _backup = new ButtonItem(BackupCard.Label, "Export...", "Settings, presets, shortcuts, rules, scripts, custom actions, app lists",
            () => _ = backup.ExportAsync())
        {
            Extra = new[] { new CardAction("Import...", () => _ = backup.ImportAsync()), new CardAction("Wipe all...", () => _ = backup.WipeAsync(), danger: true) },
        };

        _importFromPc = new MenuChoiceItem(ImportFromPcCard.Label, () => "Choose a PC", () => ImportPcRows(backup))
        {
            Tip = "Copy chosen areas of a paired PC's settings here. It has to be on and connected.",
        };

        _addFolder = new FileItem(AddFolderCard.Label, FileKind.OfFolder("Choose a folder with plug-ins"), AddFolder) { EmptyText = "Add a folder to scan" };
        _scan = new ButtonItem(ScanCard.Label, "Scan now", ScanIdleText, StartScan);
        _keepFx = mixer is null ? null : KeepFxCard(mixer);
        _resetAllConfirmations = new ButtonItem(ResetConfirmationsCard.Label, "Reset all", string.Empty, confirmations.ResetAll);
        _restoreAllCard = new ButtonItem(RestoreAllCard.Label, "Restore all", "Puts every page back to its defaults after one question; you can undo it", () => _ = RestoreAll?.RestoreAsync());
        _networkService = new ButtonItem(NetworkServiceCard.Label, "Remove…", "Lets per-app network limits work without running OmniControl as administrator",
            () => _ = Guard.RunAsync("Remove the network service", async () => report(await NetworkServiceMode.RemoveAsync() ?? "The network service was removed.")));
        MakeUpdateCards(updates, report);
        _featureCards = MakeFeatureCards();
        ReadyMade = new MenuChoiceItem(ReadyMadeCard.Label, () => $"Ready-made: {FeatureList.ReadyMadeOf(_features.Off)}", ReadyMadeRows)
        {
            Tip = "The same choices as Setup's Choose features page.",
        };
        TurnAllOnCommand = new RelayCommand(() => _features.SetOff(Array.Empty<string>()), () => _features.Off.Count > 0);

        // Each group is made from its fixed cards (its closed header lists their names), then the cards that come and go are added.
        Appearance = Fill(AppearanceItems, "AppSettings/Appearance", AppearanceName, Theme, Background, Accent, _accentIntensity, _groupsOpen, _gpuDrawing, _trayIcon, _trayColour);
        StartAndClose = Fill(StartItems, "AppSettings/Start", StartName, _startWithWindows, _startMinimised, _whenClosing, _hardwareCheck, _uiRelease);
        foreach (var (_, card) in _featureCards) FeatureItems.Add(card);
        FeatureGroup = new ItemGroup("AppSettings/Features", FeaturesName, FeatureItems) { NoMasterSwitch = true };
        YourData = Fill(DataItems, "AppSettings/Data", DataName, new object?[] { _presets, _backup, backup.CanImportFromPc ? _importFromPc : null, _networkService,
            _resetAllConfirmations, _restoreAllCard }.OfType<object>().ToArray());
        PlugIns = Fill(PluginItems, "AppSettings/Plugins", PluginsName, new object?[] { _addFolder, _scan, _keepFx }.OfType<object>().ToArray());
        UpdatesAndAbout = Fill(UpdateItems, "AppSettings/Updates", UpdatesName, _updateDaily, _version, _latest, _about);
        foreach (var folder in _settings.PluginFolders) PluginItems.Insert(PluginItems.IndexOf(_scan), FolderRow(folder));
        ReloadConfirmations();
        Follow(h => confirmations.Changed += h, h => confirmations.Changed -= h, ReloadConfirmations);
        ShowFeatures();
        Follow(h => features.Settled += h, h => features.Settled -= h, ShowFeatures);
        ReadStartup();
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

    /// settings.json was taken from disk again and applied (an import, <c>ShellViewModel.ReloadAppSettings</c>): the cards show the values without applying them again.
    public void ShowReloaded()
    {
        Theme.Load((int)_settings.Theme);
        Background.Load(SurfaceService.IndexOf(_settings.Background));
        Accent.Load(AccentIndex(AccentService.Name));
        ShowGroupsOpen();
        _startMinimised.Load(_settings.StartMinimised);
        _gpuDrawing.Load(_settings.GpuDrawing);
        ShowGpuDrawingNote();
        _whenClosing.Load(_whenClosing.Options, CloseBehaviors.LabelOf(_settings.CloseBehavior));
        _hardwareCheck.Load(_hardwareCheck.Options, HardwareChecks.LabelOf(_hardware.Check));
        _uiRelease.Load(_uiRelease.Options, UiReleases.LabelOf(_settings.UiRelease));
    }

    // Feature cards' restart words, so a restart-only switch reads the same everywhere.
    private void ShowGpuDrawingNote() => _gpuDrawing.Note = _settings.GpuDrawing != _gpuDrawingAtStart
        ? "Takes effect after restart"
        : "Smoother scrolling and animations; uses about 40–80 MB more memory.";

    /// The choice was changed elsewhere (the sidebar chip, Restore defaults): the card shows it without applying it again.
    private void ShowGroupsOpen() => _groupsOpen.Load(_groupsOpen.Options, GroupsOpen.LabelOf(_settings.GroupsOpen));

    private void OnThemeChosen(string name)
    {
        var choice = Enum.Parse<ThemeChoice>(name);
        ThemeChoices.Apply(choice);
        _settings.Theme = choice;
        _settings.Save();
    }

    private readonly Subscriptions _followed = new(); // the services' events this page follows, given back at Dispose

    private void Follow(Action<Action> add, Action<Action> remove, Action handler) => _followed.Follow(add, remove, handler);

    public void Dispose() => _followed.Dispose();
}
