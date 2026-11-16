using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Services;

namespace OmniControl.App.Picker;

/// <summary>
/// The keys part of the Choose-the-action window, in both modes: the keys of an entry (null when the entry is not a master action, so it
/// can have none), the hotkey part of the settings pane for an entry, and the live master list (its saves refresh the keycaps; its hold
/// lets a pressed key reach the window).
/// </summary>
public sealed record ManageMode(Func<PickChoice, IReadOnlyList<string>?> KeysOf, Func<PickChoice, ActionHotkeys?> PaneFor, HotkeyRegistry Registry);

/// <summary>The manage mode of the picker (<see cref="ManageMode"/>): keycaps on the rows, "Only with a hotkey", search by key, the hotkey pane.</summary>
public sealed partial class PickerModel
{
    private ManageMode? _manage;
    private readonly bool _picking;

    /// <summary>True when the window picks an entry ("Use this ..." shown); false when it manages the actions and their keys.</summary>
    public bool IsPicking => _picking;

    /// <summary>True when the keys are shown (keycaps, the filter, the hotkey part): always once the window has given them.</summary>
    public bool HasKeys => _manage is not null;

    /// <summary>The window gives the keys part to a model a picker caller made (pick mode), so callers stay unaware of it.</summary>
    public void UseKeys(ManageMode keys)
    {
        if (_manage is not null) return;
        _manage = keys;
        OnPropertyChanged(nameof(HasKeys));
        OnPropertyChanged(nameof(ShowsKeyFilter));
        OnPropertyChanged(nameof(SearchHint));
        OnPropertyChanged(nameof(SearchTip));
        RefreshKeys();
        SyncSettings();
    }

    public bool IsManaging => !IsPicking;

    /// <summary>"Only with a hotkey": the lists show only the entries that have a key.</summary>
    [ObservableProperty] private bool _onlyWithKey;

    /// <summary>The hotkey part of the settings pane for the highlighted entry; null when it can have no key or the window picks.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasHotkeys))] private ActionHotkeys? _hotkeys;

    public bool HasHotkeys => Hotkeys is not null;

    partial void OnOnlyWithKeyChanged(bool value) => Reload();

    private IReadOnlyList<string>? KeysOf(PickChoice choice) => _manage?.KeysOf(choice);

    private PickerRow Row(PickChoice choice, string? hint) => new(choice, choice.Name, hint, choice.Command is null ? KeysOf(choice) : null, IsInPanel(choice));

    private bool PassesKeyFilter(PickChoice choice) =>
        (!OnlyWithKey || !ListsActions || KeysOf(choice) is { Count: > 0 }) && (!OnlyInPanel || IsInPanel(choice));

    private void SyncHotkeys()
    {
        Hotkeys?.Dispose();
        Hotkeys = _manage is not null && ActiveChoice is { Command: null } choice ? _manage.PaneFor(choice) : null;
    }

    /// <summary>The keys changed somewhere (this window, a set switched, Clear all): the rows show them again, the same entry highlighted.</summary>
    public void RefreshKeys()
    {
        var keep = ActiveChoice;
        _index = null; // the keys are searched too
        var rows = Build();
        var at = keep is null ? -1 : IndexIn(rows, keep.Spec);
        ShowRows(rows, () => at >= 0 ? at : FirstEntry(), sync: at < 0); // the same entry keeps its settings pane (a key may be being typed there)
    }

    /// <summary>Opens the entry with this <see cref="PickChoice.Spec"/>: its section, the entry highlighted (the search and the filter left).</summary>
    public void ShowEntry(string spec)
    {
        var section = _groups.FindIndex(g => g.Choices.Any(c => c.Spec == spec));
        if (section < 0) return;
        OnlyWithKey = false;
        Query = string.Empty;
        SectionIndex = section;
        var at = IndexIn(Actions, spec);
        if (at >= 0) ActionIndex = at;
    }

    /// <summary>Lets go of the hotkey pane and of the list it follows (the window closes).</summary>
    public void Release()
    {
        _search.Cancel();
        Hotkeys?.Dispose();
        if (_panel is not null) _panel.Changed -= OnPanelChanged;
        StopFollowing();
    }
}
