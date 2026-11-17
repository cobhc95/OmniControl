using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
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
    /// <summary>
    /// Marks the part of a row that is the ‹ value › selector itself (arrows and value). A row-level wheel handler changes the value only
    /// when the pointer is over a marked element (<see cref="IsOverSelector"/>); over the label or empty space it leaves the wheel
    /// unhandled so the page scrolls.
    /// </summary>
    public static readonly AttachedProperty<bool> SelectorProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Selector", typeof(ValueWheel));

    public static bool GetSelector(Control control) => control.GetValue(SelectorProperty);
    public static void SetSelector(Control control, bool value) => control.SetValue(SelectorProperty, value);

    /// <summary>True when the wheel's source is a marked selector or inside one: the one decision every row-level wheel handler asks.</summary>
    public static bool IsOverSelector(Visual? source)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
            if (visual is Control control && GetSelector(control)) return true;
        return false;
    }

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
