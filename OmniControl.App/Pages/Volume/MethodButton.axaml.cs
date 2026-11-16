using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Volume;

/// <summary>Wiring only: a row's Method chip opens its pop-down (the page reads the cables and the engine again first; keys in <see cref="OptionPanelKeys"/>); closing puts focus back on the chip.</summary>
public partial class MethodButton : UserControl
{
    private OptionPanelKeys? _keys;

    public MethodButton() => InitializeComponent();

    private void OnClick(object? sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(this);

    private void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control panel } || DataContext is not VolumeRow { RouteChoice: { } choice } row) return;
        choice.Refresh?.Invoke();
        panel.DataContext = row;
        _keys?.Dispose();
        _keys = new OptionPanelKeys(panel, () => FlyoutBase.GetAttachedFlyout(this)?.Hide());
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _keys?.Dispose();
        _keys = null;
        Chip.Focus(NavigationMethod.Directional);
    }
}
