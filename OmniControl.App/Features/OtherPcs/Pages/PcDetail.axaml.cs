using Avalonia.Input;

namespace OmniControl.App.Features.OtherPcs.Pages;

/// Wiring only: the Connect menu and the "..." menu are given their rows when they open and close after a pick (a pop-down does not inherit the data context); the address box applies when it is left or Enter is pressed.
public partial class PcDetail : UserControl
{
    private Flyout? _connectMenu;
    private Flyout? _moreMenu;

    public PcDetail()
    {
        InitializeComponent();
        ConnectRows.Picked += (_, _) => _connectMenu?.Hide();
        MoreRows.Picked += (_, _) => _moreMenu?.Hide();
    }

    private void OnConnectMenuOpened(object? sender, Flyout flyout)
    {
        _connectMenu = flyout;
        ConnectRows.ItemsSource = (DataContext as PcItem)?.ConnectRows;
    }

    private void OnMoreOpening(object? sender, EventArgs e)
    {
        _moreMenu = sender as Flyout;
        MoreRows.ItemsSource = (DataContext as PcItem)?.MenuRows;
    }

    private void OnAddressLeft(object? sender, RoutedEventArgs e) => (DataContext as PcItem)?.ApplyAddress();

    private void OnAddressKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        (DataContext as PcItem)?.ApplyAddress();
        e.Handled = true;
    }
}
