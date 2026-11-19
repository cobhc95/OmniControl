using Avalonia.Controls.Primitives;

namespace OmniControl.App.Features.Volume.Pages;

public partial class VolumeRowView : UserControl
{
    private Flyout? _listenMenu;
    private MenuRowList? _listenRows;

    public VolumeRowView() => InitializeComponent();

    /// A right-click (or the menu key) on a microphone's Listen button: the shared menu of outputs it can play on.
    private void OnListenContext(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Button button || DataContext is not InputItem { ListenMenu: { } rows }) return;
        if (_listenMenu is null)
        {
            _listenRows = new MenuRowList();
            _listenRows.Picked += (_, _) => _listenMenu?.Hide();
            _listenMenu = new ListMenuFlyout { Placement = PlacementMode.Pointer, Content = _listenRows };
        }
        _listenRows!.ItemsSource = rows();
        _listenMenu.ShowAt(button, showAtPointer: true);
        e.Handled = true;
    }
}
