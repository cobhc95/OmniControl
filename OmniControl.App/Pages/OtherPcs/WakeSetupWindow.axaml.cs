using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.OtherPcs;

/// <summary>Wiring only: shows a <see cref="WakeSetupViewModel"/>; Esc (the dialog style) and Close shut it.</summary>
public partial class WakeSetupWindow : Window
{
    public WakeSetupWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
