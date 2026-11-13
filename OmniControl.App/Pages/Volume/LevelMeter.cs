using OmniControl.App.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// A small stereo level meter standing upright: two thin bars (left, right) that rise from the bottom, green up to the warm level
/// and amber above it, a peak-hold tick and a red clip mark at the top. Colours and parts follow TabForge's track meter.
/// Drawing only: the numbers come from a <see cref="MeterModel"/> that the Volume page feeds.
/// </summary>
public sealed class LevelMeter : Control
{
    public static readonly StyledProperty<MeterModel?> ModelProperty = AvaloniaProperty.Register<LevelMeter, MeterModel?>(nameof(Model));

    private const double BarWidth = 5, Gap = 2, Radius = 1.5, HoldHeight = 1.5, ClipMark = 3, MeterHeight = 28;
    private static readonly IBrush Green = new SolidColorBrush(Palette.MeterGreen);
    private static readonly IBrush Amber = new SolidColorBrush(Palette.MeterAmber);
    private static readonly IBrush Red = new SolidColorBrush(Palette.Danger);
    private MeterModel? _watching;

    static LevelMeter()
    {
        AffectsRender<LevelMeter>(ModelProperty);
        ModelProperty.Changed.AddClassHandler<LevelMeter>((m, _) => m.Watch());
    }

    public LevelMeter()
    {
        Width = BarWidth * 2 + Gap;
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
        var height = Bounds.Height;
        if (height < 4) return;
        var track = this.TryFindResource("BrushDivider", ActualThemeVariant, out var t) && t is IBrush b ? b : Brushes.Gray;
        var hold = this.TryFindResource("BrushMuted", ActualThemeVariant, out var h) && h is IBrush hb ? hb : Brushes.Silver;
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
