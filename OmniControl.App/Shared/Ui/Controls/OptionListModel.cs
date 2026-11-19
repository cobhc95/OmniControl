namespace OmniControl.App.Shared.Ui.Controls;

/// One entry of an <see cref="OptionList"/>: its id, label and whether it is ticked.
public sealed partial class OptionEntry : ObservableObject
{
    public OptionEntry(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isChecked;
}

/// What an <see cref="OptionList"/> shows: the entries (from <see cref="SetItems"/>) and which one the keyboard is on. It never decides
/// anything itself: choosing an entry calls the host's <c>chosen</c>, which ticks or unticks it (or calls <see cref="SetItems"/> again).
public sealed partial class OptionListModel : ObservableObject
{
    private readonly Action<OptionEntry> _chosen;

    /// <param name="chosen">The user chose an entry (click, Enter or Space).</param>
    public OptionListModel(Action<OptionEntry> chosen) => _chosen = chosen;

    /// The entries shown now, in order.
    public ObservableCollection<OptionEntry> Shown { get; } = new();

    /// The index in <see cref="Shown"/> of the entry the keyboard is on; -1 = none.
    [ObservableProperty] private int _highlight = -1;

    /// Replaces the entries (from the host's source), keeping the highlighted entry.
    public void SetItems(IEnumerable<OptionEntry> items)
    {
        var keep = Highlighted?.Id;
        Shown.Clear();
        foreach (var entry in items) Shown.Add(entry);
        var kept = Shown.FirstOrDefault(e => e.Id == keep) ?? Shown.FirstOrDefault();
        Highlight = kept is null ? -1 : Shown.IndexOf(kept);
    }

    public OptionEntry? Highlighted => Highlight >= 0 && Highlight < Shown.Count ? Shown[Highlight] : null;

    /// Moves the highlight to the next or previous entry, wrapping round.
    public void Move(int direction)
    {
        if (Shown.Count == 0) return;
        Highlight = Wrap.Index(Highlight < 0 ? (direction > 0 ? 0 : -1) : Highlight + direction, Shown.Count);
    }

    /// The pointer is over this entry: it becomes the highlighted one, so hover and the keyboard's highlight are one row.
    public void HighlightEntry(OptionEntry entry)
    {
        var at = Shown.IndexOf(entry);
        if (at >= 0) Highlight = at;
    }

    public void ChooseHighlighted()
    {
        if (Highlighted is { } entry) Choose(entry);
    }

    public void Choose(OptionEntry entry) => _chosen(entry);
}
