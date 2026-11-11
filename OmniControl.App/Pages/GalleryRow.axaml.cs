using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages;

public partial class GalleryRow : UserControl
{
    public GalleryRow() => InitializeComponent();

    private void OnPrevious(object? sender, RoutedEventArgs e) => (DataContext as GalleryItem)?.Step(-1);
    private void OnNext(object? sender, RoutedEventArgs e) => (DataContext as GalleryItem)?.Step(1);
}
