using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// The keys and defaults part of the Choose-the-action window, in both modes: the keys of an entry (null when the entry is not a master
/// action, so it can have none), the hotkey part of the settings pane for an entry, the master action an entry stands for (whose defaults the
/// settings pane edits), and the live master list (its saves refresh the keycaps; its hold lets a pressed key reach the window).
public sealed record ManageMode(Func<PickChoice, IReadOnlyList<string>?> KeysOf, Func<PickChoice, ActionHotkeys?> PaneFor, Func<PickChoice, HotkeyAction?> ActionOf, HotkeyRegistry Registry);

/// The manage mode of the picker (<see cref="ManageMode"/>): keycaps on the rows, "Only with a hotkey", search by key, the hotkey pane.
public sealed partial class PickerModel
{
    private ManageMode? _manage;
    private readonly bool _picking;

    /// True when the window picks an entry ("Use this ..." shown); false when it manages the actions and their keys.
    public bool IsPicking => _picking;

    /// True when the keys are shown (keycaps, the filter, the hotkey part): always once the window has given them.
    public bool HasKeys => _manage is not null;

    /// The window gives the keys part to a model a picker caller made (pick mode), so callers stay unaware of it.
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

    /// "Only with a hotkey": the lists show only the entries that have a key.
    [ObservableProperty] private bool _onlyWithKey;

    /// The hotkey part of the settings pane: one block for the highlighted entry, or one per direction when it is a family in the Actions window; empty when it can have no key.
    public ObservableCollection<ActionHotkeys> HotkeyBlocks { get; } = new();

    public bool HasHotkeys => HotkeyBlocks.Count > 0;

    partial void OnOnlyWithKeyChanged(bool value) => Reload();

    private IReadOnlyList<string>? KeysOf(PickChoice choice) => _manage?.KeysOf(choice);

    private PickerRow Row(PickChoice choice, string? hint) => new(choice, choice.Name, hint, choice.Command is null ? KeysOf(choice) : null, IsInPanel(choice));

    private bool PassesKeyFilter(PickChoice choice) =>
        (!OnlyWithKey || !ListsActions || KeysOf(choice) is { Count: > 0 }) && (!OnlyInPanel || IsInPanel(choice));

    private void SyncHotkeys()
    {
        DisposeHotkeys();
        if (_manage is not null && ActiveChoice is { Command: null } active)
        {
            var family = ActiveRow?.Members is not null;
            foreach (var member in HotkeyMembers(active))
                if (_manage.PaneFor(member) is { } block)
                {
                    if (family) block.Role = RoleOf(member);
                    HotkeyBlocks.Add(block);
                }
            if (HotkeyBlocks.Count > 0) HotkeyBlocks[0].ShareScopeWith(HotkeyBlocks.Skip(1));
        }
        OnPropertyChanged(nameof(HasHotkeys));
    }

    private void DisposeHotkeys()
    {
        foreach (var block in HotkeyBlocks) block.Dispose();
        HotkeyBlocks.Clear();
    }

    /// The keys changed somewhere (this window, a set switched, Clear all): the rows show them again, the same entry highlighted.
    public void RefreshKeys()
    {
        var keep = ActiveChoice;
        _index = null; // the keys are searched too
        var rows = Build();
        var at = keep is null ? -1 : IndexIn(rows, keep.Spec);
        ShowRows(rows, () => at >= 0 ? at : FirstEntry(), sync: at < 0); // the same entry keeps its settings pane (a key may be being typed there)
    }

    /// Opens the entry with this <see cref="PickChoice.Spec"/>: its section, the entry highlighted (the search and the filter left).
    public void ShowEntry(string spec)
    {
        var section = _groups.FindIndex(g => g.Choices.Any(c => c.Spec == spec));
        if (section < 0) return;
        OnlyWithKey = false;
        Query = string.Empty;
        var family = _groups[section].Choices.FirstOrDefault(c => c.Spec == spec) is { } entry ? FamilyKeyOf(entry) : null;
        if (family is not null) _memberPicked[family] = spec; // a direction of a family opens its line with that direction shown
        SectionIndex = section;
        var at = IndexIn(Actions, spec);
        if (at >= 0) ActionIndex = at;
        if (family is not null) SyncSettings();
    }

    /// Lets go of the hotkey pane and of the list it follows (the window closes).
    public void Release()
    {
        _search.Cancel();
        DisposeHotkeys();
        if (_panel is not null) _panel.Changed -= OnPanelChanged;
        StopFollowing();
    }
}
