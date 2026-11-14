using Avalonia;
using Avalonia.Media;
using OmniControl.App.Audio;

namespace OmniControl.App.Fx;

/// <summary>
/// The FX window toolbar's small graph of the chain's total response (<see cref="FxAnalysisViewModel"/>, its DataContext): 20 Hz to
/// 20 kHz across, the 0 dB line in the middle, the curve in the accent (muted while effects are off), the scale top right, and the
/// peak marked in the caution colour when it is above the default scale. The frame around it is its button's (Button.graphthumb).
/// </summary>
public sealed class ResponseThumb : ItemDrawing<FxAnalysisViewModel>
{
    private const double Pad = 3, CurveThickness = 1.5, ZeroThickness = 0.5, PeakRadius = 2, LabelInset = 3;

    public ResponseThumb() => ClipToBounds = true;

    public override void Render(DrawingContext ctx)
    {
        if (Item is not { } item) return;
        var muted = DrawKit.Brush(this, "BrushMuted");
        var frame = new PlotFrame(new Rect(0, Pad, Bounds.Width, Math.Max(1, Bounds.Height - 2 * Pad)), Eq.MinFreq, Eq.MaxFreq, item.ScaleDb, -item.ScaleDb);

        ctx.DrawLine(new Pen(muted, ZeroThickness), new Point(0, frame.Y(0)), new Point(Bounds.Width, frame.Y(0)));
        var label = DrawKit.Text(item.ScaleText, DrawKit.SmallLabelSize, muted);
        ctx.DrawText(label, new Point(Bounds.Width - LabelInset - label.Width, 0));

        var curve = item.IsOn ? DrawKit.Brush(this, "BrushAccent") : muted;
        ctx.DrawGeometry(null, new Pen(curve, CurveThickness, lineJoin: PenLineJoin.Round),
            ResponsePlot.Line(frame.Points(FxAnalysisViewModel.Freqs, item.Curve)));
        if (item.PeakIsHigh)
            ctx.DrawEllipse(DrawKit.Brush(this, "BrushCaution"), null, new Point(frame.X(item.PeakHz), frame.Y(item.PeakDb)), PeakRadius, PeakRadius);
    }
}
