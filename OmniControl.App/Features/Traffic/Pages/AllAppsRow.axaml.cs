namespace OmniControl.App.Features.Traffic.Pages;

/// Wiring only: fills a limit chip's menu with the All apps row's choices when it opens and closes it after a pick; hands the switch's click to the row.
public partial class AllAppsRow : UserControl
{
    public AllAppsRow()
    {
        InitializeComponent();
        UploadMenu.Picked += (_, _) => UploadChip.Flyout?.Hide();
        DownloadMenu.Picked += (_, _) => DownloadChip.Flyout?.Hide();
    }

    private void OnUploadOpened(object? sender, EventArgs e) => UploadMenu.ItemsSource = (DataContext as AllAppsItem)?.UploadRows();

    /// The click already stepped the switch on its own; the row decides the new state and the switch is set to it.
    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AllAppsItem item) return;
        item.ToggleCommand.Execute(null);
        OnSwitch.SetCurrentValue(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty, item.IsOn); // keeps the binding
    }

    private void OnDownloadOpened(object? sender, EventArgs e) => DownloadMenu.ItemsSource = (DataContext as AllAppsItem)?.DownloadRows();
}
