using OmniControl.App.Controls;
using OmniControl.App.Picker;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace OmniControl.App.Pages.Mouse;

/// <summary>
/// One input's half-width card, wiring only. The Default pill and the cogwheel open <see cref="PickerWindow"/> on the first action; a
/// chip opens its own action's window (its settings, with Back to the list), or takes it away when pressed on its x (or Delete); the
/// dashed chip opens the picker for a new last action; the "+N more" chip opens the list of every action. A chip dragged onto another
/// moves there. What the windows return goes to the card's view-model, which decides and stores.
/// </summary>
public partial class MouseButtonRow : UserControl
{
    /// <summary>The Tag of a chip's x (its hit area).</summary>
    private const string RemoveTag = "chipremove";

    /// <summary>How far (DIPs) the pointer must move before a press on a chip becomes a drag.</summary>
    private const double DragDistance = 4;

    private bool _pressedOnRemove;
    private Button? _dragChip;
    private Point _dragFrom;
    private bool _dragging, _justDragged;
    private Control? _moreOpener;
    private ChipFoldPanel? _panel;

    public MouseButtonRow()
    {
        InitializeComponent();
        foreach (var box in new[] { ParameterBox, PathBox.Box })
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                // Moving focus to the card makes the box commit its text.
                this.FindAncestorOfType<ListBoxItem>()?.Focus();
                e.Handled = true;
            };
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        ChipRow.Loaded += (_, _) => HookPanel();
        ChipRow.Unloaded += (_, _) => UnhookPanel();
        DataContextChanged += (_, _) => ShowFolded();
    }

    private MouseButtonItem? Item => DataContext as MouseButtonItem;

    // ---- the folding panel tells the card how many chips it left out ----

    private void HookPanel()
    {
        UnhookPanel();
        _panel = ChipRow.ItemsPanelRoot as ChipFoldPanel;
        if (_panel is not null) _panel.Folded += OnFolded;
        ShowFolded();
    }

    private void UnhookPanel()
    {
        if (_panel is not null) _panel.Folded -= OnFolded;
        _panel = null;
    }

    private void OnFolded(int hidden) => Item?.ShowFolded(hidden);

    // A row shown again for another card keeps its panel: the new card is told what the panel left out last.
    private void ShowFolded()
    {
        if (_panel is not null) Item?.ShowFolded(_panel.Hidden);
    }

    // ---- pickers ----

    private async void OnPickerClick(object? sender, RoutedEventArgs e) => await PickerWindow.ChooseForAsync(this, startInSettings: false, Picker, FirstPart());

    private async void OnCogClick(object? sender, RoutedEventArgs e) => await PickerWindow.ChooseForAsync(this, startInSettings: true, CogPart.Opener, FirstPart());

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button add) await PickerWindow.ChooseForAsync(add, startInSettings: false, add, FirstPart());
    }

    private async void OnChipClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ActionSlot slot } chip) return;
        var remove = _pressedOnRemove;
        _pressedOnRemove = false;
        if (_justDragged) _justDragged = false; // a drag that ended on its own chip is not a click
        else if (remove) slot.RemoveCommand.Execute(null);
        else await PickerWindow.ChooseForAsync(chip, startInSettings: true, chip, FirstPart());
    }

    // The keyboard goes back to the first chip (or the Default pill) when the part it came from is gone.
    private Control FirstPart() =>
        ChipRow.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible) is { } chip && Item?.IsCustom == true ? chip : Picker;

    // ---- the list of every action ----

    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        _moreOpener = sender as Control;
        FlyoutBase.ShowAttachedFlyout(ChipHolder);
    }

    private async void OnListRowClick(object? sender, RoutedEventArgs e) => await OpenFromList(sender, startInSettings: false);

    private async void OnListCogClick(object? sender, RoutedEventArgs e) => await OpenFromList(sender, startInSettings: true);

    private async Task OpenFromList(object? sender, bool startInSettings)
    {
        if (sender is not Control { DataContext: ActionSlot slot }) return;
        HideList();
        await PickerWindow.ChooseForAsync(this, slot, startInSettings, _moreOpener ?? FirstPart(), FirstPart());
    }

    private void OnListRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ActionSlot slot }) return;
        slot.RemoveCommand.Execute(null);
        if (Item?.IsCustom != true) HideList();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        HideList();
        _moreOpener?.Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    private void HideList() => FlyoutBase.GetAttachedFlyout(ChipHolder)?.Hide();

    // ---- a chip's x, Delete, and dragging a chip to a new place ----

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || e.KeyModifiers != KeyModifiers.None || e.Source is not Button { DataContext: ActionSlot slot }) return;
        slot.RemoveCommand.Execute(null);
        FirstPart().Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var source = e.Source as Visual;
        _pressedOnRemove = source?.FindAncestorOfType<Border>(includeSelf: true) is { Tag: RemoveTag };
        _dragChip = !_pressedOnRemove && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && source?.FindAncestorOfType<Button>(includeSelf: true) is { DataContext: ActionSlot } chip ? chip : null;
        _dragFrom = e.GetPosition(this);
        _dragging = _justDragged = false;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragChip is null || _dragging) return;
        var at = e.GetPosition(this);
        if (Math.Abs(at.X - _dragFrom.X) < DragDistance && Math.Abs(at.Y - _dragFrom.Y) < DragDistance) return;
        _dragging = true;
        _dragChip.Classes.Set("dragging", true);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var chip = _dragChip;
        var dragged = _dragging;
        _dragChip = null;
        _dragging = false;
        if (chip is null) return;
        chip.Classes.Set("dragging", false);
        _justDragged = dragged;
        if (!dragged || chip.DataContext is not ActionSlot moving || Item is not { } item) return;
        var target = (this.InputHitTest(e.GetPosition(this)) as Visual)?.FindAncestorOfType<Button>(includeSelf: true);
        if (target is { DataContext: ActionSlot onto } && onto != moving) item.MoveSlot(moving.Index, onto.Index - moving.Index);
    }
}
