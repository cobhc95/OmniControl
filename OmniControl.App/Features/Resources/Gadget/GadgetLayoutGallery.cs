using System.Globalization;

namespace OmniControl.App.Features.Resources.Gadget;

/// One layout tile of the Ready-made layouts gallery: its name over its description, and a preview (the cells it would show now); the user's own or built
/// in; selected while it is the live look. A click applies it.
public sealed partial class LayoutCard : ObservableObject
{
    public LayoutCard(string id, bool isOwn, IRelayCommand apply) => (Id, IsOwn, Apply) = (id, isOwn, apply);

    public string Id { get; }
    public bool IsOwn { get; }
    public IRelayCommand Apply { get; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private IReadOnlyList<GadgetCell>? _cells;
    [ObservableProperty] private GadgetLayout? _layout;
    [ObservableProperty] private IBrush? _background;
}

/// One section of the unfolded gallery: a category's (or the user's own) tiles under its heading; shown while the filter allows it.
public sealed partial class LayoutSection : ObservableObject
{
    public LayoutSection(string name, IRelayCommand? add) => (Name, Add) = (name, add);

    public string Name { get; }

    /// The heading: the name in small capitals.
    public string Heading => Name.ToUpper(CultureInfo.CurrentCulture);

    /// "+ Save current as a layout" of an empty Yours section; null for a category.
    public IRelayCommand? Add { get; }

    public ObservableCollection<LayoutCard> Cards { get; } = new();

    [ObservableProperty] private bool _isShown = true;
    [ObservableProperty] private bool _isEmpty;
}

/// The Ready-made layouts group's gallery: folded, the favourite tiles and the dashed "More layouts" tile; unfolded in place, the filter chips (All, Yours and
/// the categories), a section per category with the user's own first, and the foot ("+ Save current as a layout", "Manage layouts…", "Fewer
/// layouts"). Names, descriptions and categories come from <see cref="GadgetLayouts"/>; previews are drawn only for the tiles on screen.
public sealed partial class GadgetLayoutGallery : ObservableObject, IDisposable
{
    private const string All = "All", Yours = "Yours";

    private readonly GadgetLayouts _layouts;
    private readonly Action _redraw;

    /// <param name="redraw">Asks the tab to draw the previews again (a tile came on screen).</param>
    public GadgetLayoutGallery(GadgetLayouts layouts, Action manage, Action redraw)
    {
        (_layouts, _redraw) = (layouts, redraw);
        SaveText = layouts.Words.AddText;
        Save = new RelayCommand(() => layouts.Add());
        Manage = new RelayCommand(manage);
        Sections.Add(new LayoutSection(Yours, Save));
        foreach (var category in GadgetLayouts.Categories) Sections.Add(new LayoutSection(category, null));
        foreach (var name in Sections.Select(s => s.Name).Prepend(All))
            Filters.Add(new ChipItem(name, new RelayCommand(() => SetFilter(name)), null) { Key = name, IsSelected = name == All });
        layouts.Changed += Show;
        Show();
    }

    /// The folded gallery's tiles.
    public ObservableCollection<LayoutCard> Favourites { get; } = new();

    public ObservableCollection<LayoutSection> Sections { get; } = new();

    /// The unfolded gallery's filter chips: All, Yours and the categories; one is selected.
    public ObservableCollection<ChipItem> Filters { get; } = new();

    public string SaveText { get; }
    public IRelayCommand Save { get; }
    public IRelayCommand Manage { get; }

    [ObservableProperty] private bool _isExpanded;

    /// The dashed tile's words: "More layouts · 12 more · your own".
    [ObservableProperty] private string _moreText = string.Empty;

    /// The tiles on screen now (the only ones whose previews are drawn).
    private IEnumerable<LayoutCard> OnScreen => IsExpanded ? Sections.Where(s => s.IsShown).SelectMany(s => s.Cards) : Favourites;

    [RelayCommand]
    private void ShowMore() => IsExpanded = true;

    [RelayCommand]
    private void ShowFewer() => IsExpanded = false;

    partial void OnIsExpandedChanged(bool value) => _redraw();

    private void SetFilter(string name)
    {
        foreach (var chip in Filters) chip.IsSelected = chip.Key == name;
        foreach (var section in Sections) section.IsShown = name == All || name == section.Name;
        _redraw();
    }

    /// Lists the layouts again after a change of the presets or of the live look (names, the selected tile, the user's own).
    private void Show()
    {
        var entries = _layouts.Entries;
        var known = Sections.SelectMany(s => s.Cards).ToDictionary(c => c.Id);
        var cards = entries.Select(e => known.GetValueOrDefault(e.Id) ?? new LayoutCard(e.Id, !e.BuiltIn, new RelayCommand(() => _layouts.Apply(e.Id)))).ToList();
        var about = cards.ToDictionary(c => c.Id, c => _layouts.About(c.Id));
        foreach (var (card, entry) in cards.Zip(entries))
            (card.Name, card.Description, card.IsSelected) = (entry.Name, about[card.Id].Description, entry.InUse);
        ListDiff.Apply(Favourites, cards.Where(c => about[c.Id].Favourite).ToList(), c => c.Id);
        foreach (var section in Sections)
        {
            ListDiff.Apply(section.Cards, cards.Where(c => (about[c.Id].Category ?? Yours) == section.Name).ToList(), c => c.Id);
            section.IsEmpty = section.Cards.Count == 0;
        }
        var own = cards.Count(c => c.IsOwn);
        MoreText = $"More layouts · {cards.Count - own - Favourites.Count} more · " + (own > 0 ? $"{own} your own" : "your own");
        _redraw();
    }

    /// Draws the previews of the tiles on screen with <paramref name="cellsOf"/> (sample values for what is unknown) on the taskbar's colour, in the
    /// gadget's <paramref name="font"/> (one for every layout).
    public void Draw(Func<GadgetSettings, IReadOnlyList<GadgetCell>> cellsOf, IBrush? background, string font)
    {
        foreach (var card in OnScreen)
            if (_layouts.LookOf(card.Id) is { } look) (card.Cells, card.Layout, card.Background) = (cellsOf(look), GadgetLayout.Of(look) with { Font = font }, background);
    }

    public void Dispose() => _layouts.Changed -= Show;
}
