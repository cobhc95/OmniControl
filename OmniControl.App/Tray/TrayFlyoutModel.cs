using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Hotkeys;
using OmniControl.App.States;

namespace OmniControl.App.Tray;

/// <summary>
/// What the tray flyout and the tray menu show (docs/mockups/tray-flyout.html), read from the live master lists: sliders over level states,
/// tiles that run actions or set on/off states and light from them, and the menu's master actions. Nothing is listed by hand: the stored
/// elements (<see cref="TraySettings"/>, else <see cref="TrayDefaults"/>) are ids, and an id that is not registered is not shown. The
/// flyout's lists are made again whenever it opens or a master list changes while it is open; the menu follows the lists at all times.
/// </summary>
public sealed partial class TrayFlyoutModel : ObservableObject, IDisposable
{
    private readonly HotkeyRegistry _hotkeys;
    private readonly StateRegistry _states;
    private readonly Action _openApp;
    private readonly TraySettings _settings = TraySettings.Load();
    private bool _shown;

    /// <param name="openApp">Shows the main window (the foot's "Open OmniControl").</param>
    public TrayFlyoutModel(HotkeyRegistry hotkeys, StateRegistry states, Action openApp)
    {
        (_hotkeys, _states, _openApp) = (hotkeys, states, openApp);
        _hotkeys.Actions.CollectionChanged += OnActionsChanged;
        _states.ListChanged += OnListChanged;
        _states.ValueChanged += OnValueChanged;
        Menu = MenuTargets();
    }

    public ObservableCollection<TraySliderItem> Sliders { get; } = new();
    /// <summary>The tiles, then the dashed "+ Add" tile (<see cref="TrayAddTile"/>).</summary>
    public ObservableCollection<object> Tiles { get; } = new();

    /// <summary>The right-click menu: groups of master actions, each with the state that ticks it (if any).</summary>
    public IReadOnlyList<IReadOnlyList<TrayTarget>> Menu { get; private set; }

    /// <summary>The menu's items or ticks changed: the tray icon builds its menu again.</summary>
    public event Action? MenuChanged;

    /// <summary>The flyout is about to show: its lists are read now and follow the master lists until <see cref="Hidden"/>.</summary>
    public void Showing()
    {
        _shown = true;
        Rebuild();
    }

    /// <summary>The flyout was hidden: it stops following and leaves edit mode.</summary>
    public void Hidden()
    {
        _shown = false;
        IsEditing = false;
    }

    /// <summary>Runs a master action by id, through the one run path (so the gate of block rules applies).</summary>
    private void RunAction(string id) => _hotkeys.Run(id);

    /// <summary>Whether a menu item's state is on now (its tick).</summary>
    public bool IsOn(TrayTarget target) => target.State is { } state && _states.Now(state.Id)?.Number is { } number && number != 0;

    [RelayCommand]
    private void OpenApp() => _openApp();

    /// <summary>The foot's edit link: its words follow the mode.</summary>
    public string EditText => IsEditing ? "Done" : "Edit";

    private string? IconOf(TrayTarget target) => _hotkeys.SectionOf(target.Section)?.Icon;

    private void Rebuild()
    {
        if (!_shown) return;
        var states = _states.States;
        Sliders.Clear();
        foreach (var target in TrayTargets.Sliders(_settings.SlidersInUse, states)) Sliders.Add(new TraySliderItem(target, IconOf(target), _states.Now));
        Tiles.Clear();
        foreach (var target in TrayTargets.Toggles(_settings.TilesInUse, states, _hotkeys.Actions, _hotkeys.Find))
            Tiles.Add(new TrayTileItem(target, IconOf(target), _states.Now, RunAction));
        Tiles.Add(TrayAddTile.Instance);
        if (IsEditing) RebuildEditRows(states);
    }

    private IReadOnlyList<IReadOnlyList<TrayTarget>> MenuTargets()
    {
        var states = _states.States;
        return TrayDefaults.Menu.Select(group => TrayTargets.Toggles(group, states, _hotkeys.Actions, _hotkeys.Find)).Where(g => g.Count > 0).ToList();
    }

    private void OnListsChanged()
    {
        Menu = MenuTargets();
        MenuChanged?.Invoke();
        Rebuild();
    }

    private void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnListsChanged();

    private void OnListChanged() => OnListsChanged();

    private void OnValueChanged(StateChange change)
    {
        var id = change.Entry.Id;
        if (Menu.Any(g => g.Any(t => t.State?.Id == id))) MenuChanged?.Invoke();
        if (!_shown) return;
        foreach (var slider in Sliders.Where(s => s.Shows(id))) slider.Read(_states.Now);
        foreach (var tile in Tiles.OfType<TrayTileItem>().Where(t => t.Shows(id))) tile.Read(_states.Now);
    }

    public void Dispose()
    {
        _hotkeys.Actions.CollectionChanged -= OnActionsChanged;
        _states.ListChanged -= OnListChanged;
        _states.ValueChanged -= OnValueChanged;
    }
}
