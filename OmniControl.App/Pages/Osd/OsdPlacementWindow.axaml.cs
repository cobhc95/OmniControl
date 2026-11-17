using Avalonia.Controls;
using Avalonia.Interactivity;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Osd;

/// <summary>Wiring only: shows an <see cref="OsdPlacementViewModel"/> as a dialog and closes with true (OK) or false (Cancel, Esc, the window's X).</summary>
public partial class OsdPlacementWindow : Window
{
    public OsdPlacementWindow()
    {
        InitializeComponent();
        RowEditing.Attach(CardList);
    }

    /// <summary>Shows the window over the main window and waits for the answer.</summary>
    public static Task<bool> AskAsync(OsdPlacementViewModel model) => OwnedWindow.ShowDialogAsync(new OsdPlacementWindow { DataContext = model });

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
