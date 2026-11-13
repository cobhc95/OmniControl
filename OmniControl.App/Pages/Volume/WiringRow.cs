using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// One row of a Wiring table: a source (the master, a program, a microphone: the same <see cref="VolumeRow"/> the Mixer shows, so its
/// name, icon, dimming and remove button are that row's), the devices it plays on or records from, and the cable it uses. The cell is
/// one of: chips with "+ add" (several devices), one drop-down-like choice (one device), or plain words (nothing to choose). The page
/// fills it from the routing model on every sync and decides what a choice does; this class keeps no routing of its own.
/// </summary>
public sealed partial class WiringRow : ObservableObject, IEditorItem
{
    private readonly Action<WiringRow> _opening;
    private readonly WiringAdd _add;
    private readonly WiringSingle _single;

    /// <param name="opening">The cell's menu is opening: the page fills <see cref="Choices"/> from what is plugged in now.</param>
    public WiringRow(VolumeRow source, ModeGate menuGate, Action<WiringRow> opening)
    {
        Source = source;
        MenuGate = menuGate;
        _opening = opening;
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

    /// <summary>Why the cell can't take several devices in this mode (its menu says so, with Switch); open = it can.</summary>
    public ModeGate MenuGate { get; }

    /// <summary>The menu's entries (filled when it opens).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNoChoices))] private IReadOnlyList<WiringChoice> _choices = Array.Empty<WiringChoice>();

    public bool HasNoChoices => Choices.Count == 0;

    /// <summary>The menu's title ("Add a device", "Choose a device").</summary>
    [ObservableProperty] private string _menuTitle = string.Empty;

    /// <summary>False when the cell only shows words (a microphone records from itself): no menu.</summary>
    [ObservableProperty] private bool _hasMenu;

    /// <summary>The cell's menu is opening.</summary>
    public void OpenMenu() => _opening(this);

    /// <summary>Several devices: their chips, the words shown when there are none ("Follows the master"), then "+ add".</summary>
    public void ShowChips(IEnumerable<WiringChip> chips, string? emptyNote)
    {
        var items = chips.Cast<object>().ToList();
        if (items.Count == 0 && emptyNote is not null) items.Add(new WiringNote(emptyNote));
        items.Add(_add);
        HasMenu = true;
        Show(items);
    }

    /// <summary>One device: one choice that opens the menu.</summary>
    public void ShowSingle(string text)
    {
        _single.Text = text;
        HasMenu = true;
        Show(new object[] { _single });
    }

    /// <summary>Nothing to choose: plain words.</summary>
    public void ShowFixed(string text)
    {
        HasMenu = false;
        Show(new object[] { new WiringNote(text) });
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
            (WiringNote x, WiringNote y) => x.Text == y.Text,
            _ => ReferenceEquals(a, b),
        };

        public int GetHashCode(object obj) => obj switch
        {
            WiringChip c => c.Id.GetHashCode(),
            WiringNote n => n.Text.GetHashCode(),
            _ => obj.GetHashCode(),
        };
    }
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

/// <summary>The dashed "+ add" chip at the end of a cell; it opens the cell's menu.</summary>
public sealed class WiringAdd
{
    public WiringAdd(WiringRow row) => Row = row;

    public WiringRow Row { get; }
}

/// <summary>A cell that takes one device: the device's name as a drop-down-like choice that opens the cell's menu.</summary>
public sealed partial class WiringSingle : ObservableObject
{
    public WiringSingle(WiringRow row) => Row = row;

    public WiringRow Row { get; }

    [ObservableProperty] private string _text = string.Empty;
}

/// <summary>One entry of a Wiring cell's menu: a device that can be added (or chosen), or one this mode can't use (dimmed, its reason as tooltip, a small tag).</summary>
public sealed partial class WiringChoice
{
    private readonly Action _choose;

    public WiringChoice(string label, string? tag, string? disabledReason, Action choose)
    {
        Label = label;
        Tag = tag;
        DisabledReason = disabledReason;
        _choose = choose;
    }

    public string Label { get; }

    /// <summary>A small fact at the right ("Full control" on a device this mode can't use, "Cable"); null = none.</summary>
    public string? Tag { get; }
    public bool HasTag => Tag is not null;

    public string? DisabledReason { get; }
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
