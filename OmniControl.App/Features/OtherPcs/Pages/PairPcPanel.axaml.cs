namespace OmniControl.App.Features.OtherPcs.Pages;

/// Wiring only: the online-PCs menu is given its rows when it opens and closes after a pick.
public partial class PairPcPanel : UserControl
{
    private Flyout? _peerMenu;

    public PairPcPanel()
    {
        InitializeComponent();
        PeerList.Picked += (_, _) => _peerMenu?.Hide();
    }

    private void OnPeerMenuOpened(object? sender, Flyout flyout)
    {
        _peerMenu = flyout;
        PeerList.ItemsSource = (DataContext as SetupTabItem)?.PeerRows;
    }
}
