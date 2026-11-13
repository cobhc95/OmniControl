using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OmniControl.App.Fx.Plugins;

namespace OmniControl.App.Fx;

/// <summary>
/// Wiring only, for the FX window's effect chain (it knows nothing of the window around it): the editor cards use the shared keyboard
/// model; Delete, Space, Alt+Up/Down, Ctrl+C/V, Ctrl+N and Esc act on the chain's selection; Tab and F6 move between the list and
/// the editor; rows drag to reorder (the whole selection together); a rectangle drawn on the empty part of the list selects rows; a
/// double-click on an effect's row puts its editor in its own window and back (TabForge's FX loader; Enter gives the editor the
/// keyboard). The panel takes its own keys while it has the keyboard; the window also passes it every key (<see cref="HandleKey"/>).
/// </summary>
public partial class FxChainPanel : UserControl
{
    /// <summary>The host's own buttons, shown after Add and Remove under the list (the FX window's Forget program).</summary>
    public static readonly StyledProperty<object?> ListToolsProperty = AvaloniaProperty.Register<FxChainPanel, object?>(nameof(ListTools));

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
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.ReselectRequested -= Reselect;
        _vm = DataContext as FxChainViewModel;
        if (_vm is not null) _vm.ReselectRequested += Reselect;
    }

    /// <summary>The chain list has the keyboard.</summary>
    public bool ListHasKeyboard => SlotList.IsKeyboardFocusWithin;

    /// <summary>Puts the keyboard in the chain list.</summary>
    public void FocusChain() => (SlotList.ContainerFromIndex(Math.Max(0, SlotList.SelectedIndex)) as Control ?? SlotList).Focus();

    /// <summary>The chain's shortcuts. Marks the key handled when it was used.</summary>
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
        else if (e.Key == Key.C && ctrl) vm.CopyCommand.Execute(null);
        else if (e.Key == Key.V && ctrl) vm.PasteCommand.Execute(null);
        else if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.Alt)
            (e.Key == Key.Up ? vm.MoveUpCommand : vm.MoveDownCommand).Execute(null);
        else return;
        e.Handled = true;
    }

    /// <summary>Ctrl+- smaller, Ctrl+= larger, Ctrl+0 100 % (the selected effect's Scale, from either pane); null for other keys.</summary>
    private static Action<FxSlotViewModel>? ScaleKey(Key key) => key switch
    {
        Key.OemMinus or Key.Subtract => s => s.ScaleDownCommand.Execute(null),
        Key.OemPlus or Key.Add => s => s.ScaleUpCommand.Execute(null),
        Key.D0 or Key.NumPad0 => s => s.ScaleResetCommand.Execute(null),
        _ => null,
    };

    /// <summary>Esc: ends a drag or a rectangle without changing anything, else reduces a multi-selection to one. False when there was nothing to do (the window may close).</summary>
    private bool HandleEscape() => _drag.Cancel() || _band.Cancel() || (SlotList.IsKeyboardFocusWithin && _vm?.CollapseSelection() == true);

    /// <summary>The second press of a double-click on a row (not on its switch).</summary>
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || !e.GetCurrentPoint(SlotList).Properties.IsLeftButtonPressed || _vm is not { } vm) return;
        if (e.Source is not Visual source || source.FindAncestorOfType<CheckBox>(true) is not null) return;
        if (source.FindAncestorOfType<ListBoxItem>(true) is not { DataContext: FxSlotViewModel slot }) return;
        vm.TogglePopOut(slot);
        e.Handled = true;
    }

    /// <summary>A move or paste rebuilt the list's selection; put these effects back.</summary>
    private void Reselect(IReadOnlyList<FxSlotViewModel> items)
    {
        SlotList.SelectedItems?.Clear();
        foreach (var s in items) SlotList.SelectedItems?.Add(s);
    }

    /// <summary>F6 / Tab: from the chain list to the editor cards, and back.</summary>
    private void SwitchPane()
    {
        if (SlotList.IsKeyboardFocusWithin) FocusEditor();
        else FocusChain();
    }

    /// <summary>Gives the keyboard to the editor's cards (a plug-in's own editor takes it when it is clicked).</summary>
    public void FocusEditor() => EditorView.FocusCards();

    /// <summary>Opens the Add effect window; what it returns goes on the end of the chain.</summary>
    public async void AddEffect()
    {
        if (_vm is not { } vm || TopLevel.GetTopLevel(this) is not Window owner) return;
        var picked = await new AddEffectWindow { DataContext = new AddEffectViewModel(vm.Library) }.ShowDialog<PluginEntry?>(owner);
        if (picked is not null) vm.AddEntry(picked);
    }

    private void OnAddClick(object? sender, RoutedEventArgs e) => AddEffect();
}
