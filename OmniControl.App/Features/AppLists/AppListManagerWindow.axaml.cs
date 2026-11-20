using System.ComponentModel;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace OmniControl.App.Features.AppLists;

/// Wiring only for <see cref="AppListManagerViewModel"/>: the pop-downs get the view-model as they open (a pop-down's content does not
/// inherit the data context) and the view-model fills their lists; the name box gets the keyboard when renaming starts (Enter or leaving
/// it applies, Esc cancels); the program file is picked here. Esc closes the window.
public partial class AppListManagerWindow : Window
{
    private AppListManagerViewModel? _vm;

    public AppListManagerWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelChanged;
        _vm = DataContext as AppListManagerViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnViewModelChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        DataContext = null; // lets go of the view-model's events
        base.OnClosed(e);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppListManagerViewModel.IsRenaming) || _vm is not { IsRenaming: true }) return;
        Dispatcher.UIThread.Post(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    // ---- renaming ----

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _vm?.CommitRename();
        else if (e.Key == Key.Escape) _vm?.CancelRename();
        else return;
        e.Handled = true;
    }

    private void OnNameLostFocus(object? sender, RoutedEventArgs e) => _vm?.CommitRename();

    // ---- adding apps ----

    private void OnRunningMenuClick(object? sender, RoutedEventArgs e)
    {
        FlyoutBase.ShowAttachedFlyout(RunningHolder);
        _ = _vm?.RefreshRunningAsync();
    }

    private void OnInstalledMenuClick(object? sender, RoutedEventArgs e)
    {
        FlyoutBase.ShowAttachedFlyout(InstalledHolder);
        _ = _vm?.RefreshInstalledAsync();
    }

    private void OnPopDownOpened(object? sender, EventArgs e)
    {
        if (sender is Flyout { Content: Control panel }) panel.DataContext = _vm;
    }

    // A row was clicked (its own command adds the program).
    private void OnRunningPicked(object? sender, EventArgs e) => FlyoutBase.GetAttachedFlyout(RunningHolder)?.Hide();

    private void OnInstalledPicked(object? sender, EventArgs e) => FlyoutBase.GetAttachedFlyout(InstalledHolder)?.Hide();

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (await PathPicker.PickProgramAsync(this) is { } path) _vm?.AddFile(path);
    }
}
