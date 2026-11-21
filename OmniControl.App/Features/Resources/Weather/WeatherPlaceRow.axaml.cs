using System.ComponentModel;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Resources.Weather;

/// Wiring only: opens the found places under the search box when they arrive and closes the menu after a pick.
public partial class WeatherPlaceRow : UserControl
{
    private WeatherPlaceItem? _item;

    public WeatherPlaceRow()
    {
        InitializeComponent();
        Results.Picked += (_, _) => (FlyoutBase.GetAttachedFlyout(Search) as Flyout)?.Hide();
        var styles = new MenuRowList();
        Flyout? open = null;
        styles.Picked += (_, _) => open?.Hide();
        NameStyle.Menu = styles;
        NameStyle.MenuOpened += (_, flyout) =>
        {
            open = flyout;
            styles.ItemsSource = _item?.NameStyleRows;
        };
        NameStyle.MenuClosed += (_, _) => open = null;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Follow();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Follow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Follow();
    }

    // The item is heard only while the row is on screen, so a view let go (a released main window) is never held by its model.
    private void Follow()
    {
        var item = this.IsAttachedToVisualTree() ? DataContext as WeatherPlaceItem : null;
        if (item == _item) return;
        if (_item is not null) _item.PropertyChanged -= OnItemChanged;
        _item = item;
        if (_item is not null) _item.PropertyChanged += OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WeatherPlaceItem.Results) || _item is null || !Search.IsEffectivelyVisible) return;
        Results.ItemsSource = _item.Results;
        FlyoutBase.ShowAttachedFlyout(Search);
    }
}
