using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Automation.Power;

/// Layout and wiring of the power plan editor: a double-click opens a number for typing, the wheel over a selector steps it, and a click elsewhere takes a typed number.
public partial class PowerPlanEditor : UserControl
{
    private TopLevel? _watching;

    public PowerPlanEditor()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnWheel);
    }

    private void OnValueDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: PowerValueCell cell } text || !cell.CanType) return;
        cell.BeginTyping();
        e.Handled = true;
        _watching ??= TopLevel.GetTopLevel(this);
        _watching?.AddHandler(PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel, handledEventsToo: true);
        Dispatcher.UIThread.Post(() =>
        {
            if (text.GetVisualParent()?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.IsEffectivelyVisible) is not { } box) return;
            box.Focus(NavigationMethod.Directional);
            box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void OnBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is PowerValueCell cell) cell.Commit();
    }

    /// A click anywhere but in a box being typed in takes the typed number, like Enter.
    private void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<TextBox>() is not null) return;
        foreach (var box in this.GetVisualDescendants().OfType<TextBox>().Where(b => b.IsEffectivelyVisible))
            (box.DataContext as PowerValueCell)?.Commit();
    }

    /// The wheel over a selector steps it (up = next); anywhere else it is left to scroll the page.
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!ValueWheel.IsOverSelector(e.Source as Visual)) return;
        if ((e.Source as Visual)?.FindAncestorOfType<StackPanel>() is not { DataContext: PowerValueCell cell } || cell.IsTyping) return;
        cell.Step(ValueWheel.Direction(e));
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _watching?.RemoveHandler(PointerPressedEvent, OnAnyPress);
        _watching = null;
        base.OnDetachedFromVisualTree(e);
    }
}
