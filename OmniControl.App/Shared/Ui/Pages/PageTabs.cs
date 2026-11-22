namespace OmniControl.App.Shared.Ui.Pages;

/// The tabs of a page (the Volume page's Mixer and Wiring): their names and which one is open. Shown by <see cref="PageTabsView"/>;
/// the page shows the content of <see cref="Current"/>. The choice lives as long as the page's view-model (one run).
public sealed partial class PageTabs : ObservableObject
{
    public PageTabs(params string[] names)
    {
        Items = names.Select(n => new PageTab(n, Select)).ToList();
        Items[0].IsSelected = true;
    }

    public IReadOnlyList<PageTab> Items { get; }

    /// More than one tab: the strip shows (a page left with one, its other parts' features off, shows none).
    public bool HasChoice => Items.Count > 1;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Current))] private int _selected;

    /// The name of the open tab.
    public string Current => Items[Selected].Name;

    public void Select(string name)
    {
        var at = Items.ToList().FindIndex(t => t.Name == name);
        if (at >= 0) Selected = at;
    }

    /// The next or previous tab, wrapping round.
    public void Step(int direction) => Selected = Wrap.Index(Selected + direction, Items.Count);

    partial void OnSelectedChanged(int value)
    {
        for (var i = 0; i < Items.Count; i++) Items[i].IsSelected = i == value;
    }
}

/// One tab: its name, whether it is open, and the click that opens it.
public sealed partial class PageTab : ObservableObject
{
    private readonly Action<string> _select;

    public PageTab(string name, Action<string> select)
    {
        Name = name;
        _select = select;
    }

    public string Name { get; }

    [ObservableProperty] private bool _isSelected;

    [RelayCommand] private void Select() => _select(Name);
}
