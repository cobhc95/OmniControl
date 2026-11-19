using OmniControl.App.Features.Icons;
using OmniControl.App.Features.Hotkeys.Picker;

namespace OmniControl.App.Features.Tray;

// Editing the panel: the foot's Edit shows a remove cross on every slider and tile; the foot's "+ Tile" and "+ Slider" go through the
// Choose-the-action window over the live master lists. Every slider and tile (edit mode or not) can be dragged to another place unless the
// layout is locked, and has a right-click menu: Remove from panel, Change action…, Change icon…, Lock layout, Show in OmniControl. Every
// change is saved at once; the first change copies the defaults into tray.json. An item of a pattern ("every smart device") is spelt out
// before it is edited alone (TrayOrder).
public sealed partial class TrayFlyoutModel
{
    private const string RemoveWords = "Remove from panel", ChangeActionWords = "Change action…", ChangeIconWords = "Change icon…",
        LockWords = "Lock layout", ShowWords = "Show in OmniControl";

    [ObservableProperty] private bool _isEditing;

    [RelayCommand]
    private void ToggleEdit() => IsEditing = !IsEditing;

    /// "Lock layout": nothing can be dragged while it is on (panel-wide, saved).
    public bool IsLocked => _settings.LockLayout;

    private List<TrayElement> SliderList => _settings.Sliders ??= TrayDefaults.Sliders.ToList();
    private List<TrayElement> TileList => _settings.Tiles ??= TrayDefaults.Tiles.ToList();

    /// A cross, or the menu's Remove: the slider or tile goes out of the panel.
    public void Remove(ITrayPanelItem item) => EditList(item.IsSlider, (list, shown) => TrayOrder.Remove(list, shown, IndexOf(item)));

    /// A drag ended: the slider or tile at <paramref name="from"/> now stands at <paramref name="to"/>.
    public void Move(bool slider, int from, int to) => EditList(slider, (list, shown) => TrayOrder.Move(list, shown, from, to));

    /// "Add a slider…": the picker's title and list (every level state, live).
    public (string Title, PickerModel Model) SliderPicker() =>
        TrayPicks.ForSlider(_states.States, _hotkeys.SectionOf, new TrayPanelMembership(() => SliderList, e => Edit(() => SliderList.Remove(e))));

    /// "Add a tile…": every master action and every on/off state, live.
    public (string Title, PickerModel Model) TilePicker() =>
        TrayPicks.ForTile(_states.States, _hotkeys.Actions, _hotkeys.SectionOf, new TrayPanelMembership(() => TileList, e => Edit(() => TileList.Remove(e))));

    /// "Change action…": the same picker as "Add a slider…" or "Add a tile…", at the item's own entry.
    public (string Title, PickerModel Model) ChangePicker(ITrayPanelItem item)
    {
        var (title, model) = item.IsSlider ? SliderPicker() : TilePicker();
        model.ShowEntry(TrayPicks.SpecOf(item.Target));
        return (title, model);
    }

    /// The picker's answer for "Add a slider…": a level state is added at the end.
    public void AddSlider(PickResult pick)
    {
        if (TrayPicks.ElementOf(pick) is { State: not null } element) Edit(() => SliderList.Add(element));
    }

    /// The picker's answer for "Add a tile…": an action or an on/off state is added at the end.
    public void AddTile(PickResult pick)
    {
        if (TrayPicks.ElementOf(pick) is { } element) Edit(() => TileList.Add(element));
    }

    /// The picker's answer for "Change action…": the new entry takes the item's place (and keeps an icon the user gave it).
    public void Replace(ITrayPanelItem item, PickResult pick)
    {
        if (TrayPicks.ElementOf(pick) is not { } element || (item.IsSlider && element.State is null)) return;
        EditList(item.IsSlider, (list, shown) => TrayOrder.Replace(list, shown, IndexOf(item), element));
    }

    /// What "Change icon…" asks the icon picker: the item's name, its chosen icon and its entry's suggestions.
    public IconRequest IconRequestFor(ITrayPanelItem item)
    {
        var target = item.Target;
        var suggested = target.Action?.SuggestedIcons ?? target.State?.SuggestedIcons;
        var own = target.Action?.ShownIcon ?? target.State?.ShownIcon;
        var chosen = IconResources.Find(target.Element.Icon) is not null ? target.Element.Icon : null; // a section name saved before the library is no choice of an icon
        return new IconRequest(target.Name, chosen ?? own, IconLibrary.Suggestions(suggested, own, target.Action?.Title ?? target.State?.Name));
    }

    /// "Change icon…": the shared icon picker over the window holding <paramref name="from"/>; null when cancelled.
    public Task<IconPick?> ChooseIconAsync(Avalonia.Visual from, ITrayPanelItem item) => _icons.ChooseAsync(from, IconRequestFor(item));

    /// The icon picker's answer: the item shows that icon (null: its own again).
    public void SetIcon(ITrayPanelItem item, IconPick pick) => EditList(item.IsSlider, (list, shown) => TrayOrder.WithIcon(list, shown, IndexOf(item), pick.Value));

    /// The right-click menu of a slider or tile; the window shows the pickers (they need it as their owner).
    public IReadOnlyList<MenuRowItem> ItemMenu(ITrayPanelItem item, Action<ITrayPanelItem> changeAction, Action<ITrayPanelItem> changeIcon)
    {
        var rows = new List<MenuRowItem>();
        if (item is TrayTileItem { Failure: { } failure }) rows.Add(MenuRowItem.Header(failure)); // why the tile's mark is red
        rows.Add(Row(RemoveWords, false, () => Remove(item)));
        rows.Add(Row(ChangeActionWords, false, () => changeAction(item)));
        rows.Add(Row(ChangeIconWords, false, () => changeIcon(item)));
        rows.Add(Row(LockWords, IsLocked, ToggleLock));
        rows.Add(Row(ShowWords, false, () => _showInApp(item.Target)));
        return rows;
    }

    private static MenuRowItem Row(string text, bool ticked, Action run) => new(text, string.Empty, ticked, string.Empty, new RelayCommand(run));

    private void ToggleLock()
    {
        _settings.LockLayout = !_settings.LockLayout;
        _settings.Save();
        OnPropertyChanged(nameof(IsLocked));
    }

    /// The item's place among the shown sliders or tiles (what <see cref="TrayOrder"/> counts in).
    private int IndexOf(ITrayPanelItem item) =>
        item.IsSlider ? Sliders.IndexOf((TraySliderItem)item) : Tiles.IndexOf((TrayTileItem)item);

    /// An edit of the slider or tile list, given the stored list and what it shows now.
    private void EditList(bool slider, Func<IReadOnlyList<TrayElement>, IReadOnlyList<TrayTarget>, List<TrayElement>> change) => Edit(() =>
    {
        var list = slider ? SliderList : TileList;
        var shown = slider ? Sliders.Select(s => s.Target).ToList() : Tiles.Select(t => t.Target).ToList();
        var changed = change(list, shown);
        list.Clear();
        list.AddRange(changed);
    });

    /// A row's chip was set (a display, a pinned speaker): kept with its element, saved at once. The row already shows it, so nothing is made again.
    private void SavePick(TrayElement shown, string? pick)
    {
        var stored = SliderList.FirstOrDefault(e => e.State == shown.State && e.Choice == shown.Choice && e.Together == shown.Together);
        if (stored is null || stored.Pick == pick) return;
        stored.Pick = pick;
        _settings.Save();
    }

    /// One edit: made, saved at once, and shown.
    private void Edit(Action change)
    {
        change();
        _settings.Save();
        _stale = true;
        Rebuild();
    }
}
