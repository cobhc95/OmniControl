
namespace OmniControl.App.Features.Volume.Pages;

public partial class VolumeRowView : UserControl
{
    private readonly PointerMenu _listenMenu = new();

    public VolumeRowView() => InitializeComponent();

    /// A right-click (or the menu key) on a microphone's Listen button: the shared menu of outputs it can play on.
    private void OnListenContext(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Button button || DataContext is not InputItem { ListenMenu: { } rows }) return;
        _listenMenu.Show(button, rows());
        e.Handled = true;
    }
}
