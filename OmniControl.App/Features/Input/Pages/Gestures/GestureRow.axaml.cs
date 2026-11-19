using OmniControl.App.Features.Hotkeys.Picker;

namespace OmniControl.App.Features.Input.Pages.Gestures;

/// One gesture's half-width card. Its pill opens the shared action window, its cogwheel opens that window on the action's settings.
public partial class GestureRow : UserControl
{
    public GestureRow() => InitializeComponent();

    private async void OnPickerClick(object? sender, RoutedEventArgs e) => await PickerWindow.ChooseForAsync(this, startInSettings: false, Picker, Picker);

    private async void OnCogClick(object? sender, RoutedEventArgs e) => await PickerWindow.ChooseForAsync(this, startInSettings: true, CogPart.Opener, Picker);
}
