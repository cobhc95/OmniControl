using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Fx;

/// The total response of a graphic equaliser (its bands and overall gain, from <see cref="EqMath.CurveDb"/>, the maths the engine
/// runs), drawn above the sliders over the same columns: each band's centre sits above its slider. The vertical scale is the sliders'
/// range; the band last changed is read out in the corner.
public sealed class GraphicEqCurve : GraphicEqDrawing
{
    private const double Pad = 6, Corner = 8, CurveThickness = 2;

    /// Curve points per band column.
    private const int PointsPerBand = 8;

    public GraphicEqCurve() => ClipToBounds = true;

    public override void Render(DrawingContext ctx)
    {
        var accent = DrawKit.Brush(this, "BrushAccent");
        var muted = DrawKit.Brush(this, "BrushMuted");
        var divider = DrawKit.Brush(this, "BrushDivider");
        ctx.DrawRectangle(DrawKit.Brush(this, "BrushBackground"), new Pen(divider, 1), new Rect(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1), Corner, Corner);
        if (Item is not { Bands.Count: > 1 } item) return;

        var range = item.RangeDb;
        double Y(double db) => Pad + (Bounds.Height - 2 * Pad) * (range - Math.Clamp(db, -range, range)) / (2 * range);
        var fine = new Pen(divider, 0.5);
        ctx.DrawLine(fine, new Point(0, Y(range / 2)), new Point(Bounds.Width, Y(range / 2)));
        ctx.DrawLine(fine, new Point(0, Y(-range / 2)), new Point(Bounds.Width, Y(-range / 2)));
        ctx.DrawLine(new Pen(muted, 1), new Point(0, Y(0)), new Point(Bounds.Width, Y(0)));

        var points = item.Bands.Count * PointsPerBand;
        var xs = Enumerable.Range(0, points + 1).Select(i => Bounds.Width * i / points).ToArray();
        var curve = EqMath.CurveDb(item.Export(), item.OverallGainDb, xs.Select(x => FreqAt(item, x)).ToList());

        var curvePoints = xs.Select((x, i) => new Point(x, Y(curve[i]))).ToList();
        ctx.DrawGeometry(DrawKit.Brush(this, "BrushAccentSoft"), null, ResponsePlot.Fill(curvePoints, Y(0)));
        ctx.DrawGeometry(null, new Pen(accent, CurveThickness, lineJoin: PenLineJoin.Round), ResponsePlot.Line(curvePoints));

        if (item.Selected is { } band)
            ctx.DrawText(DrawKit.Text($"{band.FreqText} · {band.GainText}", DrawKit.InfoSize, muted), new Point(Pad, Pad / 2));
    }

    /// The frequency at <paramref name="x"/>: the centres are evenly spaced on a log scale, one per column, so the first and last
    /// centres fix the scale and each centre lands in the middle of its column. Held below what the graph rate can show.
    private double FreqAt(GraphicEqItem item, double x)
    {
        var first = item.Bands[0].Freq;
        var perBand = Math.Log(item.Bands[^1].Freq / first) / (item.Bands.Count - 1);
        return Math.Min(first * Math.Exp(perBand * (x / ColumnWidth - 0.5)), EqMath.GraphSampleRate * EqMath.MaxFreqShare);
    }
}
