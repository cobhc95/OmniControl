using OmniControl.App.Picker;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace OmniControl.App.Pages.Mouse;

/// <summary>
/// One mouse button's half-width card. Its pill opens <see cref="PickerWindow"/>, its cogwheel opens that window on the action's
/// settings; what the window returns becomes the button's action and settings.
/// </summary>
public partial class MouseButtonRow : UserControl
{
    public MouseButtonRow()
    {
        InitializeComponent();
        ParameterBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            // Moving focus to the card makes the box commit its text.
            this.FindAncestorOfType<ListBoxItem>()?.Focus();
            e.Handled = true;
        };
    }

    private async void OnPickerClick(object? sender, RoutedEventArgs e) => await PickerWindow.ChooseForAsync(this, startInSettings: false, Picker, Picker);

    private async void OnCogClick(object? sender, RoutedEventArgs e) => await PickerWindow.ChooseForAsync(this, startInSettings: true, Cog, Picker);
}
