using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Hotkeys;

/// <summary>Wiring only for the shared hotkey capture box; the keys it takes are <see cref="HotkeyCapture"/>'s.</summary>
public partial class HotkeyBox : UserControl
{
    public HotkeyBox() => InitializeComponent();

    /// <summary>A click is the same as pressing Enter on the box: start listening for keys.</summary>
    private void OnHotkeyTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TextBox { DataContext: HotkeyAction action } box) return;
        box.Focus(NavigationMethod.Directional);
        action.IsListening = true;
        e.Handled = true;
    }

    /// <summary>Releasing the main key saves the shortcut (see <see cref="HotkeyCapture.TryHandleUp"/>).</summary>
    private void OnHotkeyKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is Control { DataContext: HotkeyAction action } && HotkeyCapture.TryHandleUp(e, action)) e.Handled = true;
    }

    /// <summary>Leaving the box (Tab, clicking elsewhere) cancels listening.</summary>
    private void OnHotkeyLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: HotkeyAction action }) action.IsListening = false;
    }
}
