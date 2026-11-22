using Avalonia.Controls.Templates;
using OmniControl.App.Features;
using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Automation;
using OmniControl.App.Features.Automation.Power;
using OmniControl.App.Features.Devices;
using OmniControl.App.Features.Displays;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.Icons;
using OmniControl.App.Features.Input;
using OmniControl.App.Features.Osd;
using OmniControl.App.Features.OtherPcs;
using OmniControl.App.Features.OtherPcs.SettingsImport;
using OmniControl.App.Features.Resources;
using OmniControl.App.Features.Traffic;
using OmniControl.App.Features.Traffic.Pages;
using OmniControl.App.Features.Screenshot;
using OmniControl.App.Features.Settings.About;
using OmniControl.App.Shared.Ui.Restore;
using OmniControl.App.Features.Settings.Pages;
using OmniControl.App.Features.Settings.Pages.Backup;
using OmniControl.App.Features.States;
using OmniControl.App.Features.Volume;
using OmniControl.App.Shared.Presets;
using OmniControl.App.Shared.Presets.Manager;
using OmniControl.App.Shared.Services.Updates;

namespace OmniControl.App.Shell;

public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly DisplaysModule _displays = new(); // made first: the software displays put back a picture a crashed run left changed
    private readonly HardwareWatches _hardware = new(); // the master list of hardware watches, made first: the registry and the features that depend on hardware are given it
    private readonly PowerJournal _power; // the power journal: hears sleeps, wakes and log-ins through the hardware watches' listeners
    private readonly HotkeyRegistry _hotkeys;
    private readonly ActionGateSlot _actionGate = new(); // the Automation feature attaches its block rules here; the registry asks it before every run
    private readonly IconStore _icons; // the user's icons and the chosen icons of actions, made right after the master list it follows
    private readonly IconPicker _iconPicker;
    private readonly StateRegistry _states; // the master list of states, made before the services and pages that register theirs
    private readonly PresetGroups _presetGroups = new(); // made before the pages, which register their preset groups in it
    private readonly VolumeModule _volume = new(); // reads the effects journal when made, so a left-over run is undone before anything looks at programs
    private readonly PresetShortcuts _presetShortcuts;
    private readonly PresetTriggersSlot _presetTriggers;
    private readonly PresetManagerService _presetManager;
    private readonly DialogService _dialogs = new();
    private readonly AboutWindowService _about;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly UpdateService _updates = UpdateService.Create(AppSettings.Current);
    private readonly ActionsWindowService _actionsWindow;
    private readonly HotkeysSwitch _hotkeysSwitch;
    private readonly ImportHooks _importHooks = new();
    private readonly ShutdownSteps _shutdown = new();
    private readonly InputModule _input;
    private readonly OsdModule _osd;
    private readonly AppsModule _apps;
    private readonly ScreenshotModule _screenshot = new();
    private readonly DevicesModule _devices;
    private readonly OtherPcsModule _otherPcs;
    private readonly IReadOnlyList<IFeatureModule> _modules; // in start-up order
    private readonly IReadOnlyList<IFeatureModule> _sidebar; // the modules with pages, in sidebar order
    private IReadOnlyList<PageSlot> _allPages = Array.Empty<PageSlot>(); // the running modules' pages, in sidebar order
    private readonly PageSlot _appSettings;
    private bool _syncing;
    private bool _windowShown; // the main window is showing and not minimised
    private PageSlot? _openSlot; // the sidebar or footer entry shown on the right
    private PageViewModel? _wired; // the open page once it has been wired (DrawFirst); null until then

    /// Feature pages in the sidebar (after search filtering).
    public ObservableCollection<PageSlot> Pages { get; } = new();

    /// What the sidebar list shows: the <see cref="Pages"/> with a <see cref="NavHeading"/> before the first page of each group.
    public ObservableCollection<object> NavItems { get; } = new();

    /// Pages pinned to the bottom of the sidebar (app-level, not features).
    public ObservableCollection<PageSlot> FooterPages { get; } = new();

    /// The entries at the bottom of the sidebar under a divider that open a window, not a page (Actions and hotkeys).
    public IReadOnlyList<NavLauncher> PinnedEntries { get; }
    private readonly IReadOnlyList<PageSlot> _allFooter;

    /// Every page, sidebar then footer.
    private IEnumerable<PageSlot> AllSlots => _allPages.Concat(_allFooter);

    public string VersionText => AppInfo.VersionText;

    /// The page shown on the right (null while its view-model could not be made). Set via the two sidebar lists below.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShownPage))] private PageViewModel? _currentPage;

    /// The page whose view the page area holds: none while the search results show, so their view and a page's are never both built.
    public PageViewModel? ShownPage => IsSearching ? null : CurrentPage;

    /// The pages whose views the page area keeps built (<see cref="PageViewKeeper"/>).
    [ObservableProperty] private IReadOnlyList<PageViewModel>? _keptPages;

    private readonly PageViewKeeper _pageViews;

    private readonly DispatcherTimer _hiddenSettle = new() { Interval = Timing.HiddenSettle };
    private readonly DispatcherTimer _releaseUi = new();

    /// The main window has stayed hidden as long as App settings' "Free memory after closing" says (<see cref="UiReleases"/>): whoever owns the main window lets it go (never the tray
    /// panel) and gives null, or gives why it stays (a window it owns is open), and is asked again after another wait.
    public event Func<string?>? ReleaseWindows;
    [ObservableProperty] private PageSlot? _selectedMainPage;
    [ObservableProperty] private PageSlot? _selectedFooterPage;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    /// The footer's tooltip: the full text of the message whose short line <see cref="Status"/> shows.
    [ObservableProperty] private string _statusTip = string.Empty;

    public ShellViewModel()
    {
        _pageViews = new PageViewKeeper(() => _settings.KeepRecentPages, () => UiReleases.AfterOf(_settings.UiRelease), kept => KeptPages = kept);
        _features = new FeatureSwitches(_settings.Slot(s => s.FeaturesOff, (s, off) => s.FeaturesOff = off),
            _settings.Slot(s => s.FeaturesFromSetup, (s, chosen) => s.FeaturesFromSetup = chosen));
        _input = new InputModule(_volume.RememberedPrograms); // its pointer settings put back a button swap a crashed run left on trial
        _apps = new AppsModule(_dialogs.AskAsync, Report);
        _osd = new OsdModule(() => _apps.ListedPrograms());
        _hotkeys = new HotkeyRegistry(_hardware, Report, new OmniActionSettings(), () => _states, _actionGate);
        _states = new StateRegistry(chainNow: () => _hotkeys.Chain);
        _icons = new IconStore(_hotkeys);
        _iconPicker = new IconPicker(_icons);
        // Presets, in this order: the master list of groups (a field, empty), the preset shortcuts (they follow the list and register a
        // preset's action in _hotkeys when its group arrives), then the one opener of the preset manager. All three come before the
        // modules, which are given the opener and register their groups; the manager's sections are given after the pages.
        _presetShortcuts = new PresetShortcuts(_hotkeys, _presetGroups, Report);
        _presetTriggers = new PresetTriggersSlot(_presetShortcuts);
        _presetManager = new PresetManagerService(_presetGroups, _presetShortcuts, _presetTriggers, Report);
        _hotkeysSwitch = new HotkeysSwitch(_hotkeys, _states, AppSettingsPageViewModel.Name, Report, _settings.Slot(s => s.HotkeysOn, (s, on) => s.HotkeysOn = on));
        _actionsWindow = new ActionsWindowService(_hotkeys, _presetGroups, _presetManager.Open, _dialogs.AskAsync, OpenPage, _iconPicker.ChooseForActionAsync, _hotkeysSwitch, Report);
        PinnedEntries = new[] { new NavLauncher(ActionsWindowService.Title, ActionsWindowService.Icon, () => _actionsWindow.Open()) };
        _about = new AboutWindowService(Report);
        _power = new PowerJournal(_hardware);
        _devices = new DevicesModule(() => _otherPcs?.Paired, () => OpenPage(OtherPcsService.Category));
        _otherPcs = new OtherPcsModule(_devices.ShareSetups, DeviceShareFormat.RedactFile);
        _context = new FeatureContext(_hotkeys, _hotkeys, _states, _hardware, _osd.Osd, _presetGroups, _presetManager.Open, _dialogs.AskAsync, _dialogs.AskTextAsync,
            Report, OpenPage, _apps.Scopes, _apps.Gate, _iconPicker, _actionGate, _power, setId => new SuggestedKeysSwitch(_hotkeys, setId), _settings, _importHooks, _shutdown,
            _features);
        var automation = new AutomationModule(_volume.RememberedPrograms, _hotkeysSwitch.Use, _presetTriggers);
        var resources = new ResourcesModule(TrafficPageViewModel.PageTitle);
        var traffic = new TrafficModule(resources.Settings);

        // Every feature, in start-up order: it decides the order of the master lists (actions, states, hardware watches) and of the
        // mouse hook's handlers (inside the Input module), so keep it. Only the features switched on start (ShellViewModel.Features.cs).
        _modules = new IFeatureModule[] { _volume, _apps, new PowerModule(), _screenshot, _input, automation, _devices, _otherPcs, _osd, _displays, resources, traffic };
        foreach (var module in _modules.Where(Wanted)) Begin(module);
        // What an import from another PC may reload live of what the shell itself owns; each module says so for its own.
        RegisterReloads();

        // The sidebar, in order (a page's NavGroup decides its heading; keep the pages of a group together). Each page registers its
        // entries here, in this order; its view-model is made when it is first needed.
        _sidebar = new IFeatureModule[] { _displays, _volume, _devices, _otherPcs, _input, automation, _osd, resources, traffic };
        foreach (var module in _sidebar.Where(_running.ContainsKey)) MakePages(module);
        foreach (var module in _modules.Where(_running.ContainsKey)) module.PagesMade();
        _allPages = RunningPages();
        TrayFlyout = MakeTray();
        _appSettings = new PageSlot(AppSettingsPageViewModel.Info, () => new AppSettingsPageViewModel(_about.Open, _volume.Library,
            _presetManager, new BackupService(_dialogs.ChooseAsync, Report, _otherPcs.ImportFrom), _dialogs.Confirmations, _updates, Report,
            _volume.Mixer, _hardware, _settings, TrayFlyout, _features), () => AppSettingsPageViewModel.Words(_dialogs.Confirmations, _settings, _presetManager),
            AppSettingsRestore(), OnPageMade, Report);
        RegisterAppActions();
        _allFooter = new[] { _appSettings };
        UseSections();

        Guard.Run("Start-up: power journal", _power.Start);
        _hardware.Start();
        _hardware.ApplyCheck();
        WireRestore();
        RegisterTray(TrayFlyout);
        WatchUpdates();

        _selectedCategory = AllCategories;
        Categories = CategoriesNow();
        _features.Changed += ApplyFeatures;

        ApplyFilter();
        _hiddenSettle.Tick += OnHiddenSettled;
        _hiddenSettle.Start(); // the window is not shown yet (it may start in the tray)
        _releaseUi.Tick += OnReleaseDue;
        ArmRelease();
        _searchShow.Tick += OnSearchShowDue;
        _searchMake.Tick += OnSearchMakeDue;
        if (AppDataStore.TakeDamaged() is { Count: > 0 } damaged)
            Report(Guard.SeeLog($"Damaged settings were reset to their defaults; the damaged files are kept as {string.Join(", ", damaged.Select(AppDataStore.BadCopyOf))}."));
    }

    /// The window has stayed hidden a while: one full collection gives what its use left behind back to Windows. The open page's view
    /// stays (<see cref="PageHost"/>) until <see cref="OnReleaseDue"/>, so showing it again soon rebuilds nothing.
    private void OnHiddenSettled(object? sender, EventArgs e)
    {
        _hiddenSettle.Stop();
        UiTiming.Memory(TryCompact(ProcessPower.Compact) ? "hidden settle" : "hidden settle (sound running, not collected)");
    }

    /// Start-up has settled: what it needed only is given back (<see cref="TryCompact"/>).
    public void CompactAfterStart() => TryCompact(ProcessPower.Compact);

    /// Every explicit collection goes through here: skipped while OmniControl itself plays or records sound, since even a requested
    /// background collection may pause its threads briefly. False when skipped.
    private bool TryCompact(Action compact)
    {
        if (_volume.StreamsAudio || _screenshot.Records) return false;
        Guard.Run("Give memory back", compact);
        return true;
    }

    /// The window has stayed hidden a long while: the windows go (the page view-models stay, so the next showing opens at the same page),
    /// then one compacting collection gives their memory back.
    private void OnReleaseDue(object? sender, EventArgs e)
    {
        _releaseUi.Stop();
        var before = UiTiming.Visuals();
        string? kept = null;
        Guard.Run("Let the windows go", () =>
        {
            kept = ReleaseWindows?.Invoke();
            _actionsWindow.Release();
        });
        if (kept is not null)
        {
            UiTiming.Write($"Main window kept after its long hide: {kept}; asked again after the same wait");
            ArmRelease();
            return;
        }
        DispatcherTimer.RunOnce(() => CompactAfterRelease(before), Timing.UiReleaseCompactAfter);
    }

    /// Starts the wait before the windows go, as long as the setting says now (a hide already counting keeps its wait); "Never" starts none.
    private void ArmRelease()
    {
        if (UiReleases.AfterOf(_settings.UiRelease) is not { } after) return;
        _releaseUi.Interval = after;
        _releaseUi.Start();
    }

    private void CompactAfterRelease(string before)
    {
        if (_windowShown) return;
        UiTiming.Memory(TryCompact(ProcessPower.CompactAfterRelease) ? "windows let go and collected" : "windows let go (sound running, not collected)",
            $"before the release {before}");
    }

    /// Every feature's view templates and those of what the shell itself makes (App settings, the suggested keys switch); added to the app's at start-up.
    public IEnumerable<IDataTemplate> Templates => _modules.SelectMany(m => m.Templates)
        .Append(ViewTemplate.For<AppSettingsPageViewModel, AppSettingsPage>()).Append(ViewTemplate.For<SuggestedKeysSwitch, SuggestedKeysView>());

    /// A feature page's place in the sidebar: it registers its entries now and is made at its first use.
    private PageSlot Slot(FeaturePage page)
    {
        IRestorable? restorable = null;
        if (Guard.Run($"Start-up: {page.Info.Title} page's actions", () =>
            {
                page.Registers?.Invoke();
                restorable = page.Restore?.Invoke();
            }) is not null)
            Report(Guard.SeeLog($"The {page.Info.Title} page's actions couldn't start."));
        return new PageSlot(page.Info, page.Make, page.Words, restorable, OnPageMade, Report);
    }

    /// A page was just made (at its first opening, search or reveal): it gets its Restore defaults button, and App settings the combined restore.
    private void OnPageMade(PageSlot slot)
    {
        AttachRestore(slot);
        if (slot.IfMade is AppSettingsPageViewModel settings) settings.ShowRestoreAll(RestoreAll);
    }

    private const string AllCategories = "All categories";

    /// Entries of the category filter next to the search box: every page there is now.
    [ObservableProperty] private IReadOnlyList<string> _categories = Array.Empty<string>();

    private IReadOnlyList<string> CategoriesNow() => new[] { AllCategories }.Concat(AllSlots.Select(p => p.Title)).ToArray();

    [ObservableProperty] private string _selectedCategory;

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedMainPageChanged(PageSlot? value)
    {
        if (value is null || _syncing) return;
        _syncing = true;
        SelectedFooterPage = null;
        _syncing = false;
        Show(value);
    }

    /// Opens the sidebar page with this title (a page hidden by the search filter stays closed).
    private void OpenPage(string title) => OpenPage(title, null);

    /// THE way to open a page, at one of its tabs when <paramref name="tab"/> names one (the gadget's "Gadget settings…").
    private void OpenPage(string title, string? tab)
    {
        if (Pages.FirstOrDefault(p => p.Title == title) is { } page) SelectedMainPage = page;
        else if (FooterPages.FirstOrDefault(p => p.Title == title) is { } footer) SelectedFooterPage = footer;
        if (tab is not null) SlotOf(title)?.Page?.Tabs?.Select(tab);
    }

    private PageSlot? SlotOf(string title) => AllSlots.FirstOrDefault(p => p.Title == title);

    partial void OnSelectedFooterPageChanged(PageSlot? value)
    {
        if (value is null || _syncing) return;
        _syncing = true;
        SelectedMainPage = null;
        _syncing = false;
        Show(value);
    }

    /// The footer's App settings button: opens the page it stands for.
    [RelayCommand]
    private void OpenFooterPage(PageSlot? page) => SelectedFooterPage = page;

    // Draw first, wire later (DrawFirst): the page is drawn at once from what it holds, then it reads the machine and comes on screen.
    private void Show(PageSlot slot)
    {
        _searchShow.Stop(); // a page chosen while a search's page waits wins
        _searchTarget = null;
        if (slot == _openSlot) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (_windowShown) _wired?.OnOnScreenChanged(false);
        _wired = null;
        if (_openSlot is not null) _openSlot.IsOpen = false;
        _openSlot = slot;
        slot.IsOpen = true;
        CurrentPage = slot.Page; // made here at its first opening
        if (CurrentPage is { } opened) _pageViews.Opened(opened);
        UiTiming.Shown($"Page {slot.Title}", clock);
        if (CurrentPage is { } page) DrawFirst.ThenWire(() => Wire(page, clock));
    }

    private void Wire(PageViewModel page, System.Diagnostics.Stopwatch clock)
    {
        if (page != CurrentPage) return; // another page was opened before this one was wired
        _wired = page;
        page.OnShown();
        page.Restorer?.Refresh();
        if (_windowShown) page.OnOnScreenChanged(true);
        UiTiming.Write($"Page {page.Title}: wired {clock.ElapsedMilliseconds} ms");
    }

    /// The main window was shown, hidden, minimised or restored: the open page comes on or goes off screen with it. The release waits only
    /// while it is <paramref name="hidden"/> (a minimised window keeps its taskbar button, so it is never let go), counted from the hide.
    public void SetWindowShown(bool shown, bool hidden)
    {
        if (!hidden) _releaseUi.Stop();
        else if (!_releaseUi.IsEnabled) ArmRelease();
        if (shown == _windowShown) return;
        _windowShown = shown;
        _hiddenSettle.IsEnabled = !shown;
        FollowRestoreCheck(shown);
        if (shown) _apps.Refresh();
        if (shown && _openSlot is null && (SelectedMainPage ?? SelectedFooterPage) is { } first) Show(first);
        _wired?.OnOnScreenChanged(shown); // a page not wired yet comes on screen when it is
        foreach (var slot in _allPages) slot.IfMade?.OnWindowShownChanged(shown);
    }

    /// The one way anything reports to the screen; safe from any thread, so services never post it themselves.
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

    /// The main window's own keys (MainWindow.axaml.cs), for the Keyboard shortcuts window.
    private const string ShellKeys =
        "Tab moves between the sidebar and the page; Up/Down move through the sidebar (round at the ends). "
        + "Ctrl+F searches: Down or Enter goes to the results, Esc goes back. F1 shows this list.";

    /// The Keyboard shortcuts window (F1 and the App settings action): the open page's own keys, the keys every card list shares and the main window's, in the small message window.
    [RelayCommand]
    private void ShowKeyboardShortcuts()
    {
        var sections = new List<string>();
        if (!IsSearching && CurrentPage is { KeyHelp.Length: > 0 } page) sections.Add(page.Title + Environment.NewLine + page.KeyHelp);
        sections.Add("Cards" + Environment.NewLine + PageViewModel.CardKeys);
        sections.Add("Everywhere" + Environment.NewLine + ShellKeys);
        _ = _dialogs.AskAsync(new DialogMessage("Keyboard shortcuts", string.Join(Environment.NewLine + Environment.NewLine, sections)));
    }

    public void Dispose()
    {
        // The shell's own steps join the modules' at their stages (ShutdownStage); every step runs on its own.
        _features.Changed -= ApplyFeatures;
        _hiddenSettle.Stop();
        _hiddenSettle.Tick -= OnHiddenSettled;
        _releaseUi.Stop();
        _releaseUi.Tick -= OnReleaseDue;
        _pageViews.Dispose();
        _searchShow.Stop();
        _searchShow.Tick -= OnSearchShowDue;
        _searchMake.Stop();
        _searchMake.Tick -= OnSearchMakeDue;
        void Step(ShutdownStage stage, string what, Action step) => _shutdown.Add(stage, what, step);
        Step(ShutdownStage.Search, "search", () => _search?.Dispose());
        Step(ShutdownStage.Search, "search key", () =>
        {
            if (_searchKeyTip is null) return;
            _searchKeyTip.PropertyChanged -= OnSearchKeyChanged;
            _searchKeyTip.Dispose();
        });
        Step(ShutdownStage.Restore, "restore", DisposeRestore);
        Step(ShutdownStage.HardwareWatches, "hardware watches", _hardware.Dispose);
        Step(ShutdownStage.PowerJournal, "power journal", _power.Dispose);
        Step(ShutdownStage.WinAKey, "win+a key", _winA.Dispose);
        Step(ShutdownStage.Tray, "tray", TrayFlyout.Dispose);
        Step(ShutdownStage.PresetManager, "preset manager", _presetManager.Close);
        Step(ShutdownStage.ActionsWindow, "actions window", _actionsWindow.Dispose);
        Step(ShutdownStage.HotkeysSwitch, "hotkeys switch", _hotkeysSwitch.Dispose);
        Step(ShutdownStage.PresetShortcuts, "preset shortcuts", _presetShortcuts.Dispose);
        Step(ShutdownStage.PresetGroups, "preset groups", _presetGroups.Dispose);
        foreach (var page in AllSlots.Select(s => s.IfMade).OfType<IDisposable>()) Step(ShutdownStage.Pages, page.GetType().Name, page.Dispose);
        Step(ShutdownStage.Icons, "icons", _icons.Dispose);
        Step(ShutdownStage.Osd, "OSD", _osd.Dispose);
        Step(ShutdownStage.Hotkeys, "hotkeys", _hotkeys.Dispose);
        Step(ShutdownStage.AppWindows, "about window", _about.Close);
        Step(ShutdownStage.AppWindows, "update check", _updates.Dispose);
        Step(ShutdownStage.Flush, "waiting saves", AppDataStore.FlushSaves);
        Step(ShutdownStage.Flush, "log lines", AppDataStore.FlushLogs);
        _shutdown.Run();
    }
}
