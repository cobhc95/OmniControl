using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Volume;

/// <summary>Wiring only: a row's "Method" button opens its pop-down (keys in <see cref="OptionPanelKeys"/>); closing puts focus back on the button.</summary>
public partial class RouteButton : UserControl
{
    private OptionPanelKeys? _keys;

    public RouteButton() => InitializeComponent();

    private void OnClick(object? sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(this);

    private void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control panel } || DataContext is not RouteChoice choice) return;
        choice.Refresh?.Invoke();
        panel.DataContext = choice;
        _keys?.Dispose();
        _keys = new OptionPanelKeys(panel, () => FlyoutBase.GetAttachedFlyout(this)?.Hide());
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _keys?.Dispose();
        _keys = null;
        MethodButton.Focus(NavigationMethod.Directional);
    }
}
