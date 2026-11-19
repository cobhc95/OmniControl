namespace OmniControl.App.Features.OtherPcs.Pages;

/// Wiring only: shows a <see cref="WakeSetupViewModel"/>; Esc (the dialog style) and Close shut it.
public partial class WakeSetupWindow : Window
{
    public WakeSetupWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
