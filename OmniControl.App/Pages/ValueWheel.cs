using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using OmniControl.App.Controls;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages;

/// <summary>
/// App-wide behaviour: the wheel over a value control changes it. A slider or a knob moves by 1 % of its range per notch (at least 1,
/// <see cref="Percent.StepOf"/>); a closed drop-down goes to the next or previous entry. Registered once at start-up so every such
/// control, present and future, behaves the same without each one asking for it.
/// </summary>
public static class ValueWheel
{
    public static void Register()
    {
        InputElement.PointerWheelChangedEvent.AddClassHandler<Slider>(OnRangeWheel);
        InputElement.PointerWheelChangedEvent.AddClassHandler<Knob>(OnRangeWheel);
        InputElement.PointerWheelChangedEvent.AddClassHandler<ComboBox>(OnComboWheel);
    }

    /// <summary>How far one wheel notch moves a value with this range (also used by controls that are not sliders, such as an EQ band point).</summary>
    public static double StepOf(double minimum, double maximum) => Percent.StepOf(minimum, maximum);

    private static void OnRangeWheel(RangeBase range, PointerWheelEventArgs e)
    {
        var step = StepOf(range.Minimum, range.Maximum);
        range.Value = Math.Clamp(range.Value + (e.Delta.Y > 0 ? step : -step), range.Minimum, range.Maximum);
        e.Handled = true;
    }

    private static void OnComboWheel(ComboBox combo, PointerWheelEventArgs e)
    {
        if (combo.IsDropDownOpen || combo.ItemCount == 0) return;
        ComboKeys.Step(combo, e.Delta.Y > 0 ? -1 : 1);
        e.Handled = true;
    }
}
