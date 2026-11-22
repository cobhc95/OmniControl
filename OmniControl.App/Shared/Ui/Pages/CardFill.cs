namespace OmniControl.App.Shared.Ui.Pages;

/// The cards of a card list (<c>ListBox.cards</c>, which cannot virtualize because <see cref="HalfPanel"/> pairs its cards) given as
/// <see cref="ItemsProperty"/> instead of ItemsSource: the first <see cref="FirstCards"/> are built with the page's first frame, the rest
/// follow in small batches after it (<see cref="ProgressiveFill{T}"/>), so a page with many cards shows at once. Keyboard walking
/// builds nothing extra: the batches finish within a few frames.
public static class CardFill
{
    /// Cards built at once and per later batch: about what one window shows.
    public const int FirstCards = 8;

    public static readonly AttachedProperty<IReadOnlyList<object>?> ItemsProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, IReadOnlyList<object>?>("Items", typeof(CardFill));

    public static IReadOnlyList<object>? GetItems(ItemsControl list) => list.GetValue(ItemsProperty);

    public static void SetItems(ItemsControl list, IReadOnlyList<object>? items) => list.SetValue(ItemsProperty, items);

    static CardFill() => ItemsProperty.Changed.AddClassHandler<ItemsControl>((list, e) =>
    {
        if (e.NewValue is not IReadOnlyList<object> items)
        {
            list.ItemsSource = null;
            return;
        }
        var shown = new ObservableCollection<object>();
        list.ItemsSource = shown;
        new ProgressiveFill<object>(shown, card => card, null, FirstCards, FirstCards).Show(items);
    });
}
