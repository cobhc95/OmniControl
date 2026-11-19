using Avalonia.Input;
using OmniControl.App.Shared.Ui.Controls;
using OmniControl.App.Features.Volume.Pages;

namespace OmniControl.Tests;

/// The knob's pure maths (<see cref="KnobMath"/>): the 270 degree sweep and back, the arcs of a volume and a pan knob, the drag, the
/// click rule (one click types, a double-click resets, a drag turns), the key steps, and the reset values the rows declare.
public class KnobMathTests
{
    [Theory]
    [InlineData(0, -135)]
    [InlineData(50, 0)]
    [InlineData(100, 135)]
    public void Volume_SweepsFromLowerLeftToLowerRight(double value, double angle) =>
        Assert.Equal(angle, KnobMath.AngleOf(value, 0, 100), 6);

    [Theory]
    [InlineData(-100, -135)]
    [InlineData(0, 0)]
    [InlineData(100, 135)]
    public void Pan_IsCentredAtTheTop(double value, double angle) => Assert.Equal(angle, KnobMath.AngleOf(value, -100, 100), 6);

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(100)]
    public void ValueAt_IsTheInverseOfAngleOf(double value) =>
        Assert.Equal(value, KnobMath.ValueAt(KnobMath.AngleOf(value, 0, 100), 0, 100), 6);

    [Fact]
    public void ValuesOutsideTheRange_AreHeldAtItsEnds()
    {
        Assert.Equal(-135, KnobMath.AngleOf(-20, 0, 100));
        Assert.Equal(135, KnobMath.AngleOf(140, 0, 100));
        Assert.Equal(100, KnobMath.ValueAt(400, 0, 100));
        Assert.Equal(0, KnobMath.Fraction(5, 10, 10)); // an empty range never divides by zero
    }

    [Fact]
    public void Volume_DrawsFromEmpty_Pan_FromTheCentre()
    {
        Assert.Equal((-135d, 0d), KnobMath.ArcOf(50, 0, 100, fromCentre: false));
        Assert.Equal((-67.5, 0d), KnobMath.ArcOf(-50, -100, 100, fromCentre: true));
        Assert.Equal((0d, 67.5), KnobMath.ArcOf(50, -100, 100, fromCentre: true));
        var (from, to) = KnobMath.ArcOf(0, -100, 100, fromCentre: true);
        Assert.Equal(from, to); // centred: nothing to draw
    }

    [Fact]
    public void Drag_UpTurnsUp_WholeNumbers_HeldInRange()
    {
        Assert.Equal(50, KnobMath.DragValue(50, 0, 0, 100));
        Assert.Equal(60, KnobMath.DragValue(50, KnobMath.FullTurnPixels / 10, 0, 100));
        Assert.Equal(40, KnobMath.DragValue(50, -KnobMath.FullTurnPixels / 10, 0, 100));
        Assert.Equal(100, KnobMath.DragValue(90, KnobMath.FullTurnPixels, 0, 100));
        Assert.Equal(-100, KnobMath.DragValue(0, -KnobMath.FullTurnPixels, -100, 100));
        Assert.Equal(Math.Round(KnobMath.DragValue(50, 1.3, 0, 100)), KnobMath.DragValue(50, 1.3, 0, 100));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(2, -2, false)]
    [InlineData(3, 0, true)]
    [InlineData(0, -4, true)]
    public void APressThatMoves_IsADrag(double dx, double dy, bool drag) => Assert.Equal(drag, KnobMath.IsDrag(dx, dy));

    [Fact]
    public void OneClickTypes_ADoubleClickResets_ADragTurns()
    {
        Assert.Equal(KnobGesture.Click, KnobMath.GestureOf(moved: false, clickCount: 1));
        Assert.Equal(KnobGesture.DoubleClick, KnobMath.GestureOf(moved: false, clickCount: 2));
        Assert.Equal(KnobGesture.Drag, KnobMath.GestureOf(moved: true, clickCount: 1));
        Assert.Equal(KnobGesture.Drag, KnobMath.GestureOf(moved: true, clickCount: 2));
    }

    [Fact]
    public void Keys_UpAndRightIncrease_ShiftAndPagesTakeALargeStep()
    {
        Assert.Equal(1, KnobMath.KeyStep(Key.Up, KeyModifiers.None, 0, 100));
        Assert.Equal(1, KnobMath.KeyStep(Key.Right, KeyModifiers.None, 0, 100));
        Assert.Equal(-1, KnobMath.KeyStep(Key.Down, KeyModifiers.None, 0, 100));
        Assert.Equal(-1, KnobMath.KeyStep(Key.Left, KeyModifiers.None, 0, 100));
        Assert.Equal(10, KnobMath.KeyStep(Key.Up, KeyModifiers.Shift, 0, 100));
        Assert.Equal(10, KnobMath.KeyStep(Key.PageUp, KeyModifiers.None, 0, 100));
        Assert.Equal(-10, KnobMath.KeyStep(Key.PageDown, KeyModifiers.None, 0, 100));
        Assert.Equal(2, KnobMath.KeyStep(Key.Up, KeyModifiers.None, -100, 100)); // 1 % of the pan's range, the wheel's step
        Assert.Null(KnobMath.KeyStep(Key.Home, KeyModifiers.None, 0, 100)); // the reset belongs to the shared row keys
        Assert.Null(KnobMath.KeyStep(Key.Up, KeyModifiers.Control, 0, 100));
        Assert.Equal(100, KnobMath.Stepped(99.6, 10, 0, 100));
    }

    [Fact]
    public void TheRowsResetVolumeToFull_AndPanToTheCentre()
    {
        Assert.Equal(100, VolumeRow.VolumeReset);
        Assert.Equal(0, VolumeRow.PanReset);
    }
}
