using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OmniControl.App.Controls;

namespace OmniControl.App.Pages.Devices;

/// <summary>
/// Wiring only: the rows are reordered by dragging (<see cref="ListDragReorder"/>), a click on a row (not on its link) or Enter opens that
/// method's set-up, and "+ add method" opens the list of methods in the shared menu style.
/// </summary>
public partial class MethodList : UserControl
{
    private Flyout? _menu;
    private MenuRowList? _kinds;
    private Flyout? _emulatorMenu;
    private KeepRunningMenu? _emulatorRows;

    public MethodList()
    {
        InitializeComponent();
        ListDragReorder.Attach(MethodRows, (rows, at) => (DataContext as DeviceItem)?.MoveMethods(rows, at));
        MethodRows.Tapped += OnRowTapped;
    }

    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        var source = e.Source as Visual;
        if (source?.FindAncestorOfType<Button>(includeSelf: true) is not null) return; // the row's own link does it
        if (source?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: MethodItem method }) method.EditSetupCommand.Execute(null);
    }

    /// <summary>A right-click (or the menu key) on a simulation row's emulator chip: the Keep running menu with Close now / Start and Show at its top.</summary>
    private void OnEmulatorContext(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Button chip || chip.DataContext is not MethodItem method) return;
        if (_emulatorMenu is null)
        {
            _emulatorRows = new KeepRunningMenu();
            _emulatorRows.Picked += (_, _) => _emulatorMenu?.Hide();
            _emulatorMenu = new ListMenuFlyout { Placement = PlacementMode.Pointer, Content = _emulatorRows };
        }
        _emulatorRows!.Bind(method.Simulation, withActions: true);
        _emulatorMenu.ShowAt(chip, showAtPointer: true);
        e.Handled = true;
    }

    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Source is Button || (e.Source as StyledElement)?.DataContext is not MethodItem method) return;
        method.EditSetupCommand.Execute(null);
        e.Handled = true;
    }

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DeviceItem item) return;
        if (_menu is null)
        {
            _kinds = new MenuRowList { Title = "Method" };
            _kinds.Picked += (_, _) => _menu?.Hide();
            _menu = new ListMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft, Content = _kinds };
        }
        _kinds!.ItemsSource = item.AddMethodOptions;
        _menu.ShowAt(AddButton);
    }
}
