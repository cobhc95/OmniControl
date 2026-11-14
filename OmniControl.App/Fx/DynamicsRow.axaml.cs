using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Fx;

/// <summary>Wiring only: the meters are read while the card is on screen (<see cref="DynamicsItem.StartReading"/>), never in the background.</summary>
public partial class DynamicsRow : UserControl
{
    private DynamicsItem? _reading;

    public DynamicsRow() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Follow(DataContext as DynamicsItem);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        Follow(null);
        base.OnUnloaded(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (IsLoaded) Follow(DataContext as DynamicsItem);
    }

    private void Follow(DynamicsItem? item)
    {
        if (ReferenceEquals(_reading, item)) return;
        _reading?.StopReading();
        _reading = item;
        item?.StartReading();
    }
}
