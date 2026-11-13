using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Mouse;

/// <summary>The window behind the cogwheel beside the Layer drop-down: rename, switch to and clear layers. Wiring only; the state is in <see cref="LayerManagerModel"/>.</summary>
public partial class LayerManagerWindow : Window
{
    public LayerManagerWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
        Closed += (_, _) => (DataContext as LayerManagerModel)?.Dispose();
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close();

    public static Task ShowAsync(Window owner, LayerManagerModel model) =>
        new LayerManagerWindow { DataContext = model }.ShowDialog(owner);
}
