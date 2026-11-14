using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Volume;

/// <summary>Wiring only: shows a <see cref="CombineDeviceViewModel"/> as a dialog and closes with true (Create or Save) or false (Cancel, Esc, the window's X).</summary>
public partial class CombineWindow : Window
{
    public CombineWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { Close(false); e.Handled = true; } }, RoutingStrategies.Tunnel);
        MemberList.CloseRequested += () => Close(false);
        Opened += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll(); // a suggested name ("Combined microphone") is replaced by typing
        };
    }

    /// <summary>Shows the window over the main window and waits for the answer.</summary>
    public static Task<bool> AskAsync(CombineDeviceViewModel model) => OwnedWindow.ShowDialogAsync(new CombineWindow { DataContext = model });

    private void OnCreateClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
