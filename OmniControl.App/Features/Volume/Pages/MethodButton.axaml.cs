using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace OmniControl.App.Features.Volume.Pages;

/// Wiring only: a row's Method chip opens its pop-down (the page reads the cables and the engine again first; keys in <see cref="OptionPanelKeys"/>); closing puts focus back on the chip.
/// It also opens when the row asks (<see cref="VolumeRow.MethodMenuRequested"/>), followed only while attached so a released view is never held.
public partial class MethodButton : UserControl
{
    private OptionPanelKeys? _keys;
    private VolumeRow? _row;
    private bool _attached;

    public MethodButton() => InitializeComponent();

    private void OnClick(object? sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Follow(DataContext as VolumeRow);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Follow(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached) Follow(DataContext as VolumeRow);
    }

    private void Follow(VolumeRow? row)
    {
        if (_row == row) return;
        if (_row is not null) _row.MethodMenuRequested -= OnMenuRequested;
        _row = row;
        if (row is not null) row.MethodMenuRequested += OnMenuRequested;
    }

    /// Only the chip on screen opens (a hidden tab's or page's view stays shut).
    private void OnMenuRequested()
    {
        if (_row is { HasMethodMenu: true } && Chip.IsEffectivelyVisible) FlyoutBase.ShowAttachedFlyout(this);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control panel } || DataContext is not VolumeRow { RouteChoice: { } choice } row) return;
        choice.Refresh?.Invoke();
        panel.DataContext = row;
        _keys?.Dispose();
        _keys = new OptionPanelKeys(panel, () => FlyoutBase.GetAttachedFlyout(this)?.Hide());
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _keys?.Dispose();
        _keys = null;
        Chip.Focus(NavigationMethod.Directional);
    }
}
