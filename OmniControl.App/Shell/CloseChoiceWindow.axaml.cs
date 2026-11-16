using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniControl.App.Services;

namespace OmniControl.App.Shell;

/// <summary>What the user picked in <see cref="CloseChoiceWindow"/>.</summary>
public sealed record CloseChoice(CloseBehavior Behavior, bool Remember);

/// <summary>Wiring only: asks whether to exit or minimise to the tray. Closes with a <see cref="CloseChoice"/>, or null for Esc / the window's X.</summary>
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

    private void OnTrayClick(object? sender, RoutedEventArgs e) => Close(new CloseChoice(CloseBehavior.MinimizeToTray, RememberSwitch.IsChecked == true));
}
