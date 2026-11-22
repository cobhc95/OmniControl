using Avalonia.Input;

namespace OmniControl.App.Features.Volume.Fx.Plugins;

/// Wiring only. Keys: type to search, Down goes to the list, Enter or double-click adds, Esc cancels.
/// The window closes with the chosen <see cref="PluginEntry"/>, or null.
public partial class AddEffectWindow : Window
{
    public AddEffectWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        ResultList.DoubleTapped += (_, _) => Commit();
        Opened += (_, _) => SearchBox.Focus();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not AddEffectViewModel vm) return;
            vm.Chosen += entry => Close(entry);
            // The selection is set right after the results change, so it scrolls into view through ListScroll.
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AddEffectViewModel.Selected) && vm.Selected is not null)
                    ListScroll.ShowSoon(ResultList, () => vm.Selected is { } now ? vm.Results.IndexOf(now) : -1);
            };
        };
    }

    private void Commit() => (DataContext as AddEffectViewModel)?.Add();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.Source is not Button && DataContext is AddEffectViewModel { CanAdd: true }) { Commit(); e.Handled = true; }
        else if (e.Key == Key.Down && e.Source == SearchBox && ResultList.ItemCount > 0)
        {
            if (ResultList.SelectedIndex < 0) ResultList.SelectedIndex = 0;
            (ResultList.ContainerFromIndex(ResultList.SelectedIndex) as Control ?? ResultList).Focus();
            e.Handled = true;
        }
    }

    private void OnAddClick(object? sender, RoutedEventArgs e) => Commit();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnLoadClick(object? sender, RoutedEventArgs e) => Guard.Fire("FX: load a plug-in", async () =>
    {
        if (DataContext is not AddEffectViewModel vm) return;
        if (await PathPicker.PickAsync(this, PathKind.File, "Load a plug-in", typeName: "VST plug-ins", patterns: vm.FilePatterns) is { } path) vm.LoadFile(path);
    });
}
