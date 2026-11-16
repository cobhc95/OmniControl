using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OmniControl.App.Controls;

/// <summary>
/// The drawn part of a <see cref="Knob"/> (its template's face): a 270 degree track arc, the value arc over it (from empty, or from the
/// centre mark for a pan knob), a pointer line from the middle to the value and, while focused, a thin ring. Drawing only: the angles come
/// from <see cref="KnobMath"/>, every brush and thickness from the master style (Styles/Knobs.axaml), so nothing here is a literal colour.
/// </summary>
public sealed class KnobFace : Control
{
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<KnobFace, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<KnobFace, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<KnobFace, double>(nameof(Value));
    public static readonly StyledProperty<bool> FromCentreProperty = AvaloniaProperty.Register<KnobFace, bool>(nameof(FromCentre));
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<KnobFace, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> ValueBrushProperty = AvaloniaProperty.Register<KnobFace, IBrush?>(nameof(ValueBrush));
    public static readonly StyledProperty<IBrush?> PointerBrushProperty = AvaloniaProperty.Register<KnobFace, IBrush?>(nameof(PointerBrush));

    /// <summary>The focus ring's brush; null = no ring (the knob is not focused from the keyboard).</summary>
    public static readonly StyledProperty<IBrush?> RingBrushProperty = AvaloniaProperty.Register<KnobFace, IBrush?>(nameof(RingBrush));

    public static readonly StyledProperty<double> ArcThicknessProperty = AvaloniaProperty.Register<KnobFace, double>(nameof(ArcThickness), 3);
    public static readonly StyledProperty<double> PointerThicknessProperty = AvaloniaProperty.Register<KnobFace, double>(nameof(PointerThickness), 2);

    /// <summary>The ring's thickness and the room left between it and the arcs.</summary>
    private const double RingThickness = 1.5, RingGap = 2;

    static KnobFace() => AffectsRender<KnobFace>(MinimumProperty, MaximumProperty, ValueProperty, FromCentreProperty, TrackBrushProperty,
        ValueBrushProperty, PointerBrushProperty, RingBrushProperty, ArcThicknessProperty, PointerThicknessProperty);

    public KnobFace() => IsHitTestVisible = false;

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool FromCentre { get => GetValue(FromCentreProperty); set => SetValue(FromCentreProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? ValueBrush { get => GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }
    public IBrush? PointerBrush { get => GetValue(PointerBrushProperty); set => SetValue(PointerBrushProperty, value); }
    public IBrush? RingBrush { get => GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }
    public double ArcThickness { get => GetValue(ArcThicknessProperty); set => SetValue(ArcThicknessProperty, value); }
    public double PointerThickness { get => GetValue(PointerThicknessProperty); set => SetValue(PointerThicknessProperty, value); }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var outer = size / 2;
        if (RingBrush is { } ring) context.DrawEllipse(null, new Pen(ring, RingThickness), centre, outer - RingThickness / 2, outer - RingThickness / 2);

        var radius = outer - RingThickness - RingGap - ArcThickness / 2;
        if (radius <= 0) return;
        DrawArc(context, TrackBrush, centre, radius, KnobMath.StartAngle, KnobMath.StartAngle + KnobMath.Sweep);
        if (FromCentre) DrawTick(context, TrackBrush, centre, radius, KnobMath.AngleOf((Minimum + Maximum) / 2, Minimum, Maximum));
        var (from, to) = KnobMath.ArcOf(Value, Minimum, Maximum, FromCentre);
        DrawArc(context, ValueBrush, centre, radius, from, to);

        if (PointerBrush is { } pointer)
        {
            var angle = KnobMath.AngleOf(Value, Minimum, Maximum);
            var pen = new Pen(pointer, PointerThickness, lineCap: PenLineCap.Round);
            context.DrawLine(pen, PointAt(centre, radius * 0.25, angle), PointAt(centre, radius - ArcThickness, angle));
        }
    }

    private void DrawArc(DrawingContext context, IBrush? brush, Point centre, double radius, double from, double to)
    {
        if (brush is null || to - from < 0.5) return;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(PointAt(centre, radius, from), false);
            g.ArcTo(PointAt(centre, radius, to), new Size(radius, radius), 0, to - from > 180, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, ArcThickness, lineCap: PenLineCap.Round), geometry);
    }

    /// <summary>The centre mark of a pan knob: a short line across the track at the middle of the range.</summary>
    private void DrawTick(DrawingContext context, IBrush? brush, Point centre, double radius, double angle)
    {
        if (brush is null) return;
        context.DrawLine(new Pen(brush, PointerThickness / 2), PointAt(centre, radius - ArcThickness, angle), PointAt(centre, radius + ArcThickness, angle));
    }

    /// <summary>A point at <paramref name="radius"/> from the centre, at an angle in degrees clockwise from 12 o'clock.</summary>
    private static Point PointAt(Point centre, double radius, double degrees)
    {
        var a = degrees * Math.PI / 180;
        return new Point(centre.X + radius * Math.Sin(a), centre.Y - radius * Math.Cos(a));
    }
}
