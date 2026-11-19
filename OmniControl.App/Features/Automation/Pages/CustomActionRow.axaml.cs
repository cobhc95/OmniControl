namespace OmniControl.App.Features.Automation.Pages;

/// Wiring only for <see cref="CustomActionItem"/>: Run and Delete.
public partial class CustomActionRow : UserControl
{
    public CustomActionRow() => InitializeComponent();

    private async void OnRunClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CustomActionItem item) await item.RunAsync();
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CustomActionItem item) await item.DeleteAsync();
    }
}
