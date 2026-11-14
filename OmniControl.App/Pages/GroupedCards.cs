using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OmniControl.App.Pages;

/// <summary>
/// Keyboard model for a page made of several card lists and collapsible <see cref="CardGroup"/> panels:
/// Up/Down walk through group headers and visible cards in on-screen order; on a header Left collapses and Right expands;
/// everything inside a card (Left/Right between parts, Enter...) is the shared <see cref="RowEditing"/> model.
/// </summary>
public static class GroupedCards
{
    /// <param name="root">The page (receives the key events).</param>
    /// <param name="host">The panel holding the lists and group panels, in on-screen order.</param>
    public static void Attach(Control root, Control host)
    {
        root.AddHandler(InputElement.KeyDownEvent, (_, e) => OnKeyDown(root, host, e), RoutingStrategies.Tunnel);
    }

    private static List<Control> Stops(Control host) => host.GetVisualDescendants().OfType<Control>()
        .Where(c => IsHeaderStop(c) || (c is ListBoxItem && c.IsEffectivelyVisible)).ToList();

    /// <summary>
    /// A visible group header, or a drop-down or button placed on a header's line (the mouse layer, Refresh, a page's open tab, a switch): one stop
    /// each, in on-screen order. One that cannot take focus (a tab that is not open, a dimmed button) is not a stop, as a header inside
    /// a collapsed group is not.
    /// </summary>
    private static bool IsHeaderStop(Control c) =>
        (c is Button { Tag: "grouphdr" or "grouptool" } || c is ComboBox { Tag: "grouptool" } || c is ToggleSwitch { Tag: "grouptool" }) && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.Focusable;

    private static void OnKeyDown(Control root, Control host, KeyEventArgs e)
    {
        if (e.Source is not Control src) return;
        var focused = TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement();

        // Inside a card with its own sideways/vertical navigation (the equaliser) the card decides first.
        if ((src as ListBoxItem ?? src.FindAncestorOfType<ListBoxItem>()) is { } owner && RowEditing.TryNavigateInside(owner, src, e))
        {
            e.Handled = true;
            return;
        }

        // On a group header: Left collapses, Right expands.
        if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None
            && src is Button { Tag: "grouphdr", DataContext: CardGroup group })
        {
            group.IsExpanded = e.Key == Key.Right;
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.None && !RowEditing.IsEditing(focused)
            && src is not ComboBoxItem && src.FindAncestorOfType<ComboBoxItem>() is null)
        {
            var stops = Stops(host);
            Control? current = IsHeaderStop(src) ? src : src as ListBoxItem ?? src.FindAncestorOfType<ListBoxItem>();
            var at = current is null ? -1 : stops.IndexOf(current);
            if (at >= 0)
            {
                var next = Services.Wrap.Index(at + Services.Wrap.Direction(e.Key), stops.Count);
                if (next != at) MoveTo(stops[next], stops);
                e.Handled = true;
                return;
            }
        }

        if (src.FindAncestorOfType<ListBox>() is { } list) RowEditing.Handle(list, e);
    }

    /// <summary>
    /// Moves the keyboard to the card that shows <paramref name="item"/> (as Up/Down would) and scrolls it into view; false when no
    /// visible card shows it (its page or group is hidden).
    /// </summary>
    public static bool Reveal(Control host, object item)
    {
        var stops = Stops(host);
        if (stops.OfType<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.DataContext, item)) is not { } card) return false;
        MoveTo(card, stops);
        return true;
    }

    private static void MoveTo(Control stop, List<Control> stops)
    {
        if (stop is ListBoxItem card && card.FindAncestorOfType<ListBox>() is { } list)
        {
            // Only one card is selected across all the lists.
            foreach (var other in stops.OfType<ListBoxItem>().Select(i => i.FindAncestorOfType<ListBox>()).Distinct())
                if (other is not null && other != list) other.SelectedIndex = -1;
            list.SelectedIndex = list.IndexFromContainer(card);
            RowEditing.CardTarget(card).Focus(NavigationMethod.Directional);
        }
        else stop.Focus(NavigationMethod.Directional);

        // Scroll the page after layout: a card that was just revealed by expanding its group has no position yet when focus moves.
        Dispatcher.UIThread.Post(() => ScrollIntoView(stop), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Scrolls the page (the outermost ScrollViewer) just enough to show <paramref name="target"/>. BringIntoView is not enough here:
    /// each ListBox has its own ScrollViewer that swallows the request before the page sees it.
    /// </summary>
    private static void ScrollIntoView(Control target)
    {
        var page = target.GetVisualAncestors().OfType<ScrollViewer>().LastOrDefault();
        if (page is null || target.TranslatePoint(default, page) is not { } top) return;

        const double margin = 12;
        var bottom = top.Y + target.Bounds.Height;
        var viewport = page.Viewport.Height;
        var offset = page.Offset;

        if (top.Y < margin) page.Offset = new Vector(offset.X, Math.Max(0, offset.Y + top.Y - margin));
        else if (bottom > viewport - margin) page.Offset = new Vector(offset.X, offset.Y + bottom - viewport + margin);
    }
}
