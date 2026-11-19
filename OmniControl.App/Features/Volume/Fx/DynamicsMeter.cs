namespace OmniControl.App.Features.Volume.Fx;

/// Which of a <see cref="DynamicsItem"/>'s two meters a <see cref="DynamicsMeter"/> draws.
public enum DynamicsMeterKind { GainReduction, Level }

/// One of the compressor's and the limiter's two horizontal level bars (TabForge's MeterBar, MIT): gain reduction grows from the right in
/// the caution colour, a level from the left in the "on" green, turning caution and then danger as it gets hot. Empty while nothing is
/// measured. Draws only; the shares and the tone are on its <see cref="DynamicsItem"/>.
public sealed class DynamicsMeter : ItemDrawing<DynamicsItem>
{
    private const double Radius = 3, BarHeight = 14, Thickness = 1;

    public static readonly StyledProperty<DynamicsMeterKind> KindProperty = AvaloniaProperty.Register<DynamicsMeter, DynamicsMeterKind>(nameof(Kind));

    static DynamicsMeter() => AffectsRender<DynamicsMeter>(KindProperty);

    public DynamicsMeter() => Height = BarHeight;

    public DynamicsMeterKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (Item is not { } item || size.Width < 4 || size.Height < 2) return;
        ctx.DrawRectangle(DrawKit.Brush(this, "BrushBackground"), new Pen(DrawKit.Brush(this, "BrushControlEdge"), Thickness), new Rect(size), Radius, Radius);
        var reduction = Kind == DynamicsMeterKind.GainReduction;
        var fraction = reduction ? item.ReductionFraction : item.LevelFraction;
        if (fraction <= 0) return;
        var width = Math.Max(Radius * 2, size.Width * fraction);
        var brush = reduction ? "BrushCaution" : item.LevelTone switch
        {
            DynamicsLevelTone.Hot => "BrushDanger",
            DynamicsLevelTone.Warm => "BrushCaution",
            _ => "BrushOn",
        };
        ctx.DrawRectangle(DrawKit.Brush(this, brush), null, new Rect(reduction ? size.Width - width : 0, 0, width, size.Height), Radius, Radius);
    }
}
