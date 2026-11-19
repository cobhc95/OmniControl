using Avalonia.Input;

namespace OmniControl.App.Features.Volume.Pages;

/// Wiring only: shows a <see cref="CombineDeviceViewModel"/> as a dialog and closes with true (Create or Save) or false (Cancel, Esc, the window's X).
public partial class CombineWindow : Window
{
    public CombineWindow()
    {
        InitializeComponent();
        MemberList.CloseRequested += () => Close(false);
        Opened += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll(); // a suggested name ("Combined microphone") is replaced by typing
        };
    }

    /// Shows the window over the main window and waits for the answer.
    public static Task<bool> AskAsync(CombineDeviceViewModel model) => OwnedWindow.ShowDialogAsync(new CombineWindow { DataContext = model });

    private void OnCreateClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
