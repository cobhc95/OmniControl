using Avalonia.Input;
using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Fx;

/// The larger graph of the chain's total response (the FX window's pop-up; <see cref="FxAnalysisViewModel"/> is its DataContext):
/// frequency and dB axes with the same automatic scale as the thumbnail, the grid, the total curve (measured: with the EQ and gain
/// curve faint and dashed behind it), the peak marked (in the caution
/// colour above the default scale), and a line that follows the pointer with the response there ("1.00 kHz / +24.0 dB").
public sealed class ResponseGraph : ItemDrawing<FxAnalysisViewModel>
{
    private const double Corner = 8, CurveThickness = 2, PeakRadius = 4, ReadoutPad = 3, ReadoutGap = 6, ReferenceThickness = 1.2;

    /// The faint EQ and gain reference behind a measured curve: dashes and gaps in pen widths.
    private static readonly double[] ReferenceDash = { 3, 2.5 };

    /// Where the pointer is across the plot; null while it is not over the graph.
    private double? _pointerX;

    public ResponseGraph() => ClipToBounds = true;

    private PlotFrame FrameOf(FxAnalysisViewModel item) => new(
        new Rect(ResponsePlot.AxisLeft, ResponsePlot.AxisTop,
            Math.Max(1, Bounds.Width - ResponsePlot.AxisLeft - ResponsePlot.AxisRight),
            Math.Max(1, Bounds.Height - ResponsePlot.AxisTop - ResponsePlot.AxisBottom)),
        Eq.MinFreq, Eq.MaxFreq, item.ScaleDb, -item.ScaleDb);

    public override void Render(DrawingContext ctx)
    {
        var muted = DrawKit.Brush(this, "BrushMuted");
        var card = DrawKit.Brush(this, "BrushBackground");
        var divider = DrawKit.Brush(this, "BrushDivider");
        ctx.DrawRectangle(card, new Pen(divider, 1), new Rect(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1), Corner, Corner);
        if (Item is not { } item) return;

        var frame = FrameOf(item);
        var fine = new Pen(divider, 0.5);
        ResponsePlot.FreqAxis(ctx, frame, fine, muted, Bounds.Width);
        ResponsePlot.DbAxis(ctx, frame, new Pen(muted, 1), fine, muted);

        var accent = DrawKit.Brush(this, "BrushAccent");
        if (item.IsMeasured)
            ctx.DrawGeometry(null, new Pen(muted, ReferenceThickness, new DashStyle(ReferenceDash, 0)),
                ResponsePlot.Line(frame.Points(FxAnalysisViewModel.Freqs, item.Reference)));
        ctx.DrawGeometry(null, new Pen(item.IsOn ? accent : muted, CurveThickness, lineJoin: PenLineJoin.Round),
            ResponsePlot.Line(frame.Points(FxAnalysisViewModel.Freqs, item.Curve)));
        if (item.HasBoost)
            ctx.DrawEllipse(item.PeakIsHigh ? DrawKit.Brush(this, "BrushCaution") : accent, null,
                new Point(frame.X(item.PeakHz), frame.Y(item.PeakDb)), PeakRadius, PeakRadius);
        if (_pointerX is { } x) DrawCrosshair(ctx, item, frame, x, muted, card);
    }

    /// The line at the pointer's frequency, a dot on the curve there, and the readout beside the line at the top of the plot.
    private static void DrawCrosshair(DrawingContext ctx, FxAnalysisViewModel item, PlotFrame frame, double x, IBrush muted, IBrush card)
    {
        x = Math.Clamp(x, frame.Area.Left, frame.Area.Right);
        var hz = frame.HzAt(x);
        var db = item.DbAt(hz);
        ctx.DrawLine(new Pen(muted, 1), new Point(x, frame.Area.Top), new Point(x, frame.Area.Bottom));
        ctx.DrawEllipse(muted, null, new Point(x, frame.Y(db)), PeakRadius - 1, PeakRadius - 1);

        var text = DrawKit.Text($"{DrawKit.ReadoutFreq(hz)} / {DrawKit.ReadoutDb(db)}", DrawKit.LabelSize, muted);
        var left = x + ReadoutGap + text.Width + ReadoutPad <= frame.Area.Right ? x + ReadoutGap : x - ReadoutGap - text.Width;
        var box = new Rect(left - ReadoutPad, frame.Area.Top, text.Width + 2 * ReadoutPad, text.Height);
        ctx.DrawRectangle(card, null, box, ReadoutPad, ReadoutPad);
        ctx.DrawText(text, new Point(left, frame.Area.Top));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointerX = e.GetPosition(this).X;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerX = null;
        InvalidateVisual();
    }
}
