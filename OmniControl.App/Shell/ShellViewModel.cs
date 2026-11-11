using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Hotkeys;
using OmniControl.App.Pages;
using OmniControl.App.Pages.AppSettingsPage;
using OmniControl.App.Pages.Displays;
using OmniControl.App.Osd;
using OmniControl.App.Pages.Osd;
using OmniControl.App.Pages.Shortcuts;
using OmniControl.App.Pages.Volume;
using OmniControl.App.Audio;
using OmniControl.App.Fx;
using OmniControl.Core;

namespace OmniControl.App.Shell;

public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly MonitorEngine _engine = new();
    private readonly HotkeyRegistry _hotkeys = new();
    private readonly OsdService _osd = new();
    private readonly MixerService _mixer = new();
    private readonly FxWindowService _fxWindows;
    private readonly IReadOnlyList<PageViewModel> _allPages;
    private bool _syncing;

    /// <summary>Feature pages in the sidebar (after search filtering).</summary>
    public ObservableCollection<PageViewModel> Pages { get; } = new();

    /// <summary>Pages pinned to the bottom of the sidebar (app-level, not features).</summary>
    public ObservableCollection<PageViewModel> FooterPages { get; } = new();
    private readonly IReadOnlyList<PageViewModel> _allFooter;

    public string Version { get; } = "OmniControl 0.1";

    /// <summary>The page shown on the right. Set via the two sidebar lists below.</summary>
    [ObservableProperty] private PageViewModel? _currentPage;
    [ObservableProperty] private PageViewModel? _selectedMainPage;
    [ObservableProperty] private PageViewModel? _selectedFooterPage;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    public ShellViewModel()
    {
        _hotkeys.Report = Report;
        _fxWindows = new FxWindowService(_mixer, Report);

        // Register new feature pages here, in sidebar order.
        _allPages = new PageViewModel[]
        {
            new DisplaysPageViewModel(_engine, _hotkeys, _osd, Report),
            new VolumePageViewModel(_mixer, _hotkeys, _osd, Report, _fxWindows.Open),
            new OsdPageViewModel(_osd, _hotkeys),
            new ShortcutsPageViewModel(_hotkeys),
        };
        _allFooter = new PageViewModel[] { new AppSettingsPageViewModel(_hotkeys) };

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
        CurrentPage = page;
        page.OnShown();
    }

    private bool Matches(PageViewModel p, string q) =>
        (SelectedCategory == AllCategories || p.Title == SelectedCategory)
        && (q.Length == 0
            || p.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || p.Keywords.Contains(q, StringComparison.OrdinalIgnoreCase));

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

    public void Dispose()
    {
        _hotkeys.Dispose();
        _osd.Dispose();
        _fxWindows.CloseAll();
        _mixer.Dispose();
        _engine.Dispose();
    }
}
