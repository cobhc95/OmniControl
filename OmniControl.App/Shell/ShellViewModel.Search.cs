using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.App.Shell;

// The main window's "Search settings...": the sidebar keeps the pages that match, and the results list shows the matching settings
// as live controls. A search is made ready once when it starts (SearchIndex: every page's and every result's words, read then, so
// what was added since the last search is in it); the results are the pages' own cards, so a page whose words match is made at the
// typing pause and only then. Matching runs off the UI thread (SearchRunner), and results show a screenful first (ProgressiveFill),
// all through ListDiff so no list is ever cleared and refilled.
public sealed partial class ShellViewModel
{
    private SearchRunner<SearchOutcome>? _search;
    private ProgressiveFill<SearchEntry>? _resultFill;
    private SearchIndex<PageSlot>? _pageIndex;
    private SearchIndex<(PageSlot Slot, string Words)>? _wordIndex;
    private SearchIndex<SearchEntry>? _entryIndex;
    private int _indexedPages;
    private (string Query, string Category) _wordHitsFor;
    private List<PageSlot> _wordHits = new();
    private readonly DispatcherTimer _searchShow = new() { Interval = Timing.SearchDebounce };
    private readonly DispatcherTimer _searchMake = new() { Interval = Timing.SearchDebounce };
    private PageSlot? _searchTarget;

    private void OnSearchShowDue(object? sender, EventArgs e) => Guard.Run("search page", ShowSearchTarget);

    private void ShowSearchTarget()
    {
        _searchShow.Stop();
        if (_searchTarget is { } target) Show(target);
        _searchTarget = null;
    }

    /// One search's answer: the exact matches, or, when there are none, the typo-tolerant ones (<see cref="FuzzyMatch"/>).
    private sealed record SearchOutcome(List<SearchEntry> Entries, bool Similar);

    /// Editable settings matching the search text.
    public ObservableCollection<SearchEntry> SearchResults { get; } = new();

    /// The query the shown results answer, which the results' texts highlight (what is typed now may be newer: results wait for the typing pause).
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoExactText))] private string _resultQuery = string.Empty;

    /// True while the results shown are the similar ones: the "No exact matches" line and the "Similar matches" heading show above them.
    [ObservableProperty] private bool _showsSimilar;

    public string NoExactText => $"No exact matches for \"{ResultQuery}\".";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShownPage))] private bool _isSearching;

    private SearchRunner<SearchOutcome> Search => _search ??= new SearchRunner<SearchOutcome>(ShowResults, "settings");

    private ProgressiveFill<SearchEntry> ResultFill => _resultFill ??= new ProgressiveFill<SearchEntry>(SearchResults, e => e);

    // A page also matches through the actions it registered (their category is the page's title), so a new action makes its page
    // findable by its own words without anyone adding them to the page's keywords.
    private SearchIndex<PageSlot> PageIndex => _pageIndex ??= MakePageIndex();

    private SearchIndex<PageSlot> MakePageIndex()
    {
        var actions = _hotkeys.Actions.ToLookup(a => a.Category);
        return new SearchIndex<PageSlot>(AllSlots,
            p => new[] { p.Title, p.Info.Keywords }.Concat(actions[p.Title].SelectMany(a => new[] { a.Title, a.Heading, a.Keywords, a.OptionWords })));
    }

    // Each page's result words, read without making it (FeaturePage.Words): only a page with a match is made. Joined as a result's heading
    // is, so a query matches the words exactly when it can match the result.
    private SearchIndex<(PageSlot Slot, string Words)> WordIndex => _wordIndex ??= new SearchIndex<(PageSlot Slot, string Words)>(
        AllSlots.SelectMany(s => s.Words().Concat(s.Restorable is null ? Array.Empty<string>() : new[] { $"{RestoreWords.ButtonText} {RestoreWords.Tip}" }).Select(w => (s, w))), e => new[] { Heading.Join(e.Slot.Title, e.Words) });

    // A query with a typo matches no words exactly, so the pages with near words are made instead: their cards are the similar results.
    // The word lists of the pages in the category are searched once per query, however often the filter asks (a key press asks twice).
    private List<PageSlot> UnmadeMatches(string q)
    {
        var key = (q, SelectedCategory);
        if (_wordHitsFor != key)
        {
            bool Shown((PageSlot Slot, string Words) e) => InCategory(e.Slot.Title);
            var hits = WordIndex.Match(q, accept: Shown);
            if (hits.Count == 0) hits = WordIndex.Similar(q, accept: Shown);
            (_wordHitsFor, _wordHits) = (key, hits.Select(e => e.Slot).Distinct().ToList());
        }
        return _wordHits.Where(s => s.IfMade is null).ToList();
    }

    // The made pages' results; made again once a search has made another page.
    private SearchIndex<SearchEntry> EntryIndex()
    {
        var made = AllSlots.Select(p => p.IfMade).OfType<PageViewModel>().ToList();
        if (_entryIndex is null || made.Count != _indexedPages)
            (_entryIndex, _indexedPages) = (new SearchIndex<SearchEntry>(made.SelectMany(p => p.SearchEntries).Concat(_actionsWindow.SearchEntries), e => e.SearchFields), made.Count);
        return _entryIndex;
    }

    private void OnSearchMakeDue(object? sender, EventArgs e) => Guard.Run("search pages", MakeSearchPages);

    // At the typing pause: the pages the query matches are made, then searched.
    private void MakeSearchPages()
    {
        _searchMake.Stop();
        var q = SearchText.Trim();
        if (q.Length == 0) return;
        foreach (var slot in UnmadeMatches(q)) _ = slot.Page;
        RequestResults(q);
    }

    private void RequestResults(string q)
    {
        var index = EntryIndex();
        var category = SelectedCategory; // read here: the search runs off the UI thread, and the cap of similar results comes after the filter
        bool Shown(SearchEntry e) => category == AllCategories || e.Category == category;
        Search.Request(token =>
        {
            var exact = index.Match(q, token, Shown);
            return exact.Count > 0 ? new SearchOutcome(exact, false) : new SearchOutcome(index.Similar(q, token, Shown), true);
        });
    }

    private bool InCategory(string category) => SelectedCategory == AllCategories || category == SelectedCategory;

    private void ApplyFilter()
    {
        var q = SearchText.Trim();
        if (q.Length == 0) (_pageIndex, _wordIndex, _entryIndex, _wordHitsFor, _wordHits) = (null, null, null, default, new()); // the next search reads the lists again

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
        _searchShow.Stop();
        _searchTarget = target is not null && (_windowShown || _openSlot is not null) ? target : null;
        // While typing the results cover the page, so the page behind them is switched only at the typing pause.
        if (q.Length == 0) ShowSearchTarget();
        else if (_searchTarget is not null) _searchShow.Start();

        IsSearching = q.Length > 0;
        _searchMake.Stop();
        if (!IsSearching)
        {
            Search.Cancel();
            ShowsSimilar = false;
            ResultFill.Show(Array.Empty<SearchEntry>());
            return;
        }
        if (UnmadeMatches(q).Count == 0) RequestResults(q);
        else
        {
            // A page is made only at the typing pause, so the first letters (which match nearly every page) make none.
            Search.Cancel();
            _searchMake.Start();
        }
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
    private void ShowResults(SearchOutcome outcome)
    {
        var shown = outcome.Entries;
        ResultQuery = SearchText.Trim();
        ShowsSimilar = outcome.Similar && shown.Count > 0;
        ResultFill.Show(shown);
    }
}
