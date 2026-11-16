using Avalonia.Input;
using OmniControl.Core.Models;

namespace OmniControl.App.Controls;

/// <summary>What a press on a <see cref="Knob"/> turned out to be once it ended.</summary>
public enum KnobGesture
{
    /// <summary>The pointer moved past <see cref="KnobMath.ClickSlop"/>: the value followed the drag.</summary>
    Drag,
    /// <summary>One press without a drag: the knob's value is typed in (the owner's rule for knobs).</summary>
    Click,
    /// <summary>The second press of a double-click: the value goes back to its reset value.</summary>
    DoubleClick,
}

/// <summary>
/// The pure maths of a <see cref="Knob"/> (no Avalonia drawing, so it is tested headless): the 270 degree sweep and where a value sits
/// on it, the arc a value draws (from empty, or from the centre for a pan knob), how far a drag turns it, what a press was, and how far
/// a key steps it. Whole numbers throughout, like every row value (<see cref="OmniControl.Core.Models.Percent"/>).
/// </summary>
public static class KnobMath
{
    /// <summary>The sweep: 270 degrees, from the lower left (StartAngle, measured clockwise from 12 o'clock) to the lower right.</summary>
    public const double Sweep = 270, StartAngle = -135;

    /// <summary>A press that moves less than this (pixels, either way) is a click, not a drag.</summary>
    public const double ClickSlop = 3;

    /// <summary>How far (pixels, upwards) a drag turns the knob from its minimum to its maximum.</summary>
    public const double FullTurnPixels = 160;

    /// <summary>A large key step in percent of the range (a small one is 1 %, the wheel's step, <see cref="Percent.StepOf"/>).</summary>
    private const double LargePercent = 10;

    /// <summary>The share (0-1) of the range a value sits at; an empty range counts as 0.</summary>
    public static double Fraction(double value, double minimum, double maximum) =>
        maximum <= minimum ? 0 : Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);

    /// <summary>The angle of a value on the sweep (degrees clockwise from 12 o'clock): <see cref="StartAngle"/> at the minimum, +135 at the maximum.</summary>
    public static double AngleOf(double value, double minimum, double maximum) => StartAngle + Sweep * Fraction(value, minimum, maximum);

    /// <summary>The value at an angle of the sweep (the inverse of <see cref="AngleOf"/>), held within the range.</summary>
    public static double ValueAt(double angle, double minimum, double maximum) =>
        minimum + (maximum - minimum) * Math.Clamp((angle - StartAngle) / Sweep, 0, 1);

    /// <summary>
    /// The arc a value draws, as start and end angles: from the start of the sweep to the value, or, for a pan knob
    /// (<paramref name="fromCentre"/>), from the centre of the range to the value (left or right). Equal angles = nothing to draw.
    /// </summary>
    public static (double From, double To) ArcOf(double value, double minimum, double maximum, bool fromCentre)
    {
        var at = AngleOf(value, minimum, maximum);
        if (!fromCentre) return (StartAngle, at);
        var centre = AngleOf((minimum + maximum) / 2, minimum, maximum);
        return at < centre ? (at, centre) : (centre, at);
    }

    /// <summary>The value after a drag that has moved <paramref name="upPixels"/> up (down is negative) since it began at <paramref name="startValue"/>; whole and within the range.</summary>
    public static double DragValue(double startValue, double upPixels, double minimum, double maximum) =>
        Math.Clamp(Math.Round(startValue + upPixels / FullTurnPixels * (maximum - minimum)), minimum, maximum);

    /// <summary>Whether a press that has moved this far (pixels) is a drag.</summary>
    public static bool IsDrag(double dx, double dy) => Math.Abs(dx) >= ClickSlop || Math.Abs(dy) >= ClickSlop;

    /// <summary>What a press was once it ended: a drag when it moved, a double-click on the second press, else a click.</summary>
    public static KnobGesture GestureOf(bool moved, int clickCount) =>
        moved ? KnobGesture.Drag : clickCount >= 2 ? KnobGesture.DoubleClick : KnobGesture.Click;

    /// <summary>One small step of the range: 1 %, at least 1 (the wheel's step).</summary>
    public static double SmallStep(double minimum, double maximum) => Percent.StepOf(minimum, maximum);

    /// <summary>One large step of the range: 10 %, at least 1.</summary>
    public static double LargeStep(double minimum, double maximum) => Percent.StepOf(minimum, maximum, LargePercent);

    /// <summary>
    /// How far a key turns a focused knob: Up/Right one step up, Down/Left one step down (Shift = a large step), PageUp/PageDown a
    /// large step; null for any other key (Home, the reset, belongs to the page's shared keys, which know the reset value).
    /// </summary>
    public static double? KeyStep(Key key, KeyModifiers modifiers, double minimum, double maximum)
    {
        var small = SmallStep(minimum, maximum);
        var large = LargeStep(minimum, maximum);
        var step = modifiers == KeyModifiers.Shift ? large : modifiers == KeyModifiers.None ? small : 0;
        return key switch
        {
            Key.Up or Key.Right when step > 0 => step,
            Key.Down or Key.Left when step > 0 => -step,
            Key.PageUp when modifiers == KeyModifiers.None => large,
            Key.PageDown when modifiers == KeyModifiers.None => -large,
            _ => null,
        };
    }

    /// <summary>A value moved by <paramref name="by"/>, whole and within the range.</summary>
    public static double Stepped(double value, double by, double minimum, double maximum) => Math.Clamp(Math.Round(value + by), minimum, maximum);
}
