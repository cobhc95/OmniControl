using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Picker;
using OmniControl.App.Services;

namespace OmniControl.App.Tray;

// Edit mode (the foot's Edit): the sliders and tiles stay where they are, each with a remove cross; "+ slider" and "+ Add" go through the
// Choose-the-action window over the live master lists. A right-click menu on any slider or tile (edit mode or not) removes it, changes
// its icon and moves it. Every change is saved at once; the first change copies the defaults into tray.json.
public sealed partial class TrayFlyoutModel
{
    private const string IconTitle = "Icon", OwnIcon = "Its own", RemoveWords = "Remove from panel", ChangeIconWords = "Change icon…";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(EditText))] private bool _isEditing;

    [RelayCommand]
    private void ToggleEdit() => IsEditing = !IsEditing;

    private List<TrayElement> SliderList => _settings.Sliders ??= TrayDefaults.Sliders.ToList();
    private List<TrayElement> TileList => _settings.Tiles ??= TrayDefaults.Tiles.ToList();

    private List<TrayElement> ListOf(ITrayPanelItem item) => item.IsSlider ? SliderList : TileList;

    /// <summary>A cross, or the menu's Remove: the slider or tile goes out of the panel.</summary>
    public void Remove(ITrayPanelItem item) => Edit(() => ListOf(item).Remove(item.Element));

    /// <summary>"+ slider": the picker's title and list (every level state, live).</summary>
    public (string Title, PickerModel Model) SliderPicker() =>
        TrayPicks.ForSlider(_states.States, _hotkeys.SectionOf, new TrayPanelMembership(() => SliderList, e => Edit(() => SliderList.Remove(e))));

    /// <summary>"+ tile" and the "+ Add" tile: every master action and every on/off state, live.</summary>
    public (string Title, PickerModel Model) TilePicker() =>
        TrayPicks.ForTile(_states.States, _hotkeys.Actions, _hotkeys.SectionOf, new TrayPanelMembership(() => TileList, e => Edit(() => TileList.Remove(e))));

    /// <summary>The picker's answer for "+ slider": a level state is added at the end.</summary>
    public void AddSlider(PickResult pick)
    {
        if (TrayPicks.ElementOf(pick) is { State: not null } element) Edit(() => SliderList.Add(element));
    }

    /// <summary>The picker's answer for "+ tile": an action or an on/off state is added at the end.</summary>
    public void AddTile(PickResult pick)
    {
        if (TrayPicks.ElementOf(pick) is { } element) Edit(() => TileList.Add(element));
    }

    /// <summary>The right-click menu of a slider or tile: Remove from panel, Change icon…, and a move one place each way where there is room.</summary>
    /// <param name="showIcons">Shows the icon choices (a second menu), for "Change icon…".</param>
    public IReadOnlyList<MenuRowItem> ItemMenu(ITrayPanelItem item, Action<IReadOnlyList<MenuRowItem>> showIcons)
    {
        var list = ListOf(item);
        var at = list.IndexOf(item.Element);
        var (back, forward) = item.IsSlider ? ("Move up", "Move down") : ("Move left", "Move right");
        var rows = new List<MenuRowItem>
        {
            new(RemoveWords, string.Empty, false, string.Empty, new RelayCommand(() => Remove(item))),
            new(ChangeIconWords, string.Empty, false, string.Empty, new RelayCommand(() => showIcons(IconMenu(item.Element)))),
        };
        if (at > 0) rows.Add(new MenuRowItem(back, string.Empty, false, string.Empty, new RelayCommand(() => Move(item, -1))));
        if (at >= 0 && at < list.Count - 1) rows.Add(new MenuRowItem(forward, string.Empty, false, string.Empty, new RelayCommand(() => Move(item, 1))));
        return rows;
    }

    /// <summary>One place earlier (-1) or later (1) in its list.</summary>
    private void Move(ITrayPanelItem item, int step)
    {
        var list = ListOf(item);
        var at = list.IndexOf(item.Element);
        if (at < 0 || at + step < 0 || at + step >= list.Count) return;
        Edit(() =>
        {
            var moved = Reorder.Moved(list, new[] { at }, step > 0 ? at + 1 : at - 1);
            list.Clear();
            list.AddRange(moved);
        });
    }

    /// <summary>An element's icon choices: its own section's, or any section's.</summary>
    private IReadOnlyList<MenuRowItem> IconMenu(TrayElement element)
    {
        var rows = new List<MenuRowItem>
        {
            MenuRowItem.Header(IconTitle),
            new(OwnIcon, string.Empty, element.Icon is null, string.Empty, new RelayCommand(() => Edit(() => element.Icon = null))),
        };
        rows.AddRange(_hotkeys.Sections.Where(s => s.Icon is not null).Select(section =>
            new MenuRowItem(section.Name, string.Empty, element.Icon == section.Name, string.Empty, new RelayCommand(() => Edit(() => element.Icon = section.Name)))));
        return rows;
    }

    /// <summary>One edit: made, saved at once, and shown.</summary>
    private void Edit(Action change)
    {
        change();
        _settings.Save();
        _stale = true;
        Rebuild();
    }
}
