using System.Collections.ObjectModel;
using OmniControl.App.Dialogs;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.AppLists;
using OmniControl.App.Hotkeys;
using OmniControl.App.Pages;
using OmniControl.App.Pages.AppSettingsPage;
using OmniControl.App.Pages.Displays;
using OmniControl.App.Osd;
using OmniControl.App.Pages.Osd;
using OmniControl.App.Mouse;
using OmniControl.App.Pages.Mouse;
using OmniControl.App.Pages.Presets;
using OmniControl.App.Presets;
using OmniControl.App.Gestures;
using OmniControl.App.Rules;
using OmniControl.App.Apps;
using OmniControl.App.Automation;
using OmniControl.App.Pages.Automation;
using OmniControl.App.Pages.Gestures;
using OmniControl.App.Pages.Devices;
using OmniControl.App.Pages.OtherPcs;
using OmniControl.App.OtherPcs;
using OmniControl.App.Devices;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Mouse;
using OmniControl.App.Picker;
using OmniControl.App.Pages.Volume;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Services;
using OmniControl.App.Services.Backup;
using OmniControl.App.Services.Updates;
using OmniControl.App.Fx;
using OmniControl.App.Fx.Plugins;
using OmniControl.App.Display;
using OmniControl.App.About;
using OmniControl.Core;

namespace OmniControl.App.Shell;

public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly MonitorEngine _engine = new();
    private readonly SoftwareDisplayService _softwareDisplays = new();
    private readonly HotkeyRegistry _hotkeys = new();
    private readonly Icons.IconStore _icons; // the user's icons and the chosen icons of actions, made right after the master list it follows
    private readonly Icons.IconPicker _iconPicker;
    private readonly States.StateRegistry _states; // the master list of states, made before the services and pages that register theirs
    private readonly AppActions _appActions;
    private readonly Power.PowerPlanActions _powerPlanActions;
    private readonly PresetGroups _presetGroups = new(); // made before the pages, which register their preset groups in it
    private readonly OsdService _osd = new();
    // The effects journal is read first: a previous run that ended without putting programs' sound back is undone before the
    // mixer looks at the programs (field initialisers run in this order).
    private readonly EngineJournal _journal = EngineJournal.Recover();
    private readonly MixerService _mixer;
    private readonly EffectsSupportService _effectsSupport;
    private readonly VolumePresets _volumePresets;
    private VolumePageViewModel? _volumePage; // read by the FX windows' links to the Mixer when they are used
    private readonly PluginChainHost _plugins;
    private readonly PluginLibrary _library = new();
    private readonly UserPresetStore _presets = UserPresetStore.OnDisk();
    private readonly PresetShortcuts _presetShortcuts;
    private readonly PresetManagerService _presetManager;
    private readonly EffectsEngine _effects;
    private readonly EngineInstaller _installer;
    private readonly FxWindowService _fxWindows;
    private readonly DialogService _dialogs = new();
    private readonly AboutWindowService _about;
    private readonly UpdateService _updates = UpdateService.Create(AppSettings.Current);
    private readonly AppListStore _appLists = new(AppListSettings.Load()); // the master list of app lists, made before the services and pages that limit things to apps
    private readonly AppListManagerService _appListManager;
    private readonly ActionsWindowService _actionsWindow;
    private readonly HotkeysSwitch _hotkeysSwitch;
    private readonly AppScopes _appScopes;
    private readonly MouseHook _mouseHook = new(); // one system-wide hook shared by the mouse buttons and the gestures
    private readonly GestureService _gestures;
    private readonly MouseService _mouse;
    private readonly TrackpadService _trackpad;
    private readonly PointerSettingsService _pointer = new(); // at start-up it puts back a button swap a crashed run left on trial
    private readonly RuleService _rules;
    private readonly AutomationService _automation;
    private readonly DeviceService _devices;
    private readonly OtherPcsService _otherPcs;
    private readonly MarginOverlay _margins;
    private readonly IReadOnlyList<PageViewModel> _allPages;
    private bool _syncing;
    private bool _windowShown; // the main window is showing and not minimised
    private PageViewModel? _wired; // the open page once it has been wired (DrawFirst); null until then

    /// <summary>Feature pages in the sidebar (after search filtering).</summary>
    public ObservableCollection<PageViewModel> Pages { get; } = new();

    /// <summary>What the sidebar list shows: the <see cref="Pages"/> with a <see cref="NavHeading"/> before the first page of each group.</summary>
    public ObservableCollection<object> NavItems { get; } = new();

    /// <summary>Pages pinned to the bottom of the sidebar (app-level, not features).</summary>
    public ObservableCollection<PageViewModel> FooterPages { get; } = new();

    /// <summary>The entries at the bottom of the sidebar under a divider that open a window, not a page (Actions and hotkeys).</summary>
    public IReadOnlyList<NavLauncher> PinnedEntries { get; }
    private readonly IReadOnlyList<PageViewModel> _allFooter;

    public string VersionText => AppInfo.VersionText;

    /// <summary>The page shown on the right. Set via the two sidebar lists below.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsActiveLayer))]
    private PageViewModel? _currentPage;

    /// <summary>The footer names the mouse layer in force only on the Mouse and Trackpad page.</summary>
    public bool ShowsActiveLayer => CurrentPage is MousePageViewModel;
    [ObservableProperty] private PageViewModel? _selectedMainPage;
    [ObservableProperty] private PageViewModel? _selectedFooterPage;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>The footer's tooltip: the full text of the message whose short line <see cref="Status"/> shows.</summary>
    [ObservableProperty] private string _statusTip = string.Empty;

    /// <summary>The footer line naming the mouse layer in force.</summary>
    [ObservableProperty] private string _activeLayer = string.Empty;

    public ShellViewModel()
    {
        _hotkeys.Report = Report;
        _hotkeys.Settings = new OmniActionSettings();
        _mixer = new MixerService(Report); // after the effects journal (a field initialiser): a left-over run is undone first
        _states = new States.StateRegistry(chainNow: () => _hotkeys.Chain);
        _icons = new Icons.IconStore(_hotkeys);
        _iconPicker = new Icons.IconPicker(_icons);
        // Presets, in this order: the master list of groups (a field, empty), the preset shortcuts (they follow the list and register a
        // preset's action in _hotkeys when its group arrives), then the one opener of the preset manager. All three come before the FX
        // windows and the pages, which are given the opener and register their groups; the manager's sections are given after the pages.
        _presetShortcuts = new PresetShortcuts(_hotkeys, _presetGroups, Report);
        _presetManager = new PresetManagerService(_presetGroups, _presetShortcuts, Report);
        _hotkeysSwitch = new HotkeysSwitch(_hotkeys, _states, AppSettingsPageViewModel.Name, Report);
        _actionsWindow = new ActionsWindowService(_hotkeys, _presetGroups, _presetManager.Open, _dialogs.AskAsync, OpenPage, _iconPicker.ChooseForActionAsync, _hotkeysSwitch, Report);
        PinnedEntries = new[] { new NavLauncher(ActionsWindowService.Title, ActionsWindowService.Icon, () => _actionsWindow.Open()) };
        _effectsSupport = new EffectsSupportService(new AudioEffectsProbe(), _mixer);
        _volumePresets = new VolumePresets(_mixer);
        _plugins = new PluginChainHost(_mixer.Save);
        _effects = new EffectsEngine(_mixer, _effectsSupport, _journal, _plugins, Report);
        _osd.WatchVolumeOf(() => _mixer.MasterDeviceId); // the pop-up for outside volume changes follows the master's real device
        _mixer.MasterDeviceChanged += _osd.RetargetVolume;
        _installer = new EngineInstaller(_effectsSupport);
        _fxWindows = new FxWindowService(_mixer, _effects, _effectsSupport, _plugins, _library, _presets, Report, _presetManager.Open,
            new FxMixerLinks(deviceId => { if (_volumePage is { } page) { OpenPage(page.Title); page.ShowMixerFor(deviceId); } }, () => _volumePage?.InstallEqualizerApoStepAsync() ?? Task.CompletedTask), _dialogs.AskAsync);
        _about = new AboutWindowService(Report);
        _appActions = new AppActions(new AppLauncher(Report), _hotkeys);
        _powerPlanActions = new Power.PowerPlanActions(new OmniControl.Core.Engine.PowerPlanService(), _hotkeys);
        _windowsKeys = Screenshot.ScreenshotActions.Register(_hotkeys, _osd, Report);
        _appListManager = new AppListManagerService(_appLists, () => _appActions.InstalledPrograms(), _dialogs.AskAsync, Report);
        _appScopes = new AppScopes(_appLists, _appListManager.Open);
        var appScopeGate = new AppScopeGate(_appLists);
        // Gestures are asked first, so a scroll or middle click they take never reaches the button mappings: the wheel gestures have
        // their own hook handler ahead of the mouse service's, and the mouse service asks for a middle-click gesture before its mapping.
        _gestures = new GestureService(_mouseHook, _hotkeys.RunLevel, Report, appScopeGate);
        _mouse = new MouseService(_mouseHook, _hotkeys.Run, _hotkeys.RunHeld, Report, _gestures.MiddleClickAt, appScopeGate, _states);
        _hotkeys.Moved += _mouse.FollowMoved;
        _hotkeys.Moved += _gestures.FollowMoved;
        // Custom rules are asked before both (their handler goes first on the hook), so a rule that matches wins.
        _rules = new RuleService(_mouseHook, _gestures, _mouse, _hotkeys, () => _mixer.Settings.Apps.Select(a => a.ExePath), Report);
        // The automation rules: they register their own actions in the master list and are the one gate every action passes.
        _automation = new AutomationService(_hotkeys, _states, CountdownWindow.ConfirmAsync, () => _mixer.Settings.Apps.Select(a => a.ExePath),
            StartupRegistration.IsBackgroundStart(Environment.GetCommandLineArgs().Skip(1)), Report);
        // Trackpad gestures are handed to the same hook, so rules and mappings treat them like button presses.
        _trackpad = new TrackpadService(_mouseHook, _mouse);
        _margins = new MarginOverlay(_gestures.Settings);
        _mouse.LayerChanged += (_, _) => UpdateActiveLayer();
        _mouse.LayersChanged += UpdateActiveLayer;
        UpdateActiveLayer();
        _mouseHook.Log = line => AppDataStore.AppendLog("mouse-log.txt", line);
        var hookStarted = true;
        if (!StartupMode.Inert && Guard.Run("Start-up: mouse hook", () => hookStarted = _mouseHook.Start()) is not null) hookStarted = false;
        if (!hookStarted) Report("Couldn't start mouse remapping and gestures: Windows refused the mouse hook.");

        // The devices are read from the service (start-up and periodic checks), so the tray tiles are right before the page is ever opened.
        ICredentialStore credentials = StartupMode.Inert ? new NoCredentialStore() : new WindowsCredentialStore();
        _devices = new DeviceService(_hotkeys, _states, credentials, Report);
        Guard.Run("Start-up: device auto-checks", _devices.StartAutoChecks);
        _otherPcs = new OtherPcsService(_hotkeys, _states, credentials, Report, _devices.ShareSetups); // listens only once a PC is paired or "Pair a PC" is opened

        // Register new feature pages here, in sidebar order (a page's NavGroup decides its heading; keep the pages of a group together). Pages that list actions (Mouse, Gestures) follow the master
        // list's changes, so the order does not decide which actions they offer.
        // Each page starts on its own: one that fails is logged and left out, and the window still opens.
        _allPages = new[]
        {
            TryMake("Displays", () => new DisplaysPageViewModel(_engine, _hotkeys, _osd, _softwareDisplays, Report, _presetGroups, _presetManager.Open, _states)),
            TryMake("Volume", () => new VolumePageViewModel(_mixer, _effectsSupport, _effects, _installer, _hotkeys, _osd, Report, _fxWindows.Open, _fxWindows.Toggle,
                _dialogs.AskAsync, CombineWindow.AskAsync, _volumePresets, _presetGroups, _presetManager.Open, _states)),
            TryMake("Devices", () => new DevicesPageViewModel(_devices, _dialogs.AskAsync, Report, _iconPicker.ChooseAsync, _otherPcs, () => OpenPage(OtherPcsService.Category))),
            TryMake("Other PCs", () => new OtherPcsPageViewModel(_otherPcs, _dialogs.AskAsync, Report)),
            TryMake("Mouse and Trackpad", () => new MousePageViewModel(_mouse, _trackpad, _pointer, _hotkeys, _osd, _dialogs.AskAsync, Report, _presetGroups, _presetManager.Open, _appScopes)),
            TryMake("Gestures", () => new GesturesPageViewModel(_gestures, _rules, _margins, _hotkeys, _dialogs.AskAsync, _appScopes)),
            TryMake("Automation", () => new AutomationPageViewModel(_automation, _dialogs.AskAsync)),
            TryMake("OSD", () => new OsdPageViewModel(_osd, _hotkeys, _presetGroups, _presetManager.Open, OsdPlacementWindow.AskAsync)),
        }.OfType<PageViewModel>().ToArray();
        // The effects' preset groups belong to the Volume page; they are made by the FX windows, which know how a preset reaches an effect.
        _volumePage = _allPages.OfType<VolumePageViewModel>().FirstOrDefault();
        if (_volumePage is { } volumePage) _presetGroups.Register(_fxWindows.PresetGroupsFor(volumePage.Title));
        _presetManager.UseSections(_allPages.Select(p => new PresetSection(p.Title, p.Icon))
            .Append(new PresetSection(ActionsWindowService.Title, ActionsWindowService.Icon)).ToList());
        _allFooter = new[]
        {
            TryMake("App settings", () => new AppSettingsPageViewModel(_hotkeys, _about.Open, _library.ScanAsync, _library.FoundText, () => _presetManager.Open(null, null),
                new BackupService(_dialogs.ChooseAsync, Report), _dialogs.Confirmations, _updates, Report, ShowKeyboardShortcuts, _mixer)),
        }.OfType<PageViewModel>().ToArray();
        // The Choose-the-action window's sections are the sidebar pages, in the sidebar's order, with their icons.
        _hotkeys.UseSections(_allPages.Concat(_allFooter).Select((p, i) => new ActionSection(p.Title, p.Icon, i + 1)).ToList());

        WireRestore();
        TrayFlyout = MakeTray();
        WatchUpdates();

        Categories = new[] { AllCategories }.Concat(_allPages.Concat(_allFooter).Select(p => p.Title)).ToArray();
        _selectedCategory = AllCategories;

        ApplyFilter();
    }

    /// <summary>Makes one page; a page that fails to start is logged, said in one plain sentence and left out.</summary>
    private PageViewModel? TryMake(string name, Func<PageViewModel> make)
    {
        try
        {
            return make();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Guard.Log($"Start-up: {name} page", ex);
            Report($"The {name} page couldn't start. Details are in {Guard.LogFile}.");
            return null;
        }
    }

    private const string AllCategories = "All categories";

    /// <summary>Entries of the category filter next to the search box.</summary>
    public IReadOnlyList<string> Categories { get; }

    [ObservableProperty] private string _selectedCategory;

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedMainPageChanged(PageViewModel? value)
    {
        if (value is null || _syncing) return;
        _syncing = true;
        SelectedFooterPage = null;
        _syncing = false;
        Show(value);
    }

    /// <summary>Opens the sidebar page with this title (a page hidden by the search filter stays closed).</summary>
    private void OpenPage(string title)
    {
        if (Pages.FirstOrDefault(p => p.Title == title) is { } page) SelectedMainPage = page;
        else if (FooterPages.FirstOrDefault(p => p.Title == title) is { } footer) SelectedFooterPage = footer;
    }

    partial void OnSelectedFooterPageChanged(PageViewModel? value)
    {
        if (value is null || _syncing) return;
        _syncing = true;
        SelectedMainPage = null;
        _syncing = false;
        Show(value);
    }

    /// <summary>The footer's App settings button: opens the page it stands for.</summary>
    [RelayCommand]
    private void OpenFooterPage(PageViewModel? page) => SelectedFooterPage = page;

    // Draw first, wire later (DrawFirst): the page is drawn at once from what it holds, then it reads the machine and comes on screen.
    private void Show(PageViewModel page)
    {
        if (page == CurrentPage) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (_windowShown) _wired?.OnOnScreenChanged(false);
        _wired = null;
        if (CurrentPage is not null) CurrentPage.IsOpen = false;
        CurrentPage = page;
        page.IsOpen = true;
        UiTiming.Shown($"Page {page.Title}", clock);
        DrawFirst.ThenWire(() => Wire(page, clock));
    }

    private void Wire(PageViewModel page, System.Diagnostics.Stopwatch clock)
    {
        if (page != CurrentPage) return; // another page was opened before this one was wired
        _wired = page;
        if (StartupMode.MayRead) page.OnShown(); // a page's look at the system is for a screenshot export only when it is read-only (a page that acts on devices gates itself)
        page.Restorer?.Refresh();
        if (_windowShown) page.OnOnScreenChanged(true);
        UiTiming.Write($"Page {page.Title}: wired {clock.ElapsedMilliseconds} ms");
    }

    /// <summary>The main window was shown, hidden, minimised or restored: the open page comes on or goes off screen with it.</summary>
    public void SetWindowShown(bool shown)
    {
        if (shown == _windowShown) return;
        _windowShown = shown;
        if (shown) _appActions.Refresh();
        _wired?.OnOnScreenChanged(shown); // a page not wired yet comes on screen when it is
        foreach (var page in _allPages) page.OnWindowShownChanged(shown);
    }

    /// <summary>Windows reported a display change (reported by <see cref="WindowLifecycle"/>).</summary>
    public void NotifyDisplaysChanged()
    {
        foreach (var page in _allPages) page.OnDisplaysChanged();
    }

    /// <summary>The one way anything reports to the screen; safe from any thread, so services never post it themselves.</summary>
    private void Report(string message)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Report(message));
            return;
        }
        Status = StatusMessage.BriefOf(message);
        StatusTip = StatusMessage.FullOf(message);
    }

    /// <summary>The main window's own keys (MainWindow.axaml.cs), for the Keyboard shortcuts window.</summary>
    private const string ShellKeys =
        "Tab moves between the sidebar and the page; Up/Down move through the sidebar (round at the ends). "
        + "Ctrl+F searches: Down or Enter goes to the results, Esc goes back. F1 shows this list.";

    /// <summary>The Keyboard shortcuts window (F1 and the App settings action): the open page's own keys, the keys every card list shares and the main window's, in the small message window.</summary>
    [RelayCommand]
    private void ShowKeyboardShortcuts()
    {
        var sections = new List<string>();
        if (!IsSearching && CurrentPage is { KeyHelp.Length: > 0 } page) sections.Add(page.Title + Environment.NewLine + page.KeyHelp);
        sections.Add("Cards" + Environment.NewLine + PageViewModel.CardKeys);
        sections.Add("Everywhere" + Environment.NewLine + ShellKeys);
        _ = _dialogs.AskAsync(new DialogMessage("Keyboard shortcuts", string.Join(Environment.NewLine + Environment.NewLine, sections)));
    }

    // The trackpad's layer in force is shown by its own drop-down on the same page; the footer keeps one short fact.
    private void UpdateActiveLayer() => ActiveLayer = $"Mouse layer: {_mouse.Settings.LayersOf(InputFamily.Mouse).Labels()[_mouse.LayerOf(InputFamily.Mouse)]}";

    public void Dispose()
    {
        // Every step on its own: one that throws is logged and the next still runs, so the steps that put Windows back are never skipped.
        void Step(string what, Action step) => Guard.Run($"Shut-down: {what}", step);
        Step("search", () => _search?.Dispose());
        Step("restore", DisposeRestore);
        Step("win+a key", _winA.Dispose); // gives Win+A back to Windows
        Step("tray", TrayFlyout.Dispose);
        Step("preset manager", _presetManager.Close);
        Step("app list manager", _appListManager.Close);
        Step("actions window", _actionsWindow.Dispose);
        Step("hotkeys switch", _hotkeysSwitch.Dispose);
        Step("preset shortcuts", _presetShortcuts.Dispose);
        Step("preset groups", _presetGroups.Dispose);
        foreach (var page in _allPages.OfType<IDisposable>()) Step(page.GetType().Name, page.Dispose);
        Step("software displays", _softwareDisplays.Dispose); // puts every display's picture back
        Step("margins", _margins.Dispose);
        Step("trackpad", _trackpad.Dispose);
        Step("pointer", _pointer.Dispose); // a button swap that was never kept goes back
        Step("automation", _automation.Dispose);
        Step("rules", _rules.Dispose);
        _hotkeys.Moved -= _mouse.FollowMoved;
        _hotkeys.Moved -= _gestures.FollowMoved;
        Step("mouse", _mouse.Dispose);
        Step("mouse hook", _mouseHook.Dispose);
        Step("other PCs", _otherPcs.Dispose); // its link ends and what other PCs shared leaves the master lists
        Step("icons", _icons.Dispose);
        Step("hotkeys", _hotkeys.Dispose);
        _mixer.MasterDeviceChanged -= _osd.RetargetVolume;
        Step("OSD", _osd.Dispose);
        Step("effect windows", _fxWindows.CloseAll); // plug-in editors close (and save) before the plug-ins are unloaded
        Step("effects", _effects.Dispose); // every program's own sound is put back, the APO gets an empty chain, plug-in settings are saved
        Step("about window", _about.Close);
        Step("update check", _updates.Dispose);
        Step("effects support", _effectsSupport.Dispose);
        Step("volume presets", _volumePresets.Dispose);
        Step("mixer", _mixer.Dispose);
        Step("engine", _engine.Dispose);
        // Last: a plug-in's own teardown can hang (a JUCE plug-in disconnecting every UI Automation provider of the process
        // deadlocks with Avalonia's), so it runs after everything is saved and put back, and the process ends regardless.
        using (ExitGuard.Start(Timing.PluginUnloadLimit)) Step("plug-ins", _plugins.Dispose);
    }
}
