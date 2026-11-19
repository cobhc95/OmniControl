namespace OmniControl.App.Shared.Ui.Controls;

/// THE small line of recent values (a Resources card's history; look <c>c|Sparkline</c> in Styles/Graphs.axaml): the values, oldest at the
/// left, scaled from 0 to <see cref="Maximum"/> (0: the largest value shown), drawn across the control's whole size; an unknown value (NaN) breaks the line.
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty = AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<Sparkline, double>(nameof(Maximum));
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<double> StrokeThicknessProperty = AvaloniaProperty.Register<Sparkline, double>(nameof(StrokeThickness), 1);

    static Sparkline() => AffectsRender<Sparkline>(ValuesProperty, MaximumProperty, StrokeProperty, StrokeThicknessProperty);

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Values is not { Count: > 1 } values || Stroke is null) return;
        var top = Maximum > 0 ? Maximum : values.Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max();
        if (top <= 0) top = 1; // all zero: a flat line at the bottom
        double width = Bounds.Width, height = Bounds.Height - StrokeThickness, step = width / (values.Count - 1);
        var geometry = new StreamGeometry();
        using (var line = geometry.Open())
        {
            var open = false;
            for (var i = 0; i < values.Count; i++)
            {
                if (double.IsNaN(values[i]))
                {
                    if (open) line.EndFigure(false);
                    open = false;
                    continue;
                }
                var point = new Point(i * step, StrokeThickness / 2 + height * (1 - Math.Clamp(values[i] / top, 0, 1)));
                if (open) line.LineTo(point);
                else line.BeginFigure(point, false);
                open = true;
            }
            if (open) line.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(Stroke, StrokeThickness, lineJoin: PenLineJoin.Round), geometry);
    }
}
