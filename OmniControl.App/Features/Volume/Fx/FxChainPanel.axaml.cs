using Avalonia.Input;
using Avalonia.VisualTree;
using OmniControl.App.Features.Volume.Fx.Plugins;

namespace OmniControl.App.Features.Volume.Fx;

/// Wiring only, for the FX window's effect chain (it knows nothing of the window around it): the editor cards use the shared keyboard
/// model; Delete, Space, Alt+Up/Down, Ctrl+C/V, Ctrl+N and Esc act on the chain's selection; Tab and F6 move between the list and
/// the editor; rows drag to reorder (the whole selection together); a rectangle drawn on the empty part of the list selects rows; a
/// double-click on an effect's row puts its editor in its own window and back (TabForge's FX loader; Enter gives the editor the
/// keyboard). The panel takes its own keys while it has the keyboard; the window also passes it every key (<see cref="HandleKey"/>).
public partial class FxChainPanel : UserControl
{
    /// The host's own buttons, shown after Add and Remove under the list (the FX window's Forget program).
    public static readonly StyledProperty<object?> ListToolsProperty = AvaloniaProperty.Register<FxChainPanel, object?>(nameof(ListTools));

    /// The effect list's width in pixels (0 = the standard width, <c>FxChainListWidth</c>); the user drags it at the splitter.
    public static readonly StyledProperty<double> ListWidthProperty =
        AvaloniaProperty.Register<FxChainPanel, double>(nameof(ListWidth), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private readonly ListDragReorder.Drag _drag;
    private readonly RubberBandSelect.Band _band;
    private FxChainViewModel? _vm;

    public FxChainPanel()
    {
        InitializeComponent();
        _drag = ListDragReorder.Attach(SlotList, (rows, at) => _vm?.MoveRows(rows, at));
        _band = RubberBandSelect.Attach(SlotList, SelectionBand);
        SlotList.SelectionChanged += (_, _) => _vm?.SetPicked(SlotList.SelectedItems?.OfType<FxSlotViewModel>() ?? Enumerable.Empty<FxSlotViewModel>());
        // Seen even when the drag code has taken the press (a press on a selected row of a multi-selection).
        SlotList.AddHandler(PointerPressedEvent, OnRowPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, e) => HandleKey(e), RoutingStrategies.Tunnel);
        PluginArea.ScaleRefused += () => _vm?.Selected?.PluginScaleRefused();
        PaneSplitter.DragCompleted += (_, _) => SetCurrentValue(ListWidthProperty, Panes.ColumnDefinitions[0].ActualWidth);
        ApplyListWidth();
    }

    public double ListWidth
    {
        get => GetValue(ListWidthProperty);
        set => SetValue(ListWidthProperty, value);
    }

    private void ApplyListWidth()
    {
        var standard = Application.Current?.TryFindResource("FxChainListWidth", out var w) == true && w is double d ? d : 0;
        var width = ListWidth > 0 ? ListWidth : standard;
        Panes.ColumnDefinitions[0].Width = width > 0 ? new GridLength(width) : GridLength.Auto;
    }

    public object? ListTools
    {
        get => GetValue(ListToolsProperty);
        set => SetValue(ListToolsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ListToolsProperty) ToolsHost.Content = change.NewValue;
        else if (change.Property == ListWidthProperty) ApplyListWidth();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) { _vm.ReselectRequested -= Reselect; _vm.AddRequested -= AddEffect; }
        _vm = DataContext as FxChainViewModel;
        if (_vm is not null) { _vm.ReselectRequested += Reselect; _vm.AddRequested += AddEffect; }
    }

    /// The chain list has the keyboard.
    public bool ListHasKeyboard => SlotList.IsKeyboardFocusWithin;

    /// Puts the keyboard in the chain list.
    public void FocusChain() => (SlotList.ContainerFromIndex(Math.Max(0, SlotList.SelectedIndex)) as Control ?? SlotList).Focus();

    /// The chain's shortcuts. Marks the key handled when it was used.
    public void HandleKey(KeyEventArgs e)
    {
        if (_vm is not { } vm || e.Handled) return;
        var ctrl = e.KeyModifiers == KeyModifiers.Control;
        if (e.Key == Key.N && ctrl) AddEffect();
        else if (e.Key == Key.F6 || (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == 0)) SwitchPane(); // Tab only moves between the two panes
        else if (e.Key == Key.Escape) { if (!HandleEscape()) return; }
        else if (ctrl && vm.Selected is { } effect && ScaleKey(e.Key) is { } scale) scale(effect);
        else if (e.Source is CheckBox || !SlotList.IsKeyboardFocusWithin) return;
        else if (e.Key == Key.Enter && vm.HasSelection) FocusEditor();
        else if (e.Key == Key.Space) vm.ToggleEnabled();
        else if (e.Key == Key.Delete) vm.RemoveCommand.Execute(null);
        else if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.None && vm.StepSelection(Wrap.Direction(e.Key))) FocusChain();
        else if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None) { if (!vm.SetSelectedFolderOpen(e.Key == Key.Right)) return; }
        else if (e.Key == Key.C && ctrl) vm.CopyCommand.Execute(null);
        else if (e.Key == Key.V && ctrl) vm.PasteCommand.Execute(null);
        else if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.Alt)
            (e.Key == Key.Up ? vm.MoveUpCommand : vm.MoveDownCommand).Execute(null);
        else return;
        e.Handled = true;
    }

    /// Ctrl+- smaller, Ctrl+= larger, Ctrl+0 100 % (the selected effect's Scale, from either pane); null for other keys.
    private static Action<FxSlotViewModel>? ScaleKey(Key key) => key switch
    {
        Key.OemMinus or Key.Subtract => s => s.ScaleDownCommand.Execute(null),
        Key.OemPlus or Key.Add => s => s.ScaleUpCommand.Execute(null),
        Key.D0 or Key.NumPad0 => s => s.ScaleResetCommand.Execute(null),
        _ => null,
    };

    /// Esc: ends a drag or a rectangle without changing anything, else reduces a multi-selection to one. False when there was nothing to do (the window may close).
    private bool HandleEscape() => _drag.Cancel() || _band.Cancel() || (SlotList.IsKeyboardFocusWithin && _vm?.CollapseSelection() == true);

    /// The second press of a double-click on a row (not on its switch).
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || !e.GetCurrentPoint(SlotList).Properties.IsLeftButtonPressed || _vm is not { } vm) return;
        if (e.Source is not Visual source || source.FindAncestorOfType<CheckBox>(true) is not null) return;
        if (source.FindAncestorOfType<ListBoxItem>(true) is not { DataContext: FxSlotViewModel slot }) return;
        vm.TogglePopOut(slot);
        e.Handled = true;
    }

    /// A move or paste rebuilt the list's selection; put these effects back.
    private void Reselect(IReadOnlyList<FxSlotViewModel> items)
    {
        SlotList.SelectedItems?.Clear();
        foreach (var s in items) SlotList.SelectedItems?.Add(s);
    }

    /// F6 / Tab: from the chain list to the editor cards, and back.
    private void SwitchPane()
    {
        if (SlotList.IsKeyboardFocusWithin) FocusEditor();
        else FocusChain();
    }

    /// Gives the keyboard to the editor's cards (a plug-in's own editor takes it when it is clicked).
    public void FocusEditor() => EditorView.FocusCards();

    /// Opens the Add effect window; what it returns goes on the end of the chain.
    public async void AddEffect()
    {
        if (_vm is not { } vm || TopLevel.GetTopLevel(this) is not Window owner) return;
        var picked = await new AddEffectWindow { DataContext = new AddEffectViewModel(vm.Library, vm.CanAddFolder) }.ShowDialog<PluginEntry?>(owner);
        if (picked is not null) vm.AddEntry(picked);
    }

    private void OnAddClick(object? sender, RoutedEventArgs e) => AddEffect();
}
