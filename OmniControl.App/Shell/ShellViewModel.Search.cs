namespace OmniControl.App.Shell;

// The main window's "Search settings...": the sidebar keeps the pages that match, and the results list shows the matching settings
// as live controls. A search is made ready once when it starts (SearchIndex: every page's and every setting's words, read then, so
// what was added since the last search is in it; the settings are the pages' own cards, so a search makes every page not made yet),
// matched off the UI thread (SearchRunner), and its results shown a screenful first (ProgressiveFill), all through ListDiff so no list
// is ever cleared and refilled.
public sealed partial class ShellViewModel
{
    private SearchRunner<List<SearchEntry>>? _search;
    private ProgressiveFill<SearchEntry>? _resultFill;
    private SearchIndex<PageSlot>? _pageIndex;
    private SearchIndex<SearchEntry>? _entryIndex;

    /// Editable settings matching the search text.
    public ObservableCollection<SearchEntry> SearchResults { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShownPage))] private bool _isSearching;

    private SearchRunner<List<SearchEntry>> Search => _search ??= new SearchRunner<List<SearchEntry>>(ShowResults, "settings");

    private ProgressiveFill<SearchEntry> ResultFill => _resultFill ??= new ProgressiveFill<SearchEntry>(SearchResults, e => e);

    // A page also matches through the actions it registered (their category is the page's title), so a new action makes its page
    // findable by its own words without anyone adding them to the page's keywords.
    private SearchIndex<PageSlot> PageIndex => _pageIndex ??= new SearchIndex<PageSlot>(AllSlots,
        p => new[] { p.Title, p.Info.Keywords }.Concat(_hotkeys.Actions.Where(a => a.Category == p.Title).SelectMany(a => new[] { a.Title, a.Heading, a.Keywords })));

    private SearchIndex<SearchEntry> EntryIndex => _entryIndex ??= new SearchIndex<SearchEntry>(
        AllSlots.Select(p => p.Page).OfType<PageViewModel>().SelectMany(p => p.SearchEntries).Concat(_actionsWindow.SearchEntries), e => e.SearchFields);

    private bool InCategory(string category) => SelectedCategory == AllCategories || category == SelectedCategory;

    private void ApplyFilter()
    {
        var q = SearchText.Trim();
        if (q.Length == 0) (_pageIndex, _entryIndex) = (null, null); // the next search reads the lists again

        var matching = (q.Length == 0 ? AllSlots : PageIndex.Match(q)).Where(p => InCategory(p.Title)).ToHashSet();
        ListDiff.Apply(Pages, _allPages.Where(matching.Contains).ToList(), p => p);
        ListDiff.Apply(NavItems, NavGrouped(Pages), item => item);
        ListDiff.Apply(FooterPages, _allFooter.Where(matching.Contains).ToList(), p => p);

        // Keep the open page selected if it still matches, otherwise jump to the first match.
        var target = _openSlot is not null && (Pages.Contains(_openSlot) || FooterPages.Contains(_openSlot))
            ? _openSlot
            : Pages.FirstOrDefault() ?? FooterPages.FirstOrDefault();

        _syncing = true;
        SelectedMainPage = target is not null && Pages.Contains(target) ? target : null;
        SelectedFooterPage = target is not null && FooterPages.Contains(target) ? target : null;
        _syncing = false;
        // Before the window first shows (a tray start) no page is made: the first showing opens the selected one.
        if (target is not null && (_windowShown || _openSlot is not null)) Show(target);

        IsSearching = q.Length > 0;
        if (!IsSearching)
        {
            Search.Cancel();
            ResultFill.Show(Array.Empty<SearchEntry>());
            return;
        }
        var index = EntryIndex;
        Search.Request(token => index.Match(q, token));
    }

    /// The pages under their sidebar group headings.
    private static List<object> NavGrouped(IEnumerable<PageSlot> pages)
    {
        var items = new List<object>();
        string? group = null;
        foreach (var p in pages)
        {
            if (p.Info.NavGroup is { } g && g != group) items.Add(new NavHeading(g));
            group = p.Info.NavGroup;
            items.Add(p);
        }
        return items;
    }

    // Live results: the actual controls, so they can be edited right in the list.
    private void ShowResults(List<SearchEntry> matches) => ResultFill.Show(matches.Where(e => InCategory(e.Category)).ToList());
}
