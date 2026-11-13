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

    private async void OnManageLayers(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MousePageViewModel page && TopLevel.GetTopLevel(this) is Window owner)
            await LayerManagerWindow.ShowAsync(owner, page.CreateLayerManager());
    }
}
