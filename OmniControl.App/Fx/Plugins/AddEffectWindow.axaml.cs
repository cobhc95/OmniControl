using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace OmniControl.App.Fx.Plugins;

/// <summary>
/// Wiring only. Keys: type to search, Down goes to the list, Enter or double-click adds, Esc cancels.
/// The window closes with the chosen <see cref="PluginEntry"/>, or null.
/// </summary>
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
            if (DataContext is AddEffectViewModel vm) vm.Chosen += entry => Close(entry);
        };
    }

    private void Commit() => (DataContext as AddEffectViewModel)?.Add();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.Source is not ComboBox { IsDropDownOpen: true }) { Close(null); e.Handled = true; }
        else if (e.Key == Key.Enter && e.Source is not Button && DataContext is AddEffectViewModel { CanAdd: true }) { Commit(); e.Handled = true; }
        else if (e.Key == Key.Down && e.Source == SearchBox && ResultList.ItemCount > 0)
        {
            if (ResultList.SelectedIndex < 0) ResultList.SelectedIndex = 0;
            (ResultList.ContainerFromIndex(ResultList.SelectedIndex) as Control ?? ResultList).Focus();
            e.Handled = true;
        }
    }

    private void OnAddClick(object? sender, RoutedEventArgs e) => Commit();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private async void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddEffectViewModel vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load a VST3 plug-in",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("VST3 plug-ins") { Patterns = new[] { "*.vst3" } } },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) vm.LoadFile(path);
    }
}
