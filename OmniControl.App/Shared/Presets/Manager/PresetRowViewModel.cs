using OmniControl.App.Features.Volume.Audio;
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
}

/// One preset of a card: its name (renamed in place), whether it is in use, its tags, its shortcut box and what can be done with it. The
/// shortcut box edits a draft action that is never registered (<see cref="Shortcut"/>): a captured key goes to the card, which gives it to
/// the preset through <see cref="PresetShortcuts"/> or refuses it with the reason, and the box shows what is true again.
public sealed partial class PresetRowViewModel : ObservableObject
{
    private readonly IPresetRowOwner _owner;
    private readonly bool _canDuplicate, _canExport;

    internal PresetRowViewModel(PresetEntry entry, string shortcut, string applyText, bool canDuplicate, bool canExport, IPresetRowOwner owner)
    {
        _owner = owner;
        _entry = entry;
        ApplyText = applyText;
        (_canDuplicate, _canExport) = (canDuplicate, canExport);
        Shortcut = new HotkeyAction(entry.Id, entry.Name, string.Empty, string.Empty, _ => { });
        Shortcut.Restore(new StoredHotkey(shortcut, false, new List<string>()));
        Shortcut.Changed += action => _owner.SetShortcut(this, action.Gesture);
    }

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

    public string NameTip => _entry.BuiltIn ? "Comes with the app: it can be applied, copied and given a shortcut" : _entry.CanRename ? "Double-click to rename" : _entry.Name;

    /// The longest name the rename box takes (<see cref="PresetRules.MaxNameLength"/>).
    public int NameMaxLength => PresetRules.MaxNameLength;

    /// The shortcut box's draft (see the class summary).
    public HotkeyAction Shortcut { get; }

    /// The name is being typed in place.
    [ObservableProperty] private bool _isEditing;

    [ObservableProperty] private string _editText = string.Empty;

    /// Shows the preset as it is now (after any change of its group).
    internal void Update(PresetEntry entry, string shortcut)
    {
        _entry = entry;
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
