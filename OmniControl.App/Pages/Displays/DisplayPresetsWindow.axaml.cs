using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Displays;

/// <summary>Wiring only: shows a <see cref="DisplayPresetsModel"/> over the main window; Esc and Close close it.</summary>
public partial class DisplayPresetsWindow : Window
{
    public DisplayPresetsWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } }, RoutingStrategies.Tunnel);
        Opened += (_, _) => NameBox.Focus();
        Closed += (_, _) => (DataContext as DisplayPresetsModel)?.Dispose();
    }

    /// <summary>Shows the window over the main window and waits until it is closed.</summary>
    public static Task ShowAsync(DisplayPresetsModel model) => OwnedWindow.ShowDialogAsync(new DisplayPresetsWindow { DataContext = model });

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
