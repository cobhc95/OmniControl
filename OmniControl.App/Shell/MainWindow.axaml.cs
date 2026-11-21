using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Shell;

public partial class MainWindow : Window
{
    private InputElement? _returnFocus;

    public MainWindow()
    {
        InitializeComponent();
        RowEditing.Attach(ResultList);
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPointerPressedTunnel, RoutingStrategies.Tunnel);
        PinnedNav.AddHandler(TappedEvent, (_, _) => OpenPinned());
        PinnedNav.AddHandler(KeyDownEvent, OnPinnedKeyDown);
        Opened += (_, _) => Dispatcher.UIThread.Post(() => FocusSidebar()?.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
        DataContextChanged += (_, _) => Follow(DataContext as ShellViewModel);
        Closed += (_, _) => Follow(null); // let go after a long hide (WindowLifecycle): the shell no longer holds this window
    }

    private ShellViewModel? _shell;

    private void Follow(ShellViewModel? shell)
    {
        if (_shell is not null) _shell.SearchFocusRequested -= OnSearchFocusRequested;
        _shell = shell;
        if (_shell is not null) _shell.SearchFocusRequested += OnSearchFocusRequested;
    }

    /// Where a window stood: its position and size unless maximised (its page's scroll is the page's own, <see cref="PageViewModel.Scroll"/>).
    internal sealed record Placement(PixelPoint Position, Size Size, bool Maximised);

    /// This window's <see cref="Placement"/>, for the window made after it is let go.
    internal Placement Placed => new(Position, ClientSize, WindowState == WindowState.Maximized);

    /// Opens as <paramref name="placed"/> says (call before it is first shown).
    internal void PlaceAs(Placement placed)
    {
        if (placed.Maximised) WindowState = WindowState.Maximized;
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = placed.Position;
            (Width, Height) = (placed.Size.Width, placed.Size.Height);
        }
    }

    /// Before the window is let go: an open tooltip or pop-up is a window of its own holding the control it belongs to, and the keyboard
    /// keeps its focused control; either would keep the whole closed window in memory.
    internal void LetGo()
    {
        foreach (var control in this.GetVisualDescendants().OfType<Control>())
        {
            if (ToolTip.GetIsOpen(control)) ToolTip.SetIsOpen(control, false);
            if (control is Popup { IsOpen: true } popup) popup.IsOpen = false;
            if (control is Button { Flyout: { IsOpen: true } flyout }) flyout.Hide();
            if (FlyoutBase.GetAttachedFlyout(control) is { IsOpen: true } attached) attached.Hide();
        }
        if (FocusManager is { } focus && focus.GetFocusedElement() is Visual focused && this.IsVisualAncestorOf(focused)) focus.ClearFocus();
    }

    /// A click anywhere except the hotkey box that is listening cancels listening.
    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        HotkeyCapture.CancelOnPressElsewhere(FocusManager?.GetFocusedElement(), e.Source);
    }

    /// The highlighted pinned entry opens its window; the entry does not stay selected, as it is not a page.
    private void OpenPinned()
    {
        if (PinnedNav.SelectedItem is not NavLauncher entry) return;
        PinnedNav.SelectedItem = null;
        entry.Open();
    }

    private void OnPinnedKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || e.KeyModifiers != KeyModifiers.None) return;
        OpenPinned();
        e.Handled = true;
    }

    /// The search action ran (any thread): bring the cursor into the search box.
    private void OnSearchFocusRequested() => Dispatcher.UIThread.Post(FocusSearch);

    private void FocusSearch()
    {
        if (!SearchBox.IsFocused) _returnFocus = FocusManager?.GetFocusedElement() as InputElement;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// F1 lists the keyboard shortcuts; Tab swaps between sidebar and page; Ctrl+F jumps to search; Down/Enter in search enters results.
    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        var focused = FocusManager?.GetFocusedElement();

        // While capturing a hotkey, combos like Ctrl+F belong to the box, not to the window.
        if (HotkeyCapture.IsListening(focused)
            && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;

        if (e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None && !HotkeyCapture.IsListening(focused))
        {
            (DataContext as ShellViewModel)?.ShowKeyboardShortcutsCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            FocusSearch();
            e.Handled = true;
        }
        else if (SearchBox.IsFocused && e.Key is Key.Enter or Key.Down)
        {
            FocusPage(fromSearch: true)?.Focus(NavigationMethod.Tab);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && !RowEditing.IsEditing(focused)
                 && (SearchBox.IsFocused || focused is Visual fv && ContentHost.IsVisualAncestorOf(fv) && (DataContext as ShellViewModel)?.IsSearching == true))
        {
            // Esc in the box: clear it and go back to where Ctrl+F was pressed; from a result row: back to the box.
            if (SearchBox.IsFocused)
            {
                SearchBox.Text = string.Empty;
                var back = _returnFocus is { IsEffectivelyVisible: true } r && this.IsVisualAncestorOf(r) ? r : FocusPage() ?? FocusSidebar();
                back?.Focus(NavigationMethod.Tab);
            }
            else SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Up && focused is InputElement nav && StepBetweenNavLists(nav, e.Key == Key.Down))
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == 0)
        {
            var inPage = focused is Visual v && ContentHost.IsVisualAncestorOf(v);
            (inPage ? FocusSidebar() : FocusPage())?.Focus(NavigationMethod.Tab);
            e.Handled = true;
        }
    }

    /// Arrow keys continue from the end of the category list to the footer's App settings button and back, and wrap round at either end.
    private bool StepBetweenNavLists(InputElement item, bool down)
    {
        var footer = FooterButtons();
        if (footer.Count == 0) return false;
        if (item is Button { Tag: "footernav" })
        {
            // From the footer button: Down goes round to the first page, Up back to the last.
            var pageRows = NavPageRows();
            if (pageRows.Count == 0) return false;
            return Select(down ? pageRows[0] : pageRows[^1]);
        }
        // Only the sidebar lists hand over; a card list on a page loops inside its own page (Tab leaves it).
        if (item is not ListBoxItem row || row.FindAncestorOfType<ListBox>() is not { } list || (list != MainNav && list != PinnedNav)) return false;
        var rows = NavPageRows();
        var at = rows.IndexOf(row);
        if (at >= 0 && at != (down ? rows.Count - 1 : 0))
        {
            // Inside one list the list steps itself; from the end of the main list to the pinned one (and back) it is done here.
            var next = rows[at + (down ? 1 : -1)];
            return next.FindAncestorOfType<ListBox>() != list && Select(next);
        }
        if (at < 0) return false;
        if (footer[0].DataContext is PageSlot page) (DataContext as ShellViewModel)?.OpenFooterPageCommand.Execute(page);
        footer[0].Focus(NavigationMethod.Directional);
        return true;

        static bool Select(ListBoxItem? target)
        {
            if (target is null) return false;
            target.IsSelected = true;
            target.Focus(NavigationMethod.Directional);
            return true;
        }
    }

    /// The sidebar's page rows in order: the grouped pages, then the pinned ones (the group headings are not among them).
    private List<ListBoxItem> NavPageRows() => MainNav.GetVisualDescendants().Concat(PinnedNav.GetVisualDescendants())
        .OfType<ListBoxItem>().Where(i => i.Focusable).ToList();

    private List<Button> FooterButtons() => FooterNav.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is "footernav").ToList();

    private InputElement? FocusSidebar() =>
        (InputElement?)NavPageRows().FirstOrDefault(i => i.IsSelected)
        ?? (InputElement?)FooterButtons().FirstOrDefault(b => b.Classes.Contains("custom"))
        ?? NavPageRows().FirstOrDefault();

    /// <param name="fromSearch">Coming down from the search box always lands on the first result, not the last selected card.</param>
    private InputElement? FocusPage(bool fromSearch = false)
    {
        var items = ContentHost.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(i => i.IsEffectivelyVisible).ToList();
        var card = fromSearch ? items.FirstOrDefault() : items.FirstOrDefault(i => i.IsSelected) ?? items.FirstOrDefault();
        if (fromSearch && card is not null) card.IsSelected = true;
        if (card is not null) return RowEditing.CardTarget(card);
        // A page whose groups are all collapsed: land on the first group header.
        return ContentHost.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Tag is "grouphdr" && b.IsEffectivelyVisible);
    }

    /// The footer button closes the window exactly like the title-bar X: <see cref="WindowLifecycle"/> follows "When closing".
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
