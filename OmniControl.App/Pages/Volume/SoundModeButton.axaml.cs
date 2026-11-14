using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Volume;

/// <summary>Wiring only: the "Sound mode" button (Mixer and Wiring tabs) opens its panel, its badge the setup table (keys in <see cref="OptionPanelKeys"/>); closing puts focus back on the button.</summary>
public partial class SoundModeButton : UserControl
{
    private OptionPanelKeys? _keys;

    public SoundModeButton() => InitializeComponent();

    private void OnClick(object? sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(this);

    /// <summary>The badge opens the setup table instead of the panel (handled here, so the button underneath does not press).</summary>
    private void OnBadgePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not SoundModeChoice choice) return;
        e.Handled = true;
        choice.ShowTableCommand.Execute(null);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control panel } || DataContext is not SoundModeChoice choice) return;
        panel.DataContext = choice;
        choice.IsOpen = true;
        _keys?.Dispose();
        _keys = new OptionPanelKeys(panel, () => FlyoutBase.GetAttachedFlyout(this)?.Hide());
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _keys?.Dispose();
        _keys = null;
        if (DataContext is SoundModeChoice choice) choice.IsOpen = false;
        ModeButton.Focus(NavigationMethod.Directional);
    }
}
