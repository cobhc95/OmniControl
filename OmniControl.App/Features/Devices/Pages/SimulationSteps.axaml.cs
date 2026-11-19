using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Devices.Pages;

/// Wiring only: the rows are reordered by dragging (<see cref="ListDragReorder"/>), Delete on a step removes it (the keyboard is put back
/// by <see cref="SentenceFocus"/>), Ctrl+C, X, V and D copy, cut, paste below and duplicate, a right-click (or the menu key) opens the step's
/// menu in the shared menu style, and "+ add step" opens the list of step kinds.
public partial class SimulationSteps : UserControl
{
    private Flyout? _menu;
    private MenuRowList? _kinds;
    private Flyout? _rowMenu;
    private MenuRowList? _rowRows;

    public SimulationSteps()
    {
        InitializeComponent();
        ListDragReorder.Attach(StepList, (rows, at) => (DataContext as SimulationViewModel)?.MoveRows(rows, at));
        StepList.ContextRequested += OnStepContext;
    }

    private void OnStepContext(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not SimulationViewModel vm || (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: SimStepItem } item) return;
        if (_rowMenu is null)
        {
            _rowRows = new MenuRowList();
            _rowRows.Picked += (_, _) => _rowMenu?.Hide();
            _rowMenu = new ListMenuFlyout { Placement = PlacementMode.Pointer, Content = _rowRows };
        }
        if (!item.IsSelected) { StepList.SelectedItems?.Clear(); item.IsSelected = true; }
        _rowRows!.ItemsSource = vm.RowMenu(Selected());
        _rowMenu.ShowAt(item, showAtPointer: true);
        e.Handled = true;
    }

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SimulationViewModel vm) return;
        if (_menu is null)
        {
            _kinds = new MenuRowList();
            _kinds.Picked += (_, _) => _menu?.Hide();
            _menu = new ListMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft, Content = _kinds };
        }
        _kinds!.ItemsSource = vm.AddOptions;
        _menu.ShowAt(AddButton);
    }

    private IReadOnlyList<SimStepItem> Selected() => (StepList.SelectedItems ?? new List<object>()).OfType<SimStepItem>().ToList();

    private void OnStepKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SimulationViewModel vm || e.Source is TextBox) return;
        if (e.Key == Key.Escape && Selected().Count > 0)
        {
            StepList.SelectedItems?.Clear();
            e.Handled = true;
            return;
        }
        var rows = Selected();
        if (rows.Count == 0 && (e.Source as StyledElement)?.DataContext is SimStepItem focused) rows = new[] { focused };
        if (rows.Count == 0) return;
        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.C or Key.X or Key.V or Key.D)
        {
            if (e.Key == Key.C) vm.Copy(rows);
            else if (e.Key == Key.X) { vm.Copy(rows); vm.Remove(rows); }
            else if (e.Key == Key.V) vm.PasteBelow(rows);
            else vm.Duplicate(rows);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Delete) return;
        var first = vm.Rows.IndexOf(rows.OrderBy(vm.Rows.IndexOf).First());
        var at = e.Source is Button button && button.Classes.Contains("block") ? SentenceFocus.IndexOf(StepList, button) : Math.Max(0, first - 1);
        vm.Remove(rows);
        SentenceFocus.FocusLater(StepList, at);
        e.Handled = true;
    }
}
