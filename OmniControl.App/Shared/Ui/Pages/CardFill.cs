using System.Collections.Specialized;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Pages;

/// THE way a card list (<c>ListBox.cards</c>, which cannot virtualize because <see cref="HalfPanel"/> pairs its cards) gets its cards:
/// given as <see cref="ItemsProperty"/> instead of ItemsSource, the first <see cref="FirstCards"/> are built with the page's first frame
/// and the rest follow in small batches below input (<see cref="ProgressiveFill{T}"/>), so a page with many cards shows at once. A source
/// that changes (an ObservableCollection) is followed while the list is on screen. Up/Down, Tab and revealing a card finish the
/// list first (<see cref="FinishWithin"/>), so a card not yet built is still reached.
public static class CardFill
{
    /// Cards built at once and per later batch: about what one window shows.
    public const int FirstCards = 8;

    public static readonly AttachedProperty<IReadOnlyList<object>?> ItemsProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, IReadOnlyList<object>?>("Items", typeof(CardFill));

    // The list's own fill, kept on the control (never in a static) so a let-go view holds nothing.
    private static readonly AttachedProperty<Fill?> FillProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, Fill?>("Fill", typeof(CardFill));

    public static IReadOnlyList<object>? GetItems(ItemsControl list) => list.GetValue(ItemsProperty);

    public static void SetItems(ItemsControl list, IReadOnlyList<object>? items) => list.SetValue(ItemsProperty, items);

    static CardFill()
    {
        ItemsProperty.Changed.AddClassHandler<ItemsControl>((list, e) =>
        {
            list.GetValue(FillProperty)?.Dispose();
            if (e.NewValue is not IReadOnlyList<object> items)
            {
                list.SetValue(FillProperty, null);
                list.ItemsSource = null;
                return;
            }
            list.SetValue(FillProperty, new Fill(list, items));
        });
        // Tab and Shift+Tab walk focus in layout order, so the cards still waiting are built first, as for Up/Down.
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((top, e) =>
        {
            if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift) FinishWithin(top);
        }, RoutingStrategies.Tunnel);
    }

    /// Builds at once every card still waiting in the card lists under <paramref name="host"/> (or <paramref name="host"/> itself) and
    /// lays them out; false when none was waiting.
    public static bool FinishWithin(Control host)
    {
        var any = false;
        foreach (var list in host.GetSelfAndVisualDescendants().OfType<ItemsControl>())
            any |= list.GetValue(FillProperty)?.Finish() == true;
        if (any) host.UpdateLayout();
        return any;
    }

    private sealed class Fill : IDisposable
    {
        private readonly ItemsControl _list;
        private readonly IReadOnlyList<object> _source;
        private readonly ProgressiveFill<object> _fill;

        public Fill(ItemsControl list, IReadOnlyList<object> source)
        {
            (_list, _source) = (list, source);
            var shown = new ObservableCollection<object>();
            list.ItemsSource = shown;
            _fill = new ProgressiveFill<object>(shown, card => card, null, FirstCards, FirstCards);
            _fill.Show(source);
            if (source is not INotifyCollectionChanged) return;
            list.AttachedToVisualTree += OnAttached;
            list.DetachedFromVisualTree += OnDetached;
            if (list.GetVisualRoot() is not null) Follow(true);
        }

        public bool Finish() => _fill.Finish();

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            Follow(true);
            _fill.Show(_source.ToList(), keepShown: true); // it may have changed while off screen
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Follow(false);

        // Followed only while on screen, so the view-model's collection never holds a let-go view.
        private void Follow(bool on)
        {
            var source = (INotifyCollectionChanged)_source;
            source.CollectionChanged -= OnChanged;
            if (on) source.CollectionChanged += OnChanged;
        }

        private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => _fill.Show(_source.ToList(), keepShown: true);

        public void Dispose()
        {
            if (_source is not INotifyCollectionChanged) return;
            Follow(false);
            _list.AttachedToVisualTree -= OnAttached;
            _list.DetachedFromVisualTree -= OnDetached;
        }
    }
}
