using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace OmniControl.App.Hotkeys;

public partial class HotkeyRow : UserControl
{
    private Flyout? _flyout;
    private bool _multi;

    public HotkeyRow() => InitializeComponent();

    /// <summary>Double-click is the same as pressing Enter on the box: start listening for keys.</summary>
    private void OnHotkeyDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TextBox { DataContext: HotkeyAction action } box) return;
        box.Focus(NavigationMethod.Directional);
        action.IsListening = true;
        e.Handled = true;
    }

    /// <summary>Leaving the box (Tab, clicking elsewhere) cancels listening.</summary>
    private void OnHotkeyLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: HotkeyAction action }) action.IsListening = false;
    }

    /// <summary>
    /// The device list opens on its first ticked option. Up/Down move, Space ticks (and lets you tick more),
    /// Enter picks the highlighted option (just that one, unless Space was used) and closes the list.
    /// </summary>
    private void OnFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control panel } flyout) return;
        _flyout = flyout;
        _multi = false;
        panel.DataContext ??= flyout.Target?.DataContext;

        var chips = Chips(panel);
        (chips.FirstOrDefault(c => c.IsChecked == true) ?? chips.FirstOrDefault())?.Focus(NavigationMethod.Directional);

        panel.RemoveHandler(KeyDownEvent, OnPanelKeyDown);
        panel.AddHandler(KeyDownEvent, OnPanelKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Back on the device box once the list closes.</summary>
    private void OnFlyoutClosed(object? sender, EventArgs e) =>
        (sender as Flyout)?.Target?.Focus(NavigationMethod.Directional);

    private static List<ToggleButton> Chips(Control panel) =>
        panel.GetVisualDescendants().OfType<ToggleButton>().ToList();

    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control panel) return;
        var chips = Chips(panel);
        var i = chips.FindIndex(c => c.IsFocused);

        switch (e.Key)
        {
            case Key.Up or Key.Down:
                var from = i < 0 && e.Key == Key.Up ? 0 : i; // nothing focused yet: Down lands on the first, Up on the last
                if (chips.Count > 0) chips[Services.Wrap.Index(from + Services.Wrap.Direction(e.Key), chips.Count)].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;

            case Key.Space:
                _multi = true; // the toggle itself is done by the button
                break;

            case Key.Enter:
                if (i >= 0 && chips[i].DataContext is ScopeChip chip && DataContext is HotkeyAction action)
                {
                    if (!_multi) action.SelectOnly(chip);
                    else if (!chip.IsOn) chip.IsOn = true;
                }
                _flyout?.Hide();
                e.Handled = true;
                break;
        }
    }
}
