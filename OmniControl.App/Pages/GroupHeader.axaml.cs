using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages;

/// <summary>The clickable header of a <see cref="CardGroup"/> panel: chevron, name, count.</summary>
public partial class GroupHeader : UserControl
{
    public GroupHeader() => InitializeComponent();

    private void OnClick(object? sender, RoutedEventArgs e) => (DataContext as CardGroup)?.Toggle();
}
