using Avalonia.Data;

namespace OmniControl.App.Shared.Ui.Controls;

/// The colour editor's hue bar: every hue from 0 to 360 degrees, left to right, at full strength. See <see cref="ColorPickArea"/> for the
/// mouse and keys (Left/Right only; Up/Down are left to the pop-down).
public sealed class HueBar : ColorPickArea
{
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<HueBar, double>(nameof(Hue), defaultBindingMode: BindingMode.TwoWay);

    /// The colour stops of the bar: red, yellow, green, cyan, blue, magenta and red again.
    private const int Stops = 6;
    private const double FullTurn = 360;

    static HueBar() => AffectsRender<HueBar>(HueProperty);

    /// 0 to 360 degrees.
    public double Hue { get => GetValue(HueProperty); set => SetValue(HueProperty, value); }

    protected override Point Handle => new(Hue / FullTurn, 0.5);

    protected override Color HandleColour => new HsvColor(1, Hue, 1, 1).ToRgb();

    protected override void MoveTo(double x, double y) => Hue = Math.Round(x * FullTurn);

    protected override bool Nudge(int dx, int dy)
    {
        if (dx == 0) return false;
        Hue = Math.Clamp(Hue + dx, 0, FullTurn);
        return true;
    }

    protected override void DrawField(DrawingContext context, Rect field)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        };
        for (var i = 0; i <= Stops; i++) brush.GradientStops.Add(new GradientStop(new HsvColor(1, FullTurn * i / Stops % FullTurn, 1, 1).ToRgb(), (double)i / Stops));
        context.FillRectangle(brush, field);
    }
}
