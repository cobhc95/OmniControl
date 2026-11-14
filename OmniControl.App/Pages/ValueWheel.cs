using Avalonia.Controls;
using Avalonia.Input;

namespace OmniControl.App.Pages;

/// <summary>
/// App-wide behaviour: the wheel over a value control changes it. A slider moves by <see cref="SliderNotchShare"/> of its range per notch
/// (at least 1); a closed drop-down goes to the next or previous entry. Registered once at start-up so every such control, present and
/// future, behaves the same without each one asking for it.
/// </summary>
public static class ValueWheel
{
    /// <summary>One notch moves a slider by 1/this of its range.</summary>
    private const int SliderNotchShare = 100;

    public static void Register()
    {
        InputElement.PointerWheelChangedEvent.AddClassHandler<Slider>(OnSliderWheel);
        InputElement.PointerWheelChangedEvent.AddClassHandler<ComboBox>(OnComboWheel);
    }

    /// <summary>How far one wheel notch moves a value with this range (also used by controls that are not sliders, such as an EQ band point).</summary>
    public static double StepOf(double minimum, double maximum) => Math.Max(1, (maximum - minimum) / SliderNotchShare);

    private static void OnSliderWheel(Slider slider, PointerWheelEventArgs e)
    {
        var step = StepOf(slider.Minimum, slider.Maximum);
        slider.Value = Math.Clamp(slider.Value + (e.Delta.Y > 0 ? step : -step), slider.Minimum, slider.Maximum);
        e.Handled = true;
    }

    private static void OnComboWheel(ComboBox combo, PointerWheelEventArgs e)
    {
        if (combo.IsDropDownOpen || combo.ItemCount == 0) return;
        ComboKeys.Step(combo, e.Delta.Y > 0 ? -1 : 1);
        e.Handled = true;
    }
}
