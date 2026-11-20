using System.Windows.Input;
using Avalonia.Input;

namespace OmniControl.App.Shared.Ui.Controls;

/// One row of a <see cref="MenuRowList"/>: its words (shown whole, wrapping), a "Get it" style link at the right (may be empty), whether it is the
/// one that is on now, a tooltip and what it does; optionally tags (after the words, or on their own line under them with
/// <see cref="TagsOnOwnLine"/>), one muted line of detail, a dimmed look, a row that keeps the menu open when clicked (a ticked list where
/// several can be ticked), a <see cref="Trailing"/> fact in the warm colour (<see cref="TrailingWarm"/>), an accent row (the one that opens something bigger) or a muted row (a way out to a manager), or a header row (a muted section title) with a thin line above it.
/// A row that cannot be used now (<see cref="IsEnabled"/> false) is greyed and keeps its tooltip, which says why. A row that shows another page of
/// the same menu in place (<see cref="OpensPage"/>; its command turns the page and it does not close the menu) has the chevron a sub-menu row has.
public sealed record MenuRowItem(string Text, string Link, bool IsOn, string Tip, ICommand Command, IReadOnlyList<TagItem>? Tags = null, string Detail = "", bool Dim = false, bool IsHeader = false, bool RuleAbove = false, bool TagsOnOwnLine = false, bool IsSwitch = false, bool KeepsOpen = false, bool IsAccent = false, bool IsMuted = false, string? Icon = null, bool IsDanger = false, string Trailing = "", Func<IReadOnlyList<MenuRowItem>>? Submenu = null, bool TrailingWarm = false, bool IsEnabled = true, bool OpensPage = false)
{
    /// A row that opens a nested menu at its side (a chevron at its right) when the pointer rests on it, on a click or on Right; the rows are asked each time the menu opens and again after a
    /// <see cref="KeepsOpen"/> row of it is clicked, so its ticks follow.
    public static MenuRowItem Sub(string text, string icon, Func<IReadOnlyList<MenuRowItem>> rows, string trailing = "") =>
        new(text, string.Empty, false, string.Empty, NoCommand, Icon: icon, Trailing: trailing, Submenu: rows);

    public bool HasChevron => Submenu is not null || OpensPage;

    /// True when the row has an icon (an icon-library id) in its glyph slot; null means nothing is drawn. The tick of a ticked row takes the slot instead.
    public bool HasIcon => Icon is not null && !ShowsTick;

    /// A muted section title with no command.
    public static MenuRowItem Header(string text, bool ruleAbove = false) => new(text, string.Empty, false, string.Empty, NoCommand, IsHeader: true, RuleAbove: ruleAbove);

    private static readonly ICommand NoCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });

    public bool IsRow => !IsHeader;
    public IReadOnlyList<TagItem> InlineTags => TagsOnOwnLine ? Array.Empty<TagItem>() : Tags ?? Array.Empty<TagItem>();
    public IReadOnlyList<TagItem> OwnLineTags => TagsOnOwnLine ? Tags ?? Array.Empty<TagItem>() : Array.Empty<TagItem>();
    public bool HasOwnLineTags => OwnLineTags.Count > 0;
    public double RowOpacity => Dim ? 0.6 : 1;
    public bool HasLink => Link.Length > 0;

    /// The one in force of a single-choice menu: the accent bar at its left (<c>Button.menurow.chosen</c>). A row that keeps the menu
    /// open belongs to a multi-choice menu and shows an accent tick instead; a switch row shows its switch.
    public bool ShowsBar => IsOn && !IsSwitch && !KeepsOpen;
    public bool ShowsTick => IsOn && !IsSwitch && KeepsOpen;

    /// The row needs the glyph column (an icon, or a tick that may show).
    internal bool UsesGlyph => Icon is not null || (KeepsOpen && !IsSwitch);
    public bool HasDetail => Detail.Length > 0;

    /// A muted small fact at the row's right on the name's own line (the name then trims instead of wrapping, its full text is the tooltip).
    public bool OneLine => Trailing.Length > 0;
    public bool NotOneLine => !OneLine;
}

/// One place of a <see cref="MenuRowList"/>: it keeps its row control while the rows are asked again, and only its <see cref="Row"/> changes.
public sealed partial class MenuRowSlot : ObservableObject
{
    [ObservableProperty] private MenuRowItem _row;

    public MenuRowSlot(MenuRowItem row) => _row = row;
}

/// Layout and wiring only: the rows of a small menu; a row click raises <see cref="Picked"/> (unless the row keeps the menu open) so the owner can close its pop-down (the row's own command runs as well).
public partial class MenuRowList : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<MenuRowList, string?>(nameof(Title));
    public static readonly StyledProperty<IEnumerable<MenuRowItem>?> ItemsSourceProperty = AvaloniaProperty.Register<MenuRowList, IEnumerable<MenuRowItem>?>(nameof(ItemsSource));

    private readonly ObservableCollection<MenuRowSlot> _slots = new();

    public MenuRowList()
    {
        InitializeComponent();
        Rows.ItemsSource = _slots;
    }

    /// The muted title above the rows (empty: none).
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// The rows (set, or bound to a model's list).
    public IEnumerable<MenuRowItem>? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty) Fill(ItemsSource?.ToList() ?? new List<MenuRowItem>());
    }

    // Rows given again (live values, a tick after a click, another page) fill the places already shown instead of new ones: a new row
    // control would lose the pointer's hover until the mouse moved, and the keyboard's focus.
    private void Fill(List<MenuRowItem> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (i < _slots.Count) _slots[i].Row = rows[i];
            else _slots.Add(new MenuRowSlot(rows[i]));
        }
        while (_slots.Count > rows.Count) _slots.RemoveAt(_slots.Count - 1);
        Classes.Set("noglyphs", !rows.Any(r => r.UsesGlyph));
    }

    public event EventHandler? Picked;

    /// Asks the rows again after a row that keeps the menu open was clicked (set on a nested menu, whose ticks must follow).
    private Func<IReadOnlyList<MenuRowItem>>? _refresh;
    private Flyout? _nested;
    private Control? _nestedOpener;

    /// Opens or closes the nested menu once the pointer has rested (Windows' menu show delay), as Windows' own menus do.
    private DispatcherTimer? _rest;
    private Action? _afterRest;

    private void OnRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: MenuRowSlot { Row: var row } } opener) return;
        if (row.Submenu is { } rows)
        {
            OpenNested(opener, rows);
            return;
        }
        if (row.OpensPage) return;
        if (!row.KeepsOpen)
        {
            Picked?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (_refresh is { } refresh) Dispatcher.UIThread.Post(() => ItemsSource = refresh()); // after the row's command has run
    }

    /// Resting on a sub-menu row opens its menu; resting on another row closes the open one; Right opens it from the keyboard.
    private void OnRowEnter(object? sender, PointerEventArgs e)
    {
        if (sender is not Control { DataContext: MenuRowSlot { Row: var row } } opener) return;
        _rest?.Stop();
        if (IsOpenFrom(opener)) return;
        if (row.Submenu is { } rows) AfterRest(() => OpenNested(opener, rows));
        else if (_nested is { IsOpen: true } nested) AfterRest(nested.Hide);
    }

    private void OnRowExit(object? sender, PointerEventArgs e) => _rest?.Stop();

    private void OnRowKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Right || sender is not Control { DataContext: MenuRowSlot { Row.Submenu: { } rows } } opener) return;
        OpenNested(opener, rows);
        e.Handled = true;
    }

    private void AfterRest(Action act)
    {
        if (_rest is null)
        {
            var delay = OmniControl.Core.Engine.Mouse.SystemCommands.MenuShowDelay() is { } ms ? TimeSpan.FromMilliseconds(ms) : Timing.SubmenuDelay;
            _rest = new DispatcherTimer { Interval = delay };
            _rest.Tick += (_, _) =>
            {
                _rest.Stop();
                _afterRest?.Invoke();
            };
        }
        _afterRest = act;
        _rest.Start();
    }

    private bool IsOpenFrom(Control opener) => _nested is { IsOpen: true } && _nestedOpener == opener;

    private void OpenNested(Control opener, Func<IReadOnlyList<MenuRowItem>> rows)
    {
        _rest?.Stop();
        if (IsOpenFrom(opener)) return;
        _nested?.Hide();
        var list = new MenuRowList { ItemsSource = rows(), _refresh = rows };
        var flyout = _nested = new ListMenuFlyout { Placement = PlacementMode.RightEdgeAlignedTop, Content = list };
        _nestedOpener = opener;
        list.PointerEntered += (_, _) => _rest?.Stop(); // the pointer reached the open sub-menu: keep it
        list.Picked += (_, _) =>
        {
            flyout.Hide();
            Picked?.Invoke(this, EventArgs.Empty);
        };
        flyout.ShowAt(opener);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _rest?.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
