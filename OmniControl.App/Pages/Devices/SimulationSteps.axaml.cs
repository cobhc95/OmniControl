using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniControl.App.Controls;

namespace OmniControl.App.Pages.Devices;

/// <summary>
/// Wiring only: the rows are reordered by dragging (<see cref="ListDragReorder"/>), Delete on a block removes its step (the keyboard is put back
/// by <see cref="SentenceFocus"/>), and "+ add step" opens the list of step kinds.
/// </summary>
public partial class SimulationSteps : UserControl
{
    private Flyout? _menu;
    private MenuRowList? _kinds;

    public SimulationSteps()
    {
        InitializeComponent();
        ListDragReorder.Attach(StepList, (rows, at) => (DataContext as SimulationViewModel)?.MoveRows(rows, at));
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

    private void OnStepKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || e.Source is not Button { DataContext: SimStepItem row } button || !button.Classes.Contains("block")) return;
        var at = SentenceFocus.IndexOf(StepList, button);
        row.RemoveCommand.Execute(null);
        SentenceFocus.FocusLater(StepList, at);
        e.Handled = true;
    }
}
