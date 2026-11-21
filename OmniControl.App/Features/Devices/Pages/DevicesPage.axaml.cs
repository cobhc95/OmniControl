using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Devices.Pages;

/// Wiring only: the device list's right-click (or menu key) menu in the shared menu style, the "Merge into…" list it opens, and the keys
/// Ctrl+C, X, V, D and Delete on the chosen rows (the same pattern as a simulation's step list, <see cref="SimulationSteps"/>).
public partial class DevicesPage : UserControl
{
    private readonly PointerMenu _menu = new();
    private DevicesPageViewModel? _wired;

    public DevicesPage()
    {
        InitializeComponent();
        DeviceList.ContextRequested += OnDeviceContext;
        DataContextChanged += (_, _) => Follow();
        AttachedToVisualTree += (_, _) => Follow();
        DetachedFromVisualTree += (_, _) => Follow();
    }

    // The model is heard only while the page is on screen, so a view let go (a released main window) is never held by its model.
    private void Follow()
    {
        var model = this.IsAttachedToVisualTree() ? DataContext as DevicesPageViewModel : null;
        if (model == _wired) return;
        if (_wired is not null) _wired.ImportOpened -= OnImportOpened;
        _wired = model;
        if (_wired is not null) _wired.ImportOpened += OnImportOpened;
    }

    private void OnImportOpened() => Dispatcher.UIThread.Post(ImportCard.BringIntoView, DispatcherPriority.Background);

    private IReadOnlyList<DeviceItem> Chosen() => (DeviceList.SelectedItems ?? new List<object>()).OfType<DeviceItem>().ToList();

    private void OnDeviceContext(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not DevicesPageViewModel vm || (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: DeviceItem } item) return;
        if (!item.IsSelected) { DeviceList.SelectedItems?.Clear(); item.IsSelected = true; }
        var rows = Chosen();
        ShowMenu(item, vm.RowMenu(rows, () => Dispatcher.UIThread.Post(() => ShowMenu(item, vm.MergeTargets(rows), "Merge into"))), null);
        e.Handled = true;
    }

    private void ShowMenu(Control at, IReadOnlyList<MenuRowItem> rows, string? title) => _menu.Show(at, rows, title);

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
