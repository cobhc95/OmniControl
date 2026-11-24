using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using OmniControl.App.Shared.Ui.Controls;

namespace OmniControl.App.Shared.Ui.Pages;

/// Attached property that gives a slider or a knob (any <see cref="RangeBase"/> value control) a value to jump back to when it is
/// double-clicked (pan to centre, volume to full...).
public static class SliderReset
{
    public static readonly AttachedProperty<double?> ValueProperty = AvaloniaProperty.RegisterAttached<RangeBase, double?>("Value", typeof(SliderReset));

    public static double? GetValue(RangeBase control) => control.GetValue(ValueProperty);
    public static void SetValue(RangeBase control, double? value) => control.SetValue(ValueProperty, value);

    static SliderReset() => ValueProperty.Changed.AddClassHandler<RangeBase>((control, e) =>
    {
        // Handled events too: the slider's thumb (and the knob) mark the press they received as handled.
        control.RemoveHandler(Avalonia.Input.Gestures.DoubleTappedEvent, OnDoubleTapped);
        if (e.NewValue is double) control.AddHandler(Avalonia.Input.Gestures.DoubleTappedEvent, OnDoubleTapped, handledEventsToo: true);
        foreach (var bar in control.GetVisualDescendants().OfType<SliderBar>()) bar.InvalidateVisual();
    });

    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (NumberText.IsValueBox(e.Source)) return; // the typeable value text opens for typing, it never resets
        if (sender is not RangeBase control || GetValue(control) is not { } value) return;
        control.Value = value;
        if (control is Slider slider) HoldUntilReleased(slider);
    }

    /// The double-click's second press also starts the slider's move-to-the-pointer drag (a press anywhere on the bar sets the value
    /// there and keeps following the pointer): until the button is let go, the slider is kept from seeing the pointer move, so a hand that
    /// shifts a pixel does not drag the value away from the reset. The handlers live on the slider's parent (it sees the move first on its
    /// way down) and remove themselves on the release.
    private static void HoldUntilReleased(Slider slider)
    {
        if (slider.GetVisualParent() is not Interactive parent) return;

        void Swallow(object? sender, PointerEventArgs e)
        {
            if (!e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed) Stop(); // the release was missed: never hold a later drag
            else if (e.Source is Visual source && (source == slider || slider.IsVisualAncestorOf(source))) e.Handled = true;
        }

        void Release(object? sender, PointerReleasedEventArgs e) => Stop();

        void Stop()
        {
            parent.RemoveHandler(InputElement.PointerMovedEvent, Swallow);
            parent.RemoveHandler(InputElement.PointerReleasedEvent, Release);
        }

        parent.AddHandler(InputElement.PointerMovedEvent, Swallow, RoutingStrategies.Tunnel);
        parent.AddHandler(InputElement.PointerReleasedEvent, Release, RoutingStrategies.Tunnel, handledEventsToo: true);
    }
}
