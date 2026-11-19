using Avalonia.Input;

namespace OmniControl.App.Features.Hotkeys;

/// Wiring only for the shared hotkey capture box; the keys it takes are <see cref="HotkeyCapture"/>'s.
public partial class HotkeyBox : UserControl
{
    public HotkeyBox() => InitializeComponent();

    /// A double-click is the same as pressing Enter on the box: start listening for keys.
    private void OnHotkeyDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TextBox { DataContext: HotkeyTarget target } box) return;
        box.Focus(NavigationMethod.Directional);
        target.IsListening = true;
        e.Handled = true;
    }

    /// Releasing the main key takes the shortcut (see <see cref="HotkeyCapture.TryHandleUp"/>).
    private void OnHotkeyKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is Control { DataContext: HotkeyTarget target } && HotkeyCapture.TryHandleUp(e, target)) e.Handled = true;
    }

    /// Leaving the box (Tab, clicking elsewhere) cancels listening.
    private void OnHotkeyLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: HotkeyTarget target }) target.IsListening = false;
    }
}
