using System.Windows.Input;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Shared.Presets.Manager;

/// What a preset row asks of the card that holds it (the card knows the group's provider and shortcuts).
internal interface IPresetRowOwner
{
    void Apply(PresetRowViewModel row);
    void Rename(PresetRowViewModel row, string name);
    void RenameStarted(PresetRowViewModel row);
    void Delete(PresetRowViewModel row);
    void Duplicate(PresetRowViewModel row);
    void Export(PresetRowViewModel row);
    void SetShortcut(PresetRowViewModel row, string gesture);
    void PickTrigger(PresetRowViewModel row, bool state);
    void RemoveTrigger(PresetRowViewModel row, int index);
    void FlipJoin(PresetRowViewModel row, int index);
}

/// One preset of a card: its name (renamed in place), whether it is in use, its tags, its trigger chips (the shortcut, then the states and
/// actions of its rule, joined by "or" / "and"; "+ Add" offers Hotkey, State…, Action…) and what can be done with it. The shortcut box edits a draft action that is never registered (<see cref="Shortcut"/>): a captured key goes to the card, which gives it to
/// the preset through <see cref="PresetShortcuts"/> or refuses it with the reason, and the box shows what is true again.
public sealed partial class PresetRowViewModel : ObservableObject
{
    private readonly IPresetRowOwner _owner;
    private readonly bool _canDuplicate, _canExport;

    internal PresetRowViewModel(PresetEntry entry, string shortcut, IReadOnlyList<PresetTrigger> triggers, bool hasRules, string applyText, bool canDuplicate,
        bool canExport, IPresetRowOwner owner)
    {
        _owner = owner;
        _entry = entry;
        (_triggers, _hasRules) = (triggers, hasRules);
        ApplyText = applyText;
        (_canDuplicate, _canExport) = (canDuplicate, canExport);
        Shortcut = new HotkeyAction(entry.Id, entry.Name, string.Empty, string.Empty, _ => { });
        Shortcut.Restore(new StoredHotkey(shortcut, false, new List<string>()));
        Shortcut.Changed += action => _owner.SetShortcut(this, action.Gesture);
        Shortcut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(HotkeyTarget.IsListening) or nameof(HotkeyTarget.Gesture)) OnPropertyChanged(nameof(Chips));
        };
    }

    private IReadOnlyList<PresetTrigger> _triggers;
    private bool _hasRules;

    /// The words a user can read on a row, for search (the manager's card in App settings' search).
    public static IReadOnlyList<string> Words { get; } = new[] { "Add", "Hotkey", StateRow, ActionRow, "Rename", "Delete", "trigger", "or", "and", NoRules };

    private const string StateRow = "State…", ActionRow = "Action…", NoRules = "Needs Hotkeys and automation switched on";

    /// The chip row: the key box while it listens, else the shortcut's chip, then the triggers, then "+ Add".
    public IReadOnlyList<object> Chips
    {
        get
        {
            var chips = new List<object>();
            if (Shortcut.IsListening) chips.Add(new PresetCaptureChip(Shortcut));
            else if (Shortcut.Gesture.Length > 0)
                chips.Add(new PresetChip(Shortcut.Gesture, string.Empty, null, new RelayCommand(() => Shortcut.Gesture = string.Empty), "The shortcut that switches to this preset"));
            for (var i = 0; i < _triggers.Count; i++)
            {
                var at = i;
                // A key is a moment, so the join after it is always "or"; between triggers a click flips it.
                var join = i == 0 ? (chips.Count > 0 ? "or" : string.Empty) : _triggers[i].Join;
                chips.Add(new PresetChip(_triggers[i].Text, join, i == 0 ? null : new RelayCommand(() => _owner.FlipJoin(this, at)),
                    new RelayCommand(() => _owner.RemoveTrigger(this, at)), _triggers[i].Text));
            }
            chips.Add(new PresetAddChip(AddRows));
            return chips;
        }
    }

    /// "+ Add": Hotkey listens for keys in place; State… and Action… open the picker (greyed while automation is off).
    private IReadOnlyList<MenuRowItem> AddRows => new[]
    {
        new MenuRowItem("Hotkey", string.Empty, false, string.Empty, new RelayCommand(() => Shortcut.IsListening = true)),
        new MenuRowItem(StateRow, string.Empty, false, _hasRules ? string.Empty : NoRules, new RelayCommand(() => _owner.PickTrigger(this, true)), IsEnabled: _hasRules),
        new MenuRowItem(ActionRow, string.Empty, false, _hasRules ? string.Empty : NoRules, new RelayCommand(() => _owner.PickTrigger(this, false)), IsEnabled: _hasRules),
    };

    private PresetEntry _entry;

    public PresetEntry Entry => _entry;
    public string Id => _entry.Id;
    public string Name => _entry.Name;
    public bool InUse => _entry.InUse;
    public bool CanApply => !_entry.InUse;
    public bool CanRename => _entry.CanRename;
    public bool CanDelete => _entry.CanDelete;
    public bool CanMove => _entry.CanMove;
    public string DeleteText => _entry.ClearsOnly ? "Clear" : "Delete";
    public IReadOnlyList<TagItem> Tags => _entry.Tags;
    public string ApplyText { get; }

    /// Copy and file export live in the row's "more" menu, offered only where the group has them.
    public bool CanDuplicate => _canDuplicate;
    public bool CanExport => _canExport;
    public bool HasMore => _canDuplicate || _canExport;

    /// The "…" menu's rows (<c>Controls/MenuRowList</c>): Duplicate and Export where the group allows them.
    public IReadOnlyList<MenuRowItem> MoreRows
    {
        get
        {
            var rows = new List<MenuRowItem>();
            if (_canDuplicate) rows.Add(new MenuRowItem("Duplicate", string.Empty, false, string.Empty, DuplicateCommand));
            if (_canExport) rows.Add(new MenuRowItem("Export to a file…", string.Empty, false, string.Empty, ExportCommand));
            return rows;
        }
    }

    public string NameTip => _entry.BuiltIn ? "Comes with the app: it can be applied, copied and given a shortcut" : _entry.CanRename ? "Double-click to rename" : string.Empty;

    /// The longest name the rename box takes (<see cref="PresetRules.MaxNameLength"/>).
    public int NameMaxLength => PresetRules.MaxNameLength;

    /// The shortcut box's draft (see the class summary).
    public HotkeyAction Shortcut { get; }

    /// The name is being typed in place.
    [ObservableProperty] private bool _isEditing;

    [ObservableProperty] private string _editText = string.Empty;

    /// Shows the preset as it is now (after any change of its group).
    internal void Update(PresetEntry entry, string shortcut, IReadOnlyList<PresetTrigger> triggers, bool hasRules)
    {
        _entry = entry;
        (_triggers, _hasRules) = (triggers, hasRules);
        OnPropertyChanged(string.Empty);
        ShowShortcut(shortcut);
    }

    /// Shows this shortcut in the box without taking it as a new one.
    internal void ShowShortcut(string shortcut)
    {
        if (Shortcut.Gesture != shortcut) Shortcut.Restore(new StoredHotkey(shortcut, false, new List<string>()));
    }

    [RelayCommand]
    private void Apply() => _owner.Apply(this);

    [RelayCommand]
    private void BeginRename()
    {
        if (!CanRename) return;
        EditText = Name;
        IsEditing = true;
        _owner.RenameStarted(this);
    }

    /// Enter, or leaving the box: the typed name is taken (a refused one stays in the box with the reason on the window's line).
    public void CommitRename()
    {
        if (!IsEditing) return;
        if (EditText.Trim() == Name)
        {
            IsEditing = false;
            return;
        }
        _owner.Rename(this, EditText);
    }

    public void CancelRename() => IsEditing = false;

    [RelayCommand]
    private void Delete() => _owner.Delete(this);

    [RelayCommand]
    private void Duplicate() => _owner.Duplicate(this);

    [RelayCommand]
    private void Export() => _owner.Export(this);
}

/// One chip of a preset row: its words, the join word before it (empty: none), what a click on the join does (null: a fixed "or"), the cross.
public sealed record PresetChip(string Text, string Join, ICommand? Flip, ICommand Remove, string Tip)
{
    public bool HasJoin => Join.Length > 0 && Flip is not null;
    public bool HasFixedJoin => Join.Length > 0 && Flip is null;
}

/// The shortcut box in the chip row while it listens for keys.
public sealed record PresetCaptureChip(HotkeyAction Shortcut);

/// The dashed "+ Add" at the end of the chip row and its pop-down.
public sealed record PresetAddChip(IReadOnlyList<MenuRowItem> Rows);