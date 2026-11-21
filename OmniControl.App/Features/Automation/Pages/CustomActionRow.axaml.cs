namespace OmniControl.App.Features.Automation.Pages;

/// Wiring only for <see cref="CustomActionItem"/>: Run and Delete.
public partial class CustomActionRow : UserControl
{
    public CustomActionRow() => InitializeComponent();

    private void OnRunClick(object? sender, RoutedEventArgs e) => Guard.Fire("Automation: run a custom action", async () =>
    {
        if (DataContext is CustomActionItem item) await item.RunAsync();
    });

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => Guard.Fire("Automation: delete a custom action", async () =>
    {
        if (DataContext is CustomActionItem item) await item.DeleteAsync();
    });
}
