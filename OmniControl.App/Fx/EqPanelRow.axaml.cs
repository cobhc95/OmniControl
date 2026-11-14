using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Fx;

public partial class EqPanelRow : UserControl
{
    public EqPanelRow()
    {
        InitializeComponent();
        // Tunnelling, so the list's keys win over the focused row button's own Space and Enter.
        BandList.AddHandler(KeyDownEvent, OnListKey, RoutingStrategies.Tunnel);
    }

    private void OnListKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is EqPanelItem item && item.ListKey(e.Key)) e.Handled = true;
    }
}
