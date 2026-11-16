using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Devices;

/// <summary>Wiring only: the checklist's Router guide pill opens the guide pop-down, which is given the device when it opens (a pop-down's content does not inherit the data context).</summary>
public partial class DeviceDetail : UserControl
{
    private Flyout? _methodMenu;

    public DeviceDetail()
    {
        InitializeComponent();
        MethodRows.Picked += (_, _) => _methodMenu?.Hide();
    }

    private void OnMethodMenuOpened(object? sender, Flyout flyout)
    {
        _methodMenu = flyout;
        MethodRows.ItemsSource = (DataContext as DeviceItem)?.MethodOptions;
    }

    private void OnGuideClick(object? sender, RoutedEventArgs e) => FlyoutBase.GetAttachedFlyout(this)?.ShowAt((Control)sender!);

    private void OnGuideOpened(object? sender, EventArgs e)
    {
        if (sender is Flyout { Content: Control panel }) panel.DataContext = DataContext;
    }
}
