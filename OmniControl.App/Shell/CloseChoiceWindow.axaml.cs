using Avalonia.Input;

namespace OmniControl.App.Shell;

/// What the user picked in <see cref="CloseChoiceWindow"/>.
public sealed record CloseChoice(CloseBehavior Behavior, bool Remember);

/// Wiring only: asks whether to exit or minimise to the tray. Closes with a <see cref="CloseChoice"/>, or null for Esc / the window's X.
public partial class CloseChoiceWindow : Window
{
    public CloseChoiceWindow()
    {
        InitializeComponent();
        ExitButton.Content = CloseBehaviors.LabelOf(CloseBehavior.Exit);
        TrayButton.Content = CloseBehaviors.LabelOf(CloseBehavior.MinimizeToTray);
        Opened += (_, _) => TrayButton.Focus();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close(new CloseChoice(CloseBehavior.Exit, RememberSwitch.IsChecked == true));

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnTrayClick(object? sender, RoutedEventArgs e) => Close(new CloseChoice(CloseBehavior.MinimizeToTray, RememberSwitch.IsChecked == true));
}
