using OmniControl.App.Features.Hotkeys.Actions;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// Favourites in a list of actions (<see cref="FavouriteActions"/>, given with the keys part): a star on each entry's row and on the settings
/// title, a Favourites section first in pane 1 while any entry is starred, and a section's own starred entries again under a Favourites heading
/// at the top of its list. A search keeps its order (the stars show on its rows). A family is starred as one (every direction).
public sealed partial class PickerModel
{
    private List<PickChoice>? _favouriteChoices;

    /// True while the Favourites section is open (<see cref="SectionIndex"/> then keeps the section to go back to).
    private bool InFavourites { get; set; }

    /// True when the keyboard is on the star beside the settings title.
    [ObservableProperty] private bool _onStar;

    private FavouriteActions? Favourites => ListsActions ? _manage?.Favourites : null;

    /// 1 when the Favourites section leads pane 1, else 0.
    private int FavouritesLead => FavouriteChoices.Count > 0 ? 1 : 0;

    /// The starred entries in this list, in the order they were starred.
    private List<PickChoice> FavouriteChoices => _favouriteChoices ??= Favourites is { } favourites
        ? favourites.Specs.Select(spec => _groups.SelectMany(g => g.Choices).FirstOrDefault(c => c.Spec == spec && CanStar(c))).OfType<PickChoice>().ToList()
        : new();

    private bool CanStar(PickChoice choice) => Favourites is not null && choice is { Command: null, Create: null };

    private bool IsFavourite(PickChoice choice) => Favourites?.Contains(choice.Spec) == true;

    /// The highlighted entry has the star beside the settings title.
    public bool ShowsTitleStar => ActiveRow is { CanStar: true };
    public bool ActiveIsFavourite => ActiveRow is { IsFavourite: true };
    public string ActiveStarTip => PickerRow.StarTipFor(ActiveIsFavourite);

    /// A section's starred entries under the Favourites heading, then <paramref name="rows"/> (the section as it is).
    private List<PickerRow> FavouritesFirst(List<PickChoice> shown, List<PickerRow> rows)
    {
        var starred = FavouriteChoices.Where(shown.Contains).ToList();
        if (starred.Count == 0) return rows;
        var top = new List<PickerRow> { PickerRow.HeadingOf(FavouritesName) };
        top.AddRange(Rows(starred, _ => null, preselect: false, copy: true));
        return top.Concat(rows).ToList();
    }

    /// The star of the highlighted entry (the settings title's, by click, Enter or Space).
    public void ToggleActiveFavourite()
    {
        if (ActiveRow is { } row) ToggleFavourite(row);
    }

    /// Stars or un-stars a row's entry (every direction of a family); the lists and sections follow, the same line kept highlighted.
    public void ToggleFavourite(PickerRow row)
    {
        if (Favourites is not { } favourites || !row.CanStar || row.Choice is not { } choice) return;
        var keep = ActiveChoice;
        var copy = ActiveRow?.IsCopy == true;
        var keepAt = ActionIndex;
        favourites.Set((row.Members ?? new[] { choice }).Select(c => c.Spec), !row.IsFavourite);
        ReadFavouritesAgain();
        var rows = Build();
        var at = keep is null ? -1 : IndexIn(rows, keep.Spec, copy);
        var clamped = Math.Clamp(keepAt, 0, Math.Max(0, rows.Count - 1));
        ShowRows(rows, () => at >= 0 ? at : rows.Count > clamped && rows[clamped].IsHeading ? FirstEntry() : clamped, sync: at < 0);
        NotifyStarChanged();
    }

    /// The favourites were read again: pane 1 gains or loses its Favourites section (leaving it when it is gone).
    private void ReadFavouritesAgain()
    {
        var lead = _sections is null ? -1 : FavouritesLead;
        _favouriteChoices = null;
        if (FavouritesLead == 0) InFavourites = false;
        if (lead == FavouritesLead) return;
        _sections = null;
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(SectionHighlight));
    }

    partial void OnInSettingsChanged(bool value)
    {
        if (!value) OnStar = false;
    }

    private void NotifyStarChanged()
    {
        OnPropertyChanged(nameof(ShowsTitleStar));
        OnPropertyChanged(nameof(ActiveIsFavourite));
        OnPropertyChanged(nameof(ActiveStarTip));
    }
}
