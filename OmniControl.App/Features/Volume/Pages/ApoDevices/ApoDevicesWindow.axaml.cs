using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Features.Volume.Pages.ApoDevices;

/// Wiring only: shows an <see cref="ApoDevicesViewModel"/>; Up/Down and Enter go to it, Esc closes an open confirmation first (seen
/// before the dialog style's Esc closes the window), Close shuts it.
public partial class ApoDevicesWindow : Window
{
    private Flyout? _modeMenu;

    public ApoDevicesWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private ApoDevicesViewModel? Model => DataContext as ApoDevicesViewModel;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Model is not { } model || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = model.CancelConfirm();
            return;
        }
        // a focused button or tick box (row actions, footer, options) keeps its own Enter, Space and arrows
        if (e.Source is Button or CheckBox) return;
        if (e.Key is Key.Up or Key.Down) model.Move(Wrap.Direction(e.Key));
        else if (e.Key == Key.Enter) model.ActOnSelected();
        else return;
        e.Handled = true;
    }

    private void OnModeMenuOpened(object? sender, Flyout menu)
    {
        if (sender is not ChipMenu { Menu: MenuRowList rows } || Model is not { } model) return;
        _modeMenu = menu;
        rows.Picked -= OnModePicked;
        rows.Picked += OnModePicked;
        rows.ItemsSource = model.ModeRows();
    }

    private void OnModePicked(object? sender, EventArgs e) => _modeMenu?.Hide();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
