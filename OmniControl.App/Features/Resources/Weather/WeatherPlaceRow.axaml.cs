using System.ComponentModel;
using Avalonia.Controls.Primitives;

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
        if (_item is not null) _item.PropertyChanged -= OnItemChanged;
        _item = DataContext as WeatherPlaceItem;
        if (_item is not null) _item.PropertyChanged += OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WeatherPlaceItem.Results) || _item is null || !Search.IsEffectivelyVisible) return;
        Results.ItemsSource = _item.Results;
        FlyoutBase.ShowAttachedFlyout(Search);
    }
}
