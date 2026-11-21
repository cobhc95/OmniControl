using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// A small stereo level meter standing upright: two thin bars (left, right) that rise from the bottom, green up to the warm level
/// and amber above it, a peak-hold tick and a red clip mark at the top. Colours and parts follow TabForge's track meter.
/// Drawing only: the numbers come from a <see cref="MeterModel"/> that the Volume page feeds.
public sealed class LevelMeter : Control
{
    public static readonly StyledProperty<MeterModel?> ModelProperty = AvaloniaProperty.Register<LevelMeter, MeterModel?>(nameof(Model));

    private const double BarWidth = 5, Gap = 2, Pad = 2, Radius = 1.5, FrameRadius = 3, HoldHeight = 1.5, ClipMark = 3, MeterHeight = 32;
    private static readonly IBrush Green = new SolidColorBrush(Palette.MeterGreen);
    private static readonly IBrush Amber = new SolidColorBrush(Palette.MeterAmber);
    private static readonly IBrush Red = new SolidColorBrush(Palette.Danger);
    private MeterModel? _watching;
    private Pen? _frame; // made again only when the theme gives another edge brush, not every frame

    static LevelMeter()
    {
        AffectsRender<LevelMeter>(ModelProperty);
        ModelProperty.Changed.AddClassHandler<LevelMeter>((m, _) => m.Watch());
    }

    public LevelMeter()
    {
        Width = BarWidth * 2 + Gap + Pad * 2;
        Height = MeterHeight;
        IsHitTestVisible = false;
    }

    public MeterModel? Model { get => GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    private void Watch()
    {
        if (_watching is not null) _watching.Changed -= InvalidateVisual;
        _watching = Model;
        if (_watching is not null) _watching.Changed += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_watching is not null) _watching.Changed -= InvalidateVisual;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch();
    }

    public override void Render(DrawingContext context)
    {
        var frame = Bounds.Height;
        if (frame < 4 + Pad * 2) return;
        var height = frame - Pad * 2;
        var edge = Fx.DrawKit.Brush(this, "BrushControlEdge");
        if (_frame?.Brush != edge) _frame = new Pen(edge, 1);
        context.DrawRectangle(null, _frame, new RoundedRect(new Rect(0.5, 0.5, Bounds.Width - 1, frame - 1), FrameRadius));
        using var _ = context.PushTransform(Matrix.CreateTranslation(Pad, Pad));
        var track = Fx.DrawKit.Brush(this, "BrushControlTrack");
        var hold = Fx.DrawKit.Brush(this, "BrushMuted");
        var m = Model;
        var clip = m?.ClipLit == true;
        DrawBar(context, track, hold, 0, height, m?.LeftDb ?? MeterModel.FloorDb, m?.HoldLeftDb ?? MeterModel.FloorDb, clip);
        DrawBar(context, track, hold, BarWidth + Gap, height, m?.RightDb ?? MeterModel.FloorDb, m?.HoldRightDb ?? MeterModel.FloorDb, clip);
    }

    private static void DrawBar(DrawingContext c, IBrush track, IBrush hold, double x, double height, double db, double holdDb, bool clip)
    {
        c.DrawRectangle(track, null, new RoundedRect(new Rect(x, 0, BarWidth, height), Radius));
        var fraction = MeterModel.Fraction(db);
        var warm = MeterModel.Fraction(MeterModel.WarmDb);
        if (fraction > 0)
        {
            var green = Math.Min(fraction, warm) * height;
            c.DrawRectangle(Green, null, new Rect(x, height - green, BarWidth, green));
            if (fraction > warm) c.DrawRectangle(Amber, null, new Rect(x, height * (1 - fraction), BarWidth, (fraction - warm) * height));
        }
        if (holdDb > MeterModel.FloorDb)
            c.DrawRectangle(hold, null, new Rect(x, Math.Min(height - HoldHeight, height * (1 - MeterModel.Fraction(holdDb))), BarWidth, HoldHeight));
        if (clip) c.DrawRectangle(Red, null, new Rect(x, 0, BarWidth, ClipMark));
    }
}
