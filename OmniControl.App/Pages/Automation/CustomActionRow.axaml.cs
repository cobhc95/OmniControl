using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Automation;

/// <summary>Wiring only for <see cref="CustomActionItem"/>: Run and Delete.</summary>
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
