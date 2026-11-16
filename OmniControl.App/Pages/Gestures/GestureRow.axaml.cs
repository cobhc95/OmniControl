using Avalonia.Controls;
using Avalonia.Interactivity;
using OmniControl.App.Picker;

namespace OmniControl.App.Pages.Gestures;

/// <summary>One gesture's half-width card. Its pill opens the shared action window, its cogwheel opens that window on the action's settings.</summary>
public partial class GestureRow : UserControl
{
    public GestureRow() => InitializeComponent();

    private async void OnPickerClick(object? sender, RoutedEventArgs e) => await PickerWindow.ChooseForAsync(this, startInSettings: false, Picker, Picker);

    private async void OnCogClick(object? sender, RoutedEventArgs e) => await PickerWindow.ChooseForAsync(this, startInSettings: true, CogPart.Opener, Picker);
}
