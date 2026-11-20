using Avalonia.Data;

namespace OmniControl.App.Shared.Ui.Controls;

/// The colour editor's saturation and brightness square for one <see cref="Hue"/>: saturation grows to the right, brightness to the top
/// (HSV, both in percent like the editor's sliders). See <see cref="ColorPickArea"/> for the mouse and keys.
public sealed class ColorSquare : ColorPickArea
{
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<ColorSquare, double>(nameof(Hue), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<ColorSquare, double>(nameof(Saturation), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> BrightnessProperty =
        AvaloniaProperty.Register<ColorSquare, double>(nameof(Brightness), defaultBindingMode: BindingMode.TwoWay);

    static ColorSquare() => AffectsRender<ColorSquare>(HueProperty, SaturationProperty, BrightnessProperty);

    /// 0 to 360 degrees.
    public double Hue { get => GetValue(HueProperty); set => SetValue(HueProperty, value); }

    /// 0 to 100 %.
    public double Saturation { get => GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }

    /// 0 to 100 %.
    public double Brightness { get => GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }

    protected override Point Handle => new(Saturation / 100, 1 - Brightness / 100);

    protected override Color HandleColour => new HsvColor(1, Hue, Saturation / 100, Brightness / 100).ToRgb();

    protected override void MoveTo(double x, double y)
    {
        Saturation = Math.Round(x * 100);
        Brightness = Math.Round((1 - y) * 100);
    }

    protected override bool Nudge(int dx, int dy)
    {
        Saturation = Math.Clamp(Saturation + dx, 0, 100);
        Brightness = Math.Clamp(Brightness - dy, 0, 100);
        return true;
    }

    /// The hue at full strength, washed to white towards the left and darkened to black towards the bottom.
    protected override void DrawField(DrawingContext context, Rect field)
    {
        context.FillRectangle(new SolidColorBrush(new HsvColor(1, Hue, 1, 1).ToRgb()), field);
        context.FillRectangle(Gradient(Colors.White, Color.FromArgb(0, 255, 255, 255), horizontal: true), field);
        context.FillRectangle(Gradient(Color.FromArgb(0, 0, 0, 0), Colors.Black, horizontal: false), field);
    }

    private static LinearGradientBrush Gradient(Color from, Color to, bool horizontal) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(horizontal ? 1 : 0, horizontal ? 0 : 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) },
    };
}
