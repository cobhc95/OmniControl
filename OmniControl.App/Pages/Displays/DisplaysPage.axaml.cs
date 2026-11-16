using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Displays;

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
