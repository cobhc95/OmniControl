namespace OmniControl.App.Shared.Ui.Controls;

/// THE small line of recent values (a Resources card's history; look <c>c|Sparkline</c> in Styles/Graphs.axaml): the values, oldest at the
/// left, scaled from <see cref="Minimum"/> to <see cref="Maximum"/> (0: the largest value shown; values outside are clamped), drawn across the control's whole size; an unknown value (NaN) breaks the line.
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty = AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<Sparkline, double>(nameof(Maximum));
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<Sparkline, double>(nameof(Minimum));
    public static readonly StyledProperty<double> WarnAtProperty = AvaloniaProperty.Register<Sparkline, double>(nameof(WarnAt), double.NaN);
    public static readonly StyledProperty<IBrush?> WarnStrokeProperty = AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(WarnStroke));
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<double> StrokeThicknessProperty = AvaloniaProperty.Register<Sparkline, double>(nameof(StrokeThickness), 1);

    static Sparkline() => AffectsRender<Sparkline>(ValuesProperty, MaximumProperty, MinimumProperty, WarnAtProperty, WarnStrokeProperty, StrokeProperty, StrokeThicknessProperty);

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    /// The value the scale starts at (default 0).
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    /// From this value (the latest one) the line is drawn in <see cref="WarnStroke"/>; NaN: never.
    public double WarnAt { get => GetValue(WarnAtProperty); set => SetValue(WarnAtProperty, value); }

    public IBrush? WarnStroke { get => GetValue(WarnStrokeProperty); set => SetValue(WarnStrokeProperty, value); }

    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Values is not { Count: > 1 } values || Stroke is null) return;
        var top = Maximum > 0 ? Maximum : values.Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max();
        if (top <= Minimum) top = Minimum + 1; // all at the bottom: a flat line there
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
                var point = new Point(i * step, StrokeThickness / 2 + height * (1 - Math.Clamp((values[i] - Minimum) / (top - Minimum), 0, 1)));
                if (open) line.LineTo(point);
                else line.BeginFigure(point, false);
                open = true;
            }
            if (open) line.EndFigure(false);
        }
        var latest = values.LastOrDefault(v => !double.IsNaN(v), double.NaN);
        var stroke = WarnStroke is not null && latest >= WarnAt ? WarnStroke : Stroke;
        context.DrawGeometry(null, new Pen(stroke, StrokeThickness, lineJoin: PenLineJoin.Round), geometry);
    }
}
