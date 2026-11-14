using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Mouse;

public partial class MousePage : UserControl
{
    public MousePage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
    }

    private void OnManageMouseLayers(object? sender, RoutedEventArgs e) => ShowLayerManager(page => page.CreateMouseLayerManager());

    private void OnManageTrackpadLayers(object? sender, RoutedEventArgs e) => ShowLayerManager(page => page.CreateTrackpadLayerManager());

    private async void ShowLayerManager(Func<MousePageViewModel, LayerManagerModel> create)
    {
        if (DataContext is MousePageViewModel page && TopLevel.GetTopLevel(this) is Window owner)
            await LayerManagerWindow.ShowAsync(owner, create(page));
    }
}
