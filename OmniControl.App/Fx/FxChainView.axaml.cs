using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniControl.App.Fx.Plugins;
using OmniControl.App.Pages;

namespace OmniControl.App.Fx;

/// <summary>Wiring only: the editor cards use the shared keyboard model; Delete, Alt+Up/Down and Ctrl+N edit the chain; rows drag to reorder.</summary>
public partial class FxChainView : UserControl
{
    public FxChainView()
    {
        InitializeComponent();
        RowEditing.Attach(EditorList);
        ListDragReorder.Attach(SlotList, (from, to) => (DataContext as FxChainViewModel)?.MoveTo(from, to));
        RubberBandSelect.Attach(SlotList, SelectionBand);
        SlotList.SelectionChanged += (_, _) =>
            (DataContext as FxChainViewModel)?.SetPicked(SlotList.SelectedItems?.OfType<FxSlotViewModel>() ?? Enumerable.Empty<FxSlotViewModel>());
        DataContextChanged += (_, _) =>
        {
            if (DataContext is FxChainViewModel vm) vm.ReselectRequested += Reselect;
        };
    }

    /// <summary>Puts the keyboard in the chain list (the window start, not the menu bar).</summary>
    public void FocusChain() => (SlotList.ContainerFromIndex(Math.Max(0, SlotList.SelectedIndex)) as Control ?? SlotList).Focus();

    /// <summary>The window's shortcuts (called by <see cref="FxWindow"/> for every key, whoever has the keyboard).</summary>
    public void HandleKey(KeyEventArgs e)
    {
        if (DataContext is not FxChainViewModel vm) return;
        if (e.Key == Key.N && e.KeyModifiers == KeyModifiers.Control) OnAddClick(null, e);
        else if (e.Key == Key.F6 || (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == 0)) SwitchPane(); // Tab only moves between the two sections
        else if (e.Source is CheckBox || !SlotList.IsKeyboardFocusWithin) return;
        else if (e.Key == Key.Enter && vm.HasSelection) FocusEditor();
        else if (e.Key == Key.Space) vm.ToggleEnabled();
        else if (e.Key == Key.Delete) vm.RemoveCommand.Execute(null);
        else if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.Alt)
            (e.Key == Key.Up ? vm.MoveUpCommand : vm.MoveDownCommand).Execute(null);
        else return;
        e.Handled = true;
    }

    /// <summary>A move rebuilds the list's selection; put the same effects back.</summary>
    private void Reselect(IReadOnlyList<FxSlotViewModel> items)
    {
        SlotList.SelectedItems?.Clear();
        foreach (var s in items) SlotList.SelectedItems?.Add(s);
    }

    /// <summary>F6: from the chain list to the editor cards, and back.</summary>
    private void SwitchPane()
    {
        if (SlotList.IsKeyboardFocusWithin) FocusEditor();
        else (SlotList.ContainerFromIndex(Math.Max(0, SlotList.SelectedIndex)) as Control ?? SlotList).Focus();
    }

    private void FocusEditor()
    {
        if (EditorList.ItemCount == 0) return;
        if (EditorList.SelectedIndex < 0) EditorList.SelectedIndex = 0;
        (EditorList.ContainerFromIndex(EditorList.SelectedIndex) as Control ?? EditorList).Focus();
    }

    /// <summary>Add... opens the Add effect window; what it returns goes on the end of the chain.</summary>
    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FxChainViewModel vm || TopLevel.GetTopLevel(this) is not Window owner) return;
        var picked = await new AddEffectWindow { DataContext = new AddEffectViewModel(vm.Library) }.ShowDialog<PluginEntry?>(owner);
        if (picked is not null) vm.AddEntry(picked);
    }
}
