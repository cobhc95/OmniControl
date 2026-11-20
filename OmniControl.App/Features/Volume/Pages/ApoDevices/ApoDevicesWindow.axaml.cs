using Avalonia.Input;

namespace OmniControl.App.Features.Volume.Pages.ApoDevices;

/// Wiring only: shows an <see cref="ApoDevicesViewModel"/>; Up/Down and Space go to it, Esc (the dialog style) and Close shut it.
public partial class ApoDevicesWindow : Window
{
    private Flyout? _modeMenu;

    public ApoDevicesWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    private ApoDevicesViewModel? Model => DataContext as ApoDevicesViewModel;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // a focused button or tick box (footer, options) keeps its own Space and arrows
        if (e.Handled || e.Source is Button || Model is not { } model || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key is Key.Up or Key.Down) model.Move(Wrap.Direction(e.Key));
        else if (e.Key == Key.Space) model.ToggleSelected();
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
