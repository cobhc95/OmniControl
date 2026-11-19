using Avalonia.Input;

namespace OmniControl.App.Features.Displays.Pages;

public partial class DisplaysPage : UserControl
{
    public DisplaysPage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F5) return;
        Refresh();
        e.Handled = true;
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Refresh();

    private void Refresh() => (DataContext as DisplaysPageViewModel)?.ReloadCommand.Execute(null);
}
