using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OmniControl.App.Hotkeys;
using OmniControl.App.Pages;

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
        Opened += (_, _) => Dispatcher.UIThread.Post(() => FocusSidebar()?.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
    }

    /// <summary>A click anywhere except the hotkey box that is listening cancels listening.</summary>
    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox { DataContext: HotkeyAction { IsListening: true } action } box
            && !(e.Source is Visual v && (v == box || box.IsVisualAncestorOf(v))))
            action.IsListening = false;
    }

    /// <summary>F1 lists the keyboard shortcuts; Tab swaps between sidebar and page; Ctrl+F jumps to search; Down/Enter in search enters results.</summary>
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
            if (!SearchBox.IsFocused) _returnFocus = focused as InputElement;
            SearchBox.Focus();
            SearchBox.SelectAll();
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
        else if (e.Key is Key.Down or Key.Up && focused is ListBoxItem nav && StepBetweenNavLists(nav, e.Key == Key.Down))
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

    /// <summary>Arrow keys continue from the end of the category list into App settings and back, and wrap round at either end.</summary>
    private bool StepBetweenNavLists(ListBoxItem item, bool down)
    {
        var list = item.FindAncestorOfType<ListBox>();
        // Only the two sidebar lists hand over to each other; a card list on a page loops inside its own page (Tab leaves it).
        if (list != MainNav && list != FooterNav) return false;
        ListBox? other = null;
        var at = list.IndexFromContainer(item);
        if (down && at == list.ItemCount - 1) other = list == MainNav ? FooterNav : MainNav; // past the last item: on to the next list, and round to the top
        else if (!down && at == 0) other = list == FooterNav ? MainNav : FooterNav; // before the first item: back round to the bottom
        if (other is null || other.ItemCount == 0) return false;
        var target = other.ContainerFromIndex(down ? 0 : other.ItemCount - 1) as ListBoxItem;
        if (target is null) return false;
        target.IsSelected = true;
        target.Focus(NavigationMethod.Directional);
        return true;
    }

    private InputElement? FocusSidebar() =>
        Sidebar.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(i => i.IsSelected)
        ?? Sidebar.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault();

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

    /// <summary>The footer button really quits (the title-bar X may only hide to the tray).</summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Services.AppLifecycle.RequestQuit();
}
