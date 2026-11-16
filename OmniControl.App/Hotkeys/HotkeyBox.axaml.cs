using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Hotkeys;

/// <summary>Wiring only for the shared hotkey capture box; the keys it takes are <see cref="HotkeyCapture"/>'s.</summary>
public partial class HotkeyBox : UserControl
{
    public HotkeyBox() => InitializeComponent();

    /// <summary>A double-click is the same as pressing Enter on the box: start listening for keys.</summary>
    private void OnHotkeyDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TextBox { DataContext: HotkeyTarget target } box) return;
        box.Focus(NavigationMethod.Directional);
        target.IsListening = true;
        e.Handled = true;
    }

    /// <summary>Releasing the main key takes the shortcut (see <see cref="HotkeyCapture.TryHandleUp"/>).</summary>
    private void OnHotkeyKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is Control { DataContext: HotkeyTarget target } && HotkeyCapture.TryHandleUp(e, target)) e.Handled = true;
    }

    /// <summary>Leaving the box (Tab, clicking elsewhere) cancels listening.</summary>
    private void OnHotkeyLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: HotkeyTarget target }) target.IsListening = false;
    }
}
