namespace OmniControl.App.Features.Devices.Pages;

/// Wiring only: the Method, Template and Auto-check menus are given its rows when it opens (a pop-down's content does not inherit the data context) and closes after a pick; Copy details copies the last command's masked log lines.
public partial class DeviceDetail : UserControl
{
    private Flyout? _methodMenu, _autoCheckMenu, _templateMenu;

    public DeviceDetail()
    {
        InitializeComponent();
        MethodRows.Picked += (_, _) => _methodMenu?.Hide();
        TemplateRows.Picked += (_, _) => _templateMenu?.Hide();
        AutoCheckRows.Picked += (_, _) => _autoCheckMenu?.Hide();
    }

    private void OnAutoCheckMenuOpened(object? sender, Flyout flyout)
    {
        _autoCheckMenu = flyout;
        AutoCheckRows.ItemsSource = (DataContext as DeviceItem)?.AutoCheckOptions;
    }

    private void OnTemplateMenuOpened(object? sender, Flyout flyout)
    {
        _templateMenu = flyout;
        TemplateRows.ItemsSource = (DataContext as DeviceItem)?.Editing?.ProfileOptions;
    }

    private void OnMethodMenuOpened(object? sender, Flyout flyout)
    {
        _methodMenu = flyout;
        MethodRows.ItemsSource = (DataContext as DeviceItem)?.Editing?.MethodOptions;
    }

    private async void OnCopyDetails(object? sender, RoutedEventArgs e) => await CopyDetails.ToClipboardAsync(this, (DataContext as DeviceItem)?.Details);
}
