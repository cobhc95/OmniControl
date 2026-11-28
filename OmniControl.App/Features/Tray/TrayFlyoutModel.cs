using System.Collections.Specialized;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Icons;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Tray;

/// What the tray flyout and the tray menu show, read from the live master lists: sliders over level states,
/// tiles that run actions or set on/off states and light from them, and the menu's master actions. Nothing is listed by hand: the stored
/// elements (<see cref="TraySettings"/>, else <see cref="TrayDefaults"/>) are ids, and an id that is not registered is not shown. The
/// flyout's lists are made again whenever it opens or a master list changes while it is open; the menu follows the lists at all times.
public sealed partial class TrayFlyoutModel : ObservableObject, IDisposable
{
    private readonly IActionCatalogue _hotkeys;
    private readonly Func<string, bool> _run;
    private readonly IStateCatalogue _states;
    private readonly Action _openApp;
    private readonly Action<TrayTarget> _showInApp;
    private readonly IconPicker _icons;
    private readonly TraySettings _settings = TraySettings.Load();
    private readonly Func<IDisposable?> _watchWhileShown;
    private IDisposable? _watch;
    private bool _shown;
    private bool _stale = true; // the lists differ from the master lists: made again when next shown

    /// <param name="icons">The shared icon picker ("Change icon…"); its changes (a user icon renamed or removed) redraw the panel.</param>
    /// <param name="openApp">Shows the main window (the foot's "Open OmniControl").</param>
    /// <param name="showInApp">"Show in OmniControl": the main window at the page that owns the item, or the Actions and Hotkeys window at its action.</param>
    /// <param name="watchWhileShown">Asks what the panel shows to stay fresh while it is shown (the mixer's close watch; null: nothing to watch); the handle is given back when it hides.</param>
    public TrayFlyoutModel(IActionCatalogue hotkeys, Func<string, bool> run, IStateCatalogue states, IconPicker icons, Action openApp, Action<TrayTarget> showInApp,
        Func<IDisposable?> watchWhileShown)
    {
        (_hotkeys, _run, _states, _icons, _openApp, _showInApp, _watchWhileShown) = (hotkeys, run, states, icons, openApp, showInApp, watchWhileShown);
        _icons.Changed += OnListsChanged;
        _icons.Changed += OnUserIconsChanged;
        _hotkeys.Actions.CollectionChanged += OnActionsChanged;
        _states.ListChanged += OnListChanged;
        _states.ValueChanged += OnValueChanged;
        _states.LivenessChanged += OnLivenessChanged;
        Menu = MenuTargets();
    }

    public ObservableCollection<TraySliderItem> Sliders { get; } = new();
    public ObservableCollection<TrayTileItem> Tiles { get; } = new();

    /// The right-click menu: groups of master actions, each with the state that ticks it (if any).
    public IReadOnlyList<IReadOnlyList<TrayTarget>> Menu { get; private set; }

    /// The menu's items or ticks changed: the tray icon builds its menu again.
    public event Action? MenuChanged;

    /// Builds the flyout's lists once for prebuilding its hidden window without starting live watches or device rechecks.
    public void Warm()
    {
        if (!_stale) return;
        _shown = true;
        try { Rebuild(); }
        finally { _shown = false; }
    }

    /// The flyout is about to show: its lists are read now and follow the master lists until <see cref="Hidden"/>.
    public void Showing()
    {
        _shown = true;
        _watch ??= _watchWhileShown();
        if (_stale) Rebuild();
        else Refresh();
        StartRechecks();
    }

    /// The flyout was hidden: it stops following and leaves edit mode.
    public void Hidden()
    {
        _shown = false;
        IsEditing = false;
        StopRechecks();
        _watch?.Dispose();
        _watch = null;
    }

    /// Takes tray.json from disk as it is now (an import wrote it): the panel's tiles and sliders and the menu are made again from it.
    public bool ReloadFromDisk()
    {
        SettingsCopy.Into(_settings, TraySettings.Load());
        OnListsChanged();
        TrayIconChanged?.Invoke();
        return true;
    }

    /// Runs a master action by id, through the one run path (so the gate of block rules applies).
    private void RunAction(string id) => _run(id);

    /// Whether a menu item's state is on now (its tick).
    public bool IsOn(TrayTarget target) => target.State is { } state && target.IsOn(_states.Now(state.Id));

    [RelayCommand]
    private void OpenApp() => _openApp();

    /// The icon the user gave it, else its entry's own, else its section's (a section name saved before the icon library is read as that section).
    private IconImage? IconOf(TrayTarget target) => IconResources.Find(target.Element.Icon) ?? IconResources.Of(target.OwnIcon, _hotkeys.SectionOf(target.Section)?.Icon);

    private void Rebuild()
    {
        if (!_shown) return;
        _stale = false;
        var states = _states.States;
        Sliders.Clear();
        foreach (var target in TrayTargets.Sliders(_settings.SlidersInUse, states)) Sliders.Add(new TraySliderItem(target, IconOf(target), _states.Now, pick => SavePick(target.Element, pick)));
        Tiles.Clear();
        foreach (var target in TrayTargets.Toggles(_settings.TilesInUse, states, _hotkeys.Actions, _hotkeys.Find))
            Tiles.Add(new TrayTileItem(target, IconOf(target), _states.Now, RunAction));
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
        _stale = true;
        Rebuild();
    }

    private void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnListsChanged();

    private void OnListChanged() => OnListsChanged();

    private void OnValueChanged(StateChange change)
    {
        var id = change.Entry.Id;
        if (Menu.Any(g => g.Any(t => t.State?.Id == id))) MenuChanged?.Invoke();
        foreach (var slider in Sliders.Where(s => s.Shows(id))) slider.Read(_states.Now);
        foreach (var tile in Tiles.Where(t => t.Shows(id))) tile.Read(_states.Now);
    }

    private void OnLivenessChanged(StateEntry entry)
    {
        foreach (var tile in Tiles.Where(t => t.Shows(entry.Id))) tile.Read(_states.Now);
    }

    /// The lists are still right: only the values are taken again, from the states' last readings (never a device read).
    private void Refresh()
    {
        foreach (var slider in Sliders) slider.Read(_states.Now);
        foreach (var tile in Tiles) tile.Read(_states.Now);
    }

    public void Dispose()
    {
        _hotkeys.Actions.CollectionChanged -= OnActionsChanged;
        _states.ListChanged -= OnListChanged;
        _states.ValueChanged -= OnValueChanged;
        _states.LivenessChanged -= OnLivenessChanged;
        _icons.Changed -= OnListsChanged;
        _icons.Changed -= OnUserIconsChanged;
        StopRechecks();
        _watch?.Dispose();
    }
}
