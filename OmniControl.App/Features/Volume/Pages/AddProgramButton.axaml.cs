using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Volume.Pages;

/// Wiring only: opens the pop-down, gives it the view-model as it opens (a pop-down's content does not inherit the data context),
/// puts the keyboard in the search box, adds the chosen program, and runs the file picker for "Browse".
public partial class AddProgramButton : UserControl
{
    private Flyout? _menu;
    private Control? _opener;

    public AddProgramButton()
    {
        InitializeComponent();
        BrowseRow.ItemsSource = new[] { new MenuRowItem("Browse for a program…", string.Empty, false, string.Empty, new AsyncRelayCommand(BrowseAsync), RuleAbove: true) };
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        _opener = sender as Control;
        FlyoutBase.ShowAttachedFlyout(Holder);
    }

    private void OnMenuOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control panel } flyout || DataContext is not AddProgramMenuViewModel menu) return;
        _menu = flyout;
        panel.DataContext = menu;
        menu.Opened();
        Dispatcher.UIThread.Post(() => panel.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus(), DispatcherPriority.Loaded);
    }

    private void OnMenuClosed(object? sender, EventArgs e) => (DataContext as AddProgramMenuViewModel)?.Closed();

    // A program row was clicked: its own command adds it.
    private void OnPicked(object? sender, EventArgs e) => _menu?.Hide();

    /// Enter in the search box chooses the first match; Esc closes the pop-down and gives the keyboard back to the button.
    private void OnMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _menu?.Hide();
            _opener?.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.Source is TextBox && DataContext is AddProgramMenuViewModel { First: { } first } menu)
        {
            _menu?.Hide();
            menu.Add(first.Path);
            e.Handled = true;
        }
    }

    private async Task BrowseAsync()
    {
        _menu?.Hide();
        if (DataContext is not AddProgramMenuViewModel menu) return;
        if (await PathPicker.PickProgramAsync(this) is { } path) menu.Add(path);
    }
}
