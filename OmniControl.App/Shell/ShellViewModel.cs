using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Hotkeys;
using OmniControl.App.Pages;
using OmniControl.App.Pages.AppSettingsPage;
using OmniControl.App.Pages.Displays;
using OmniControl.App.Osd;
using OmniControl.App.Pages.Osd;
using OmniControl.App.Mouse;
using OmniControl.App.Pages.Mouse;
using OmniControl.App.Gestures;
using OmniControl.App.Rules;
using OmniControl.App.Pages.Gestures;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Mouse;
using OmniControl.App.Pages.Shortcuts;
using OmniControl.App.Pages.Volume;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Services;
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
    private readonly OsdService _osd = new();
    // The effects journal is read first: a previous run that ended without putting programs' sound back is undone before the
    // mixer looks at the programs (field initialisers run in this order).
    private readonly EngineJournal _journal = EngineJournal.Recover();
    private readonly MixerService _mixer = new();
    private readonly SoundModeService _soundModes;
    private readonly PluginChainHost _plugins;
    private readonly PluginLibrary _library = new();
    private readonly EffectsEngine _effects;
    private readonly EngineInstaller _installer;
    private readonly FxWindowService _fxWindows;
    private readonly AboutWindowService _about;
    private readonly MouseHook _mouseHook = new(); // one system-wide hook shared by the mouse buttons and the gestures
    private readonly GestureService _gestures;
    private readonly MouseService _mouse;
    private readonly TrackpadService _trackpad;
    private readonly PointerSettingsService _pointer = new(); // at start-up it puts back a button swap a crashed run left on trial
    private readonly RuleService _rules;
    private readonly MarginOverlay _margins;
    private readonly IReadOnlyList<PageViewModel> _allPages;
    private bool _syncing;
    private bool _windowShown; // the main window is showing and not minimised

    /// <summary>Feature pages in the sidebar (after search filtering).</summary>
    public ObservableCollection<PageViewModel> Pages { get; } = new();

    /// <summary>Pages pinned to the bottom of the sidebar (app-level, not features).</summary>
    public ObservableCollection<PageViewModel> FooterPages { get; } = new();
    private readonly IReadOnlyList<PageViewModel> _allFooter;

    public string Version { get; } = $"OmniControl {typeof(ShellViewModel).Assembly.GetName().Version!.ToString(2)}";

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

    /// <summary>The footer line naming the mouse layer in force.</summary>
    [ObservableProperty] private string _activeLayer = string.Empty;

    public ShellViewModel()
    {
        _hotkeys.Report = Report;
        _soundModes = new SoundModeService(new AudioEffectsProbe(), _mixer);
        _plugins = new PluginChainHost(_mixer.Save);
        _effects = new EffectsEngine(_mixer, _soundModes, _journal, _plugins, Report);
        _installer = new EngineInstaller(_soundModes);
        _fxWindows = new FxWindowService(_mixer, _effects, _soundModes, _plugins, _library, Report);
        _about = new AboutWindowService(Version, Report);
        // Gestures are asked first, so a scroll or middle click they take never reaches the button mappings: the wheel gestures have
        // their own hook handler ahead of the mouse service's, and the mouse service asks for a middle-click gesture before its mapping.
        _gestures = new GestureService(_mouseHook, _hotkeys.RunLevel, Report);
        _mouse = new MouseService(_mouseHook, _hotkeys.Run, Report, _gestures.MiddleClickAt);
        // Custom rules are asked before both (their handler goes first on the hook), so a rule that matches wins.
        _rules = new RuleService(_mouseHook, _gestures, _mouse, _hotkeys, () => _mixer.Settings.Apps.Select(a => a.ExePath), Report);
        // Trackpad gestures are handed to the same hook, so rules and mappings treat them like button presses.
        _trackpad = new TrackpadService(_mouseHook, _mouse);
        _margins = new MarginOverlay(_gestures.Settings);
        _mouse.LayerChanged += _ => UpdateActiveLayer();
        _mouse.LayersChanged += UpdateActiveLayer;
        UpdateActiveLayer();
        _mouseHook.Log = line => AppDataStore.AppendLog("mouse-log.txt", line);
        if (!_mouseHook.Start()) Report("Couldn't start mouse remapping and gestures: Windows refused the mouse hook.");

        // Register new feature pages here, in sidebar order. Pages that list actions (Shortcuts, Mouse, Gestures) follow the master
        // list's changes, so the order does not decide which actions they offer.
        _allPages = new PageViewModel[]
        {
            new DisplaysPageViewModel(_engine, _hotkeys, _osd, _softwareDisplays, Report),
            new VolumePageViewModel(_mixer, _soundModes, _effects, _installer, _hotkeys, _osd, Report, _fxWindows.Open,
                _fxWindows.AskAsync, CombineWindow.AskAsync),
            new OsdPageViewModel(_osd, _hotkeys),
            new MousePageViewModel(_mouse, _trackpad, _pointer, _hotkeys, _osd, _fxWindows.AskAsync, Report),
            new GesturesPageViewModel(_gestures, _rules, _margins, _hotkeys, _fxWindows.AskAsync),
            new ShortcutsPageViewModel(_hotkeys),
        };
        _allFooter = new PageViewModel[] { new AppSettingsPageViewModel(_hotkeys, _about.Open, _library.ScanAsync, _library.FoundText) };

        Categories = new[] { AllCategories }.Concat(_allPages.Concat(_allFooter).Select(p => p.Title)).ToArray();
        _selectedCategory = AllCategories;

        ApplyFilter();
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

    partial void OnSelectedFooterPageChanged(PageViewModel? value)
    {
        if (value is null || _syncing) return;
        _syncing = true;
        SelectedMainPage = null;
        _syncing = false;
        Show(value);
    }

    private void Show(PageViewModel page)
    {
        if (page == CurrentPage) return;
        if (_windowShown) CurrentPage?.OnOnScreenChanged(false);
        CurrentPage = page;
        page.OnShown();
        if (_windowShown) page.OnOnScreenChanged(true);
    }

    /// <summary>The main window was shown, hidden, minimised or restored: the open page comes on or goes off screen with it.</summary>
    public void SetWindowShown(bool shown)
    {
        if (shown == _windowShown) return;
        _windowShown = shown;
        CurrentPage?.OnOnScreenChanged(shown);
        foreach (var page in _allPages) page.OnWindowShownChanged(shown);
    }

    /// <summary>Windows reported a display change (reported by <see cref="WindowLifecycle"/>).</summary>
    public void NotifyDisplaysChanged()
    {
        foreach (var page in _allPages) page.OnDisplaysChanged();
    }

    // A page also matches through the actions it registered (their category is the page's title), so a new action makes its page
    // findable by its own words without anyone adding them to the page's keywords.
    private bool Matches(PageViewModel p, string q) =>
        (SelectedCategory == AllCategories || p.Title == SelectedCategory)
        && (q.Length == 0 || TextMatch.Any(q, p.Title, p.Keywords)
            || _hotkeys.Actions.Any(a => a.Category == p.Title && TextMatch.Any(q, a.Title, a.Heading, a.Keywords)));

    private void ApplyFilter()
    {
        var q = SearchText.Trim();

        Pages.Clear();
        foreach (var p in _allPages.Where(p => Matches(p, q))) Pages.Add(p);
        FooterPages.Clear();
        foreach (var p in _allFooter.Where(p => Matches(p, q))) FooterPages.Add(p);

        // Keep the open page selected if it still matches, otherwise jump to the first match.
        var target = CurrentPage is not null && (Pages.Contains(CurrentPage) || FooterPages.Contains(CurrentPage))
            ? CurrentPage
            : Pages.FirstOrDefault() ?? FooterPages.FirstOrDefault();

        _syncing = true;
        SelectedMainPage = target is not null && Pages.Contains(target) ? target : null;
        SelectedFooterPage = target is not null && FooterPages.Contains(target) ? target : null;
        _syncing = false;
        if (target is not null) Show(target);

        // Live results: the actual controls, so they can be edited right in the list.
        SearchResults.Clear();
        if (q.Length > 0)
            foreach (var e in _allPages.Concat(_allFooter).SelectMany(p => p.SearchEntries)
                         .Where(e => (SelectedCategory == AllCategories || e.Category == SelectedCategory) && e.Matches(q)))
                SearchResults.Add(e);
        IsSearching = q.Length > 0;
    }

    /// <summary>Editable settings matching the search text.</summary>
    public ObservableCollection<SearchEntry> SearchResults { get; } = new();

    [ObservableProperty] private bool _isSearching;

    private void Report(string message) => Status = message;

    /// <summary>The main window's own keys (MainWindow.axaml.cs), for the Keyboard shortcuts window.</summary>
    private const string ShellKeys =
        "Tab moves between the sidebar and the page; Up/Down move through the sidebar (round at the ends). "
        + "Ctrl+F searches: Down or Enter goes to the results, Esc goes back. F1 shows this list.";

    /// <summary>The footer's Keyboard shortcuts (and F1): the open page's own keys, the keys every card list shares and the main window's, in the small message window.</summary>
    [RelayCommand]
    private void ShowKeyboardShortcuts()
    {
        var sections = new List<string>();
        if (!IsSearching && CurrentPage is { KeyHelp.Length: > 0 } page) sections.Add(page.Title + Environment.NewLine + page.KeyHelp);
        sections.Add("Cards" + Environment.NewLine + PageViewModel.CardKeys);
        sections.Add("Everywhere" + Environment.NewLine + ShellKeys);
        _ = _fxWindows.AskAsync(new EngineMessage("Keyboard shortcuts", string.Join(Environment.NewLine + Environment.NewLine, sections)));
    }

    private void UpdateActiveLayer() => ActiveLayer = $"Active layer: {_mouse.Settings.LayerLabels()[_mouse.Layer]}";

    public void Dispose()
    {
        foreach (var page in _allPages.OfType<IDisposable>()) page.Dispose();
        _softwareDisplays.Dispose(); // puts every display's picture back
        _margins.Dispose();
        _trackpad.Dispose();
        _pointer.Dispose(); // a button swap that was never kept goes back
        _rules.Dispose();
        _mouse.Dispose();
        _mouseHook.Dispose();
        _hotkeys.Dispose();
        _osd.Dispose();
        _fxWindows.CloseAll(); // plug-in editors close (and save) before the plug-ins are unloaded
        _effects.Dispose(); // every program's own sound is put back, the APO gets an empty chain, plug-in settings are saved
        _about.Close();
        _soundModes.Dispose();
        _mixer.Dispose();
        _engine.Dispose();
        // Last: a plug-in's own teardown can hang (a JUCE plug-in disconnecting every UI Automation provider of the process
        // deadlocks with Avalonia's), so it runs after everything is saved and put back, and the process ends regardless.
        using (ExitGuard.Start(Timing.PluginUnloadLimit)) _plugins.Dispose();
    }
}
