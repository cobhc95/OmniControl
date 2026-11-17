using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniControl.App.Controls;

namespace OmniControl.App.Pages.OtherPcs;

/// <summary>Wiring only: the Connect menu is given its rows when it opens and closes after a pick; the address box applies when it is left or Enter is pressed.</summary>
public partial class PcDetail : UserControl
{
    private Flyout? _menu;

    public PcDetail()
    {
        InitializeComponent();
        ConnectRows.Picked += (_, _) => _menu?.Hide();
    }

    private void OnConnectMenuOpened(object? sender, Flyout flyout)
    {
        _menu = flyout;
        ConnectRows.ItemsSource = (DataContext as PcItem)?.ConnectRows;
    }

    private void OnAddressLeft(object? sender, RoutedEventArgs e) => (DataContext as PcItem)?.ApplyAddress();

    private void OnAddressKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        (DataContext as PcItem)?.ApplyAddress();
        e.Handled = true;
    }
}
