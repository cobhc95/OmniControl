using OmniControl.App.Shared.Presets.Manager;
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
    private readonly ToggleItem _gpuDrawing, _keepPages;
    private readonly bool _gpuDrawingAtStart;
    private readonly ButtonItem _about;
    private readonly ButtonItem _restoreAllCard;
    private readonly ButtonItem _presets;
    private readonly PresetManagerService _presetManager;
    private readonly ButtonItem _backup;
    private readonly MenuChoiceItem _importFromPc;
    private readonly ButtonItem _networkService;
    private bool _readingStartup;

    /// One plain sentence under the list when Windows has the start-up entry switched off or refused a change; empty otherwise.
    [ObservableProperty] private string _startupNote = string.Empty;

    public const string Name = PageTitles.AppSettings;

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
    /// <param name="presets">The preset manager: the Presets card opens it at its first section with presets, and search finds the presets and groups it holds.</param>
    /// <param name="confirmations">The prompts the user chose not to be asked about again (the Confirmations cards of Your data).</param>
    /// <param name="backup">Export, import and wipe of every saved store (the Backup and restore card).</param>
    /// <param name="updates">The update check (the Updates and about group).</param>
    /// <param name="report">Says one plain sentence in the window's footer (a web page that would not open).</param>
    /// <param name="mixer">Holds "Keep OmniControl's effects in place" (the Plug-ins group's switch, also in every FX window's Options); null with <paramref name="plugins"/>.</param>
    /// <param name="hardware">The master list of hardware watches: the "Check hardware every" card sets how often its periodic safety check runs.</param>
    /// <param name="settings">The app's settings in use, which this page edits.</param>
    /// <param name="features">THE feature switch (the Features group).</param>
    public AppSettingsPageViewModel(Action openAbout, Features.Volume.Fx.Plugins.PluginLibrary? plugins, PresetManagerService presets, BackupService backup,
        ConfirmationMemory confirmations, UpdateService updates, Action<string> report, Features.Volume.Audio.MixerService? mixer, HardwareWatches hardware,
        AppSettings settings, ITrayIconLook trayIcon, FeatureSwitches features) : base(Info)
    {
        _settings = settings;
        _presetManager = presets;
        _confirmations = confirmations;
        _hardware = hardware;
        _features = features;
        _plugins = plugins;
        Theme = new GalleryItem(ThemeCard, (int)_settings.Theme, i => (ThemeSwatch.Fill(i), SwatchMark.None), i => OnThemeChosen(ThemeCard.Choices[i])) { Half = true };

        Accent = MakeAccentCards(report);

        var backgrounds = BackgroundCard.Choices;
        Background = new GalleryItem(BackgroundCard, SurfaceService.IndexOf(_settings.Background),
            i => (new SolidColorBrush(SurfaceService.Swatch(backgrounds[i])), backgrounds[i] == SurfaceService.FollowName ? SwatchMark.Windows : SwatchMark.None),
            i =>
            {
                SurfaceService.Set(backgrounds[i]);
                _settings.Background = backgrounds[i];
                _settings.Save();
            })
        {
            Half = true,
        };
        Follow(h => SurfaceService.Changed += h, h => SurfaceService.Changed -= h, Background.Refresh);

        _groupsOpen = new ChoiceItem(GroupsOpenCard, GroupsOpen.LabelOf(_settings.GroupsOpen), label => GroupsOpen.Set(GroupsOpen.Find(label)))
        {
            Half = true,
        };
        Follow(h => GroupsOpen.Changed += h, h => GroupsOpen.Changed -= h, ShowGroupsOpen);
        MakeTrayIconCards(trayIcon);

        _startWithWindows = new ToggleItem(StartWithWindowsCard, StartupRegistration.State == StartupState.On, OnStartWithWindowsChosen) { Half = true };
        _startMinimised = new ToggleItem(StartMinimisedCard, _settings.StartMinimised, on => { _settings.StartMinimised = on; _settings.Save(); }) { Half = true };
        // The page is made after start-up and only it changes the setting (a restore restarts), so what it reads now is what drew this run.
        _gpuDrawingAtStart = _settings.GpuDrawing;
        _gpuDrawing = new ToggleItem(GpuCard, _settings.GpuDrawing, on =>
        {
            _settings.GpuDrawing = on;
            _settings.Save();
            ShowGpuDrawingNote();
        }) { Half = true };
        ShowGpuDrawingNote();
        _whenClosing = new ChoiceItem(WhenClosingCard, CloseBehaviors.LabelOf(_settings.CloseBehavior), label =>
        {
            _settings.CloseBehavior = CloseBehaviors.Find(label);
            _settings.Save();
        });

        _hardwareCheck = new ChoiceItem(HardwareCheckCard, HardwareChecks.LabelOf(_hardware.Check), label => _hardware.SetCheck(HardwareChecks.Find(label)));

        _uiRelease = new ChoiceItem(UiReleaseCard, UiReleases.LabelOf(_settings.UiRelease), label =>
        {
            _settings.UiRelease = UiReleases.Find(label);
            _settings.Save();
        });
        _keepPages = new ToggleItem(KeepPagesCard, _settings.KeepRecentPages, on => { _settings.KeepRecentPages = on; _settings.Save(); }) { Half = true };

        _about = new ButtonItem(AboutCard, "Open", openAbout);
        _presets = new ButtonItem(PresetsCard, "Open", () => presets.Open(null, null));

        _backup = new ButtonItem(BackupCard, "Export...", backup.Export)
        {
            Extra = new[] { new CardAction("Import...", () => _ = backup.ImportAsync()), new CardAction("Wipe all...", () => _ = backup.WipeAsync(), danger: true) },
        };

        _importFromPc = new MenuChoiceItem(ImportFromPcCard, () => "Choose a PC", () => ImportPcRows(backup)) { SearchesRows = false };

        _addFolder = new FileItem(AddFolderCard, FileKind.OfFolder("Choose a folder with plug-ins"), AddFolder);
        _scan = new ButtonItem(ScanCard, "Scan now", StartScan);
        _keepFx = mixer is null ? null : KeepFxCard(mixer);
        _resetAllConfirmations = new ButtonItem(ResetConfirmationsCard, "Reset all", confirmations.ResetAll);
        _restoreAllCard = new ButtonItem(RestoreAllCard, "Restore all", () => _ = RestoreAll?.RestoreAsync());
        _networkService = new ButtonItem(NetworkServiceCard, "Remove…",
            () => _ = Guard.RunAsync("Remove the network service", async () => report(await NetworkServiceMode.RemoveAsync() ?? "The network service was removed.")));
        MakeUpdateCards(updates, report);
        _featureCards = MakeFeatureCards();
        ReadyMade = new MenuChoiceItem(ReadyMadeCard, () => $"Ready-made: {FeatureList.ReadyMadeOf(_features.Off)}", ReadyMadeRows);
        TurnAllOnCommand = new RelayCommand(() => _features.SetOff(Array.Empty<string>()), () => _features.Off.Count > 0);

        // Each group is made from its fixed cards (its closed header lists their names), then the cards that come and go are added.
        Appearance = Fill(AppearanceItems, "AppSettings/Appearance", AppearanceName, Theme, Background, Accent, _accentIntensity, _groupsOpen, _gpuDrawing, _trayIcon, _trayColour);
        StartAndClose = Fill(StartItems, "AppSettings/Start", StartName, _startWithWindows, _startMinimised, _whenClosing, _hardwareCheck, _uiRelease, _keepPages);
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
        Guard.OffUi("App settings: read Start with Windows", () =>
        {
            var state = StartupRegistration.State; // the registry, off the UI thread
            Dispatcher.UIThread.Post(() => Guard.Run("App settings: show Start with Windows", () => ShowStartup(state)));
        });
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
    private void ReadStartup() => ShowStartup(StartupRegistration.State);

    private void ShowStartup(StartupState state)
    {
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
        _keepPages.Load(_settings.KeepRecentPages);
    }

    // Feature cards' restart words, so a restart-only switch reads the same everywhere.
    private void ShowGpuDrawingNote() => _gpuDrawing.Note = _settings.GpuDrawing != _gpuDrawingAtStart
        ? "Takes effect after restart"
        : GpuCard.Note;

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
