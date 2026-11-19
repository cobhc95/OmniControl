using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Devices.Pages;

/// Wiring only: the device list's right-click (or menu key) menu in the shared menu style, the "Merge into…" list it opens, and the keys
/// Ctrl+C, X, V, D and Delete on the chosen rows (the same pattern as a simulation's step list, <see cref="SimulationSteps"/>).
public partial class DevicesPage : UserControl
{
    private Flyout? _menu;
    private MenuRowList? _menuRows;

    public DevicesPage()
    {
        InitializeComponent();
        DeviceList.ContextRequested += OnDeviceContext;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DevicesPageViewModel vm) vm.ImportOpened += () => Dispatcher.UIThread.Post(ImportCard.BringIntoView, DispatcherPriority.Background);
        };
    }

    private IReadOnlyList<DeviceItem> Chosen() => (DeviceList.SelectedItems ?? new List<object>()).OfType<DeviceItem>().ToList();

    private void OnDeviceContext(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not DevicesPageViewModel vm || (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: DeviceItem } item) return;
        if (!item.IsSelected) { DeviceList.SelectedItems?.Clear(); item.IsSelected = true; }
        var rows = Chosen();
        ShowMenu(item, vm.RowMenu(rows, () => Dispatcher.UIThread.Post(() => ShowMenu(item, vm.MergeTargets(rows), "Merge into"))), null);
        e.Handled = true;
    }

    private void ShowMenu(Control at, IReadOnlyList<MenuRowItem> rows, string? title)
    {
        if (_menu is null)
        {
            _menuRows = new MenuRowList();
            _menuRows.Picked += (_, _) => _menu?.Hide();
            _menu = new ListMenuFlyout { Placement = PlacementMode.Pointer, Content = _menuRows };
        }
        _menuRows!.Title = title;
        _menuRows.ItemsSource = rows;
        _menu.ShowAt(at, showAtPointer: true);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not DevicesPageViewModel vm || e.Source is TextBox) return;
        var rows = Chosen();
        if (rows.Count == 0) return;
        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.C or Key.X or Key.V or Key.D)
        {
            if (e.Key == Key.C) vm.Copy(rows);
            else if (e.Key == Key.X) vm.Cut(rows);
            else if (e.Key == Key.V) vm.PasteBelow(rows);
            else vm.Duplicate(rows);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Delete) return;
        _ = vm.DeleteRowsAsync(rows);
        e.Handled = true;
    }
}
