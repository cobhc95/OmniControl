using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace OmniControl.App.Pages;

/// <summary>Attached property that gives a slider a value to jump back to when it is double-clicked (pan to centre, volume to full...).</summary>
public static class SliderReset
{
    public static readonly AttachedProperty<double?> ValueProperty = AvaloniaProperty.RegisterAttached<Slider, double?>("Value", typeof(SliderReset));

    public static double? GetValue(Slider slider) => slider.GetValue(ValueProperty);
    public static void SetValue(Slider slider, double? value) => slider.SetValue(ValueProperty, value);

    static SliderReset() => ValueProperty.Changed.AddClassHandler<Slider>((slider, e) =>
    {
        // Handled events too: the slider's thumb marks the press it received as handled.
        slider.RemoveHandler(Avalonia.Input.Gestures.DoubleTappedEvent, OnDoubleTapped);
        if (e.NewValue is double) slider.AddHandler(Avalonia.Input.Gestures.DoubleTappedEvent, OnDoubleTapped, handledEventsToo: true);
    });

    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Slider slider && GetValue(slider) is { } value) slider.Value = value;
    }
}
