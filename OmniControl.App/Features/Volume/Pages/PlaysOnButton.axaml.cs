using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace OmniControl.App.Features.Volume.Pages;

/// Wiring only: the Plays on chip opens the row's Plays on menu (the page fills it as it opens); a pick or Esc closes it, and closing
/// puts the keyboard back on the chip. Mouse first, like the Wiring cell's menu.
public partial class PlaysOnButton : UserControl
{
    private PlaysOnMenuView? _menuView;

    public PlaysOnButton() => InitializeComponent();

    private void OnClick(object? sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(this);

    private void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: PlaysOnMenuView view } || DataContext is not VolumeRow { PlaysOn: { } menu }) return;
        if (_menuView is null)
        {
            // The menu's view lives as long as this chip, so the subscription needs no undoing.
            _menuView = view;
            view.Picked += Close;
        }
        view.DataContext = menu;
        menu.Open();
    }

    private void OnMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }

    private void Close() => FlyoutBase.GetAttachedFlyout(this)?.Hide();

    private void OnClosed(object? sender, EventArgs e) => Chip.Focus(NavigationMethod.Directional);
}
