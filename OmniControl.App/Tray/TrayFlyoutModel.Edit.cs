using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Picker;
using OmniControl.App.Services;
using OmniControl.App.States;

namespace OmniControl.App.Tray;

// Edit mode (the foot's Edit): the stored elements as two lists with a grip and a remove cross, a tile's icon and remove on its menu,
// "+ slider" and "+ tile" through the Choose-the-action window over the live master lists. Every change is saved at once; the first
// change copies the defaults into tray.json.
public sealed partial class TrayFlyoutModel
{
    private const string IconTitle = "Icon", OwnIcon = "Its own", RemoveWords = "Remove";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsShowing)), NotifyPropertyChangedFor(nameof(EditText))] private bool _isEditing;

    /// <summary>Not editing: the sliders and tiles are in use.</summary>
    public bool IsShowing => !IsEditing;

    public ObservableCollection<TrayEditRow> EditSliders { get; } = new();
    public ObservableCollection<TrayEditRow> EditTiles { get; } = new();

    partial void OnIsEditingChanged(bool value) => Rebuild();

    [RelayCommand]
    private void ToggleEdit() => IsEditing = !IsEditing;

    private List<TrayElement> SliderList => _settings.Sliders ??= TrayDefaults.Sliders.ToList();
    private List<TrayElement> TileList => _settings.Tiles ??= TrayDefaults.Tiles.ToList();

    private void RebuildEditRows(IReadOnlyList<StateEntry> states)
    {
        EditSliders.Clear();
        foreach (var element in SliderList)
        {
            var targets = TrayTargets.Sliders(new[] { element }, states);
            EditSliders.Add(new TrayEditRow(element, targets, IconOfElement(element, targets), () => Edit(() => SliderList.Remove(element))));
        }
        EditTiles.Clear();
        foreach (var element in TileList)
        {
            var targets = TrayTargets.Toggles(new[] { element }, states, _hotkeys.Actions, _hotkeys.Find);
            EditTiles.Add(new TrayEditRow(element, targets, IconOfElement(element, targets), () => Edit(() => TileList.Remove(element))));
        }
    }

    private string? IconOfElement(TrayElement element, IReadOnlyList<TrayTarget> targets) =>
        targets.Count > 0 ? IconOf(targets[0]) : element.Icon is { } section ? _hotkeys.SectionOf(section)?.Icon : null;

    /// <summary>A drag in the sliders' edit list: the rows at <paramref name="rows"/> go in front of the <paramref name="insertAt"/>-th of the others.</summary>
    public void MoveSliders(IReadOnlyList<int> rows, int insertAt) => Edit(() => _settings.Sliders = Reorder.Moved(SliderList, rows, insertAt));

    /// <summary>The same in the tiles' edit list.</summary>
    public void MoveTiles(IReadOnlyList<int> rows, int insertAt) => Edit(() => _settings.Tiles = Reorder.Moved(TileList, rows, insertAt));

    /// <summary>"+ slider": the picker's title and list (every level state, live).</summary>
    public (string Title, PickerModel Model) SliderPicker() => TrayPicks.ForSlider(_states.States, _hotkeys.SectionOf);

    /// <summary>"+ tile" and the "+ Add" tile: every master action and every on/off state, live.</summary>
    public (string Title, PickerModel Model) TilePicker() => TrayPicks.ForTile(_states.States, _hotkeys.Actions, _hotkeys.SectionOf);

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

    /// <summary>A tile's menu in edit mode: its icon (its own section's, or any section's) and Remove.</summary>
    public IReadOnlyList<MenuRowItem> TileMenu(TrayEditRow row)
    {
        var element = row.Element;
        var rows = new List<MenuRowItem>
        {
            MenuRowItem.Header(IconTitle),
            new(OwnIcon, string.Empty, element.Icon is null, string.Empty, new RelayCommand(() => Edit(() => element.Icon = null))),
        };
        rows.AddRange(_hotkeys.Sections.Where(s => s.Icon is not null).Select(section =>
            new MenuRowItem(section.Name, string.Empty, element.Icon == section.Name, string.Empty, new RelayCommand(() => Edit(() => element.Icon = section.Name)))));
        rows.Add(MenuRowItem.Header(string.Empty, ruleAbove: true));
        rows.Add(new MenuRowItem(RemoveWords, string.Empty, false, string.Empty, row.RemoveCommand));
        return rows;
    }

    /// <summary>One edit: made, saved at once, and shown.</summary>
    private void Edit(Action change)
    {
        change();
        _settings.Save();
        Rebuild();
    }
}
