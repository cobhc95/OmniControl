using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// One row of a Wiring table: a source (the master, a program, a microphone: the same <see cref="VolumeRow"/> the Mixer shows, so its
/// name, icon, dimming and remove button are that row's), the devices it plays on or records from, and the cable it uses (with the
/// step that gets one while a microphone waits). The cell is one of: chips with "+ add" (several devices; a combined microphone's
/// members), one drop-down-like choice (one device), or words with a dashed chip ("This microphone", "Combine with..."). The page fills
/// it from the routing model on every sync and decides what a choice does; this class keeps no routing of its own.
/// </summary>
public sealed partial class WiringRow : ObservableObject, IEditorItem
{
    private readonly WiringAdd _add;
    private readonly WiringSingle _single;

    /// <param name="menu">The cell's menu (the shared "Plays on" menu; the page fills it as it opens).</param>
    public WiringRow(VolumeRow source, PlaysOnMenu menu)
    {
        Source = source;
        Menu = menu;
        _add = new WiringAdd(this);
        _single = new WiringSingle(this);
    }

    string IEditorItem.Label => Source.Name;

    /// <summary>The Mixer row this source is.</summary>
    public VolumeRow Source { get; }

    public string Key => Source.Key;

    /// <summary>What the cell shows, in order: <see cref="WiringChip"/>s, a <see cref="WiringNote"/>, then <see cref="WiringAdd"/>, or one <see cref="WiringSingle"/>.</summary>
    public ObservableCollection<object> Cell { get; } = new();

    [ObservableProperty] private CableState _cable = CableState.None;

    /// <summary>Install VB-CABLE or More cables beside the Cable column while a microphone waits for a cable; null = nothing to get.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCableAction))] private CardAction? _cableAction;

    public bool HasCableAction => CableAction is not null;

    /// <summary>The cell's menu: what can be added or chosen, and why the row takes one device only (its gate).</summary>
    public PlaysOnMenu Menu { get; }

    /// <summary>
    /// Several devices: their chips (<see cref="WiringChip"/>, or <see cref="WiringMember"/> for a combined microphone's members), the
    /// words shown when there are none ("Follows the master"), then "+ add" (its words and tooltip).
    /// </summary>
    public void ShowChips(IEnumerable<object> chips, string? emptyNote, string addLabel = WiringAdd.AddLabel, string addTip = WiringAdd.AddTip)
    {
        var items = chips.ToList();
        if (items.Count == 0 && emptyNote is not null) items.Add(new WiringNote(emptyNote));
        _add.Set(addLabel, addTip);
        items.Add(_add);
        Show(items);
    }

    /// <summary>Plain words and a dashed chip that opens the menu ("This microphone" and "Combine with...").</summary>
    public void ShowFixed(string text, string addLabel, string addTip)
    {
        _add.Set(addLabel, addTip);
        Show(new object[] { new WiringNote(text), _add });
    }

    /// <summary>One device: one choice that opens the menu.</summary>
    public void ShowSingle(string text)
    {
        _single.Text = text;
        Show(new object[] { _single });
    }

    /// <summary>
    /// Puts the cell's pieces in place, touching only what changed: the "+ add" chip and the single choice stay the same objects, so
    /// the button that had the keyboard keeps it after a choice.
    /// </summary>
    private void Show(IReadOnlyList<object> items)
    {
        if (items.SequenceEqual(Cell, PieceComparer.Instance)) return;
        for (var i = Cell.Count - 1; i >= 0; i--)
            if (!items.Contains(Cell[i], PieceComparer.Instance)) Cell.RemoveAt(i);
        for (var i = 0; i < items.Count; i++)
        {
            if (i < Cell.Count && PieceComparer.Instance.Equals(Cell[i], items[i])) continue;
            var at = Cell.ToList().FindIndex(c => PieceComparer.Instance.Equals(c, items[i]));
            if (at >= 0) Cell.Move(at, i);
            else Cell.Insert(i, items[i]);
        }
    }

    /// <summary>Two pieces are the same when they show the same thing (a chip's device and words, a note's words).</summary>
    private sealed class PieceComparer : IEqualityComparer<object>
    {
        public static readonly PieceComparer Instance = new();

        public new bool Equals(object? a, object? b) => (a, b) switch
        {
            (WiringChip x, WiringChip y) => x.Id == y.Id && x.Label == y.Label && x.CanRemove == y.CanRemove,
            (WiringMember x, WiringMember y) => x.Id == y.Id && x.Label == y.Label && x.CanRemove == y.CanRemove && x.Tip == y.Tip,
            (WiringNote x, WiringNote y) => x.Text == y.Text,
            _ => ReferenceEquals(a, b),
        };

        public int GetHashCode(object obj) => obj switch
        {
            WiringChip c => c.Id.GetHashCode(),
            WiringMember m => m.Id.GetHashCode(),
            WiringNote n => n.Text.GetHashCode(),
            _ => obj.GetHashCode(),
        };
    }
}

/// <summary>
/// A combined microphone's member in its Wiring cell: its words open a small pop-down with the member's level and mute
/// (<see cref="Menu"/>); its x takes it away (unless it is the last one).
/// </summary>
public sealed partial class WiringMember
{
    private readonly Action _remove;

    public WiringMember(string id, string label, string tip, bool canRemove, MemberLevelMenu menu, Action remove)
    {
        Id = id;
        Label = label;
        Tip = tip;
        CanRemove = canRemove;
        Menu = menu;
        _remove = remove;
    }

    public string Id { get; }
    public string Label { get; }

    /// <summary>What a click does, and the facts that matter (its own effects are not in the mix; Stereo Mix records the PC's sound).</summary>
    public string Tip { get; }

    public bool CanRemove { get; }
    public MemberLevelMenu Menu { get; }

    [RelayCommand] private void Remove() { if (CanRemove) _remove(); }
}

/// <summary>A chosen device in a Wiring cell: its words; a click takes it away (unless it is the last one that must stay).</summary>
public sealed partial class WiringChip
{
    private readonly Action _remove;

    public WiringChip(string id, string label, bool canRemove, Action remove)
    {
        Id = id;
        Label = label;
        CanRemove = canRemove;
        _remove = remove;
    }

    public string Id { get; }
    public string Label { get; }
    public bool CanRemove { get; }
    public string Tip => CanRemove ? $"Take {Label} away" : $"{Label} (the last device stays)";

    [RelayCommand] private void Remove() { if (CanRemove) _remove(); }
}

/// <summary>Words in a Wiring cell that are not a choice ("Follows the master", "This microphone").</summary>
public sealed record WiringNote(string Text);

/// <summary>The dashed chip at the end of a cell ("+ add", "Combine with..."); it opens the cell's menu.</summary>
public sealed partial class WiringAdd : ObservableObject
{
    public const string AddLabel = "+ add", AddTip = "Add a device";

    public WiringAdd(WiringRow row) => Row = row;

    public WiringRow Row { get; }

    [ObservableProperty] private string _label = AddLabel;
    [ObservableProperty] private string _tip = AddTip;

    public void Set(string label, string tip)
    {
        Label = label;
        Tip = tip;
    }
}

/// <summary>A cell that takes one device: the device's name as a drop-down-like choice that opens the cell's menu.</summary>
public sealed partial class WiringSingle : ObservableObject
{
    public WiringSingle(WiringRow row) => Row = row;

    public WiringRow Row { get; }

    [ObservableProperty] private string _text = string.Empty;
}

/// <summary>
/// One entry of a "Plays on" menu (<see cref="PlaysOnMenu"/>, both tabs): a device that can be added (or chosen), one in use now (ticked,
/// the Mixer's menu), or one that can't be used (dimmed, its reason as tooltip, a small tag).
/// </summary>
public sealed partial class WiringChoice
{
    private readonly Action _choose;

    /// <param name="caution">The tag is a fact to keep in mind (yellow), such as "Records the PC's sound".</param>
    /// <param name="tip">The tooltip of an entry that can be chosen (a dimmed one shows its reason).</param>
    /// <param name="isCurrent">The row plays there now (the entry is ticked).</param>
    public WiringChoice(string label, string? tag, string? disabledReason, Action choose, bool caution = false, string? tip = null, bool isCurrent = false)
    {
        Label = label;
        Tag = tag;
        DisabledReason = disabledReason;
        IsCaution = caution;
        Tip = disabledReason ?? tip;
        IsCurrent = isCurrent;
        _choose = choose;
    }

    public string Label { get; }

    /// <summary>The row plays there now: the entry carries a tick.</summary>
    public bool IsCurrent { get; }

    /// <summary>A small fact at the right ("Cable", "Combined"); null = none.</summary>
    public string? Tag { get; }
    public bool HasTag => Tag is not null;
    public bool IsCaution { get; }

    public string? DisabledReason { get; }
    public string? Tip { get; }
    public bool IsEnabled => DisabledReason is null;

    [RelayCommand] private void Choose() { if (IsEnabled) _choose(); }
}

/// <summary>
/// The Cable column of a Wiring row or combined device: the cable it uses now (its short name; the tooltip says what it carries), the
/// engine's own "Needs VB-CABLE" / "Needs another virtual cable" (a caution), or nothing ("—").
/// </summary>
public sealed record CableState(string Text, string Tip, bool IsCaution)
{
    public static readonly CableState None = new("—", "No virtual cable is used", false);

    /// <summary>A cable or a caution to show as a tag (otherwise the column shows the plain dash).</summary>
    public bool HasTag => !ReferenceEquals(this, None);
}
