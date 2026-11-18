using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace OmniControl.App.Pages;

/// <summary>
/// Attached property that gives a slider or a knob (any <see cref="RangeBase"/> value control) a value to jump back to when it is
/// double-clicked (pan to centre, volume to full...).
/// </summary>
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
    });

    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (NumberText.IsValueBox(e.Source)) return; // the typeable value text opens for typing, it never resets
        if (sender is RangeBase control && GetValue(control) is { } value) control.Value = value;
    }
}
