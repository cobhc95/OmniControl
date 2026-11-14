using System.Globalization;
using Avalonia;
using Avalonia.Media;
using OmniControl.Core.Models;

namespace OmniControl.App.Fx;

/// <summary>
/// The compressor's transfer curve, drawn as TabForge draws it (MIT): output level against input level from -60 to 0 dB in a square plot,
/// the grid every 10 dB, the 1:1 line, the threshold marked, the curve (from <see cref="CompressorCurve"/>, the function the sound goes
/// through) and a dot at the input level the compressor hears now. Draws only; every number is on its <see cref="DynamicsItem"/>.
/// The geometry is built again only when the settings or the size changed.
/// </summary>
public sealed class TransferGraph : ItemDrawing<DynamicsItem>
{
    private const double FloorDb = DynamicsItem.CurveFloorDb, GridStepDb = 10, CurveStepDb = 1, DotStepDb = 0.5;
    private const double Left = ResponsePlot.AxisLeft, Top = ResponsePlot.AxisTop, Right = ResponsePlot.AxisRight, Bottom = ResponsePlot.AxisBottom;
    private const double GridThickness = 1, MarkerThickness = 1.5, CurveThickness = 2, DotRadius = 5, LabelGap = 4;

    private StreamGeometry? _curve;
    private (double Threshold, double Ratio, double Knee, Size Size) _built;

    private Rect Plot
    {
        get
        {
            var side = Math.Max(1, Math.Min(Bounds.Width - Left - Right, Bounds.Height - Top - Bottom));
            return new Rect(Left, Top, side, side);
        }
    }

    private Point At(Rect plot, double inDb, double outDb) =>
        new(plot.X + plot.Width * (Math.Clamp(inDb, FloorDb, 0) - FloorDb) / -FloorDb, plot.Y + plot.Height * (0 - Math.Clamp(outDb, FloorDb, 0)) / -FloorDb);

    public override void Render(DrawingContext ctx)
    {
        if (Item is not { } item || Bounds.Width < 40 || Bounds.Height < 40) return;
        var plot = Plot;
        var muted = DrawKit.Brush(this, "BrushMuted");
        var grid = new Pen(DrawKit.Brush(this, "BrushDivider"), GridThickness);
        ctx.DrawRectangle(DrawKit.Brush(this, "BrushBackground"), null, plot);
        for (var db = FloorDb; db <= 0; db += GridStepDb)
        {
            ctx.DrawLine(grid, At(plot, db, FloorDb), At(plot, db, 0));
            ctx.DrawLine(grid, At(plot, FloorDb, db), At(plot, 0, db));
            var label = DrawKit.Text(db.ToString("0", CultureInfo.InvariantCulture), DrawKit.LabelSize, muted);
            ctx.DrawText(label, new Point(At(plot, db, FloorDb).X - label.Width / 2, plot.Bottom + LabelGap));
            ctx.DrawText(label, new Point(plot.Left - label.Width - LabelGap, At(plot, FloorDb, db).Y - label.Height / 2));
        }
        ctx.DrawRectangle(grid, plot);
        ctx.DrawLine(new Pen(muted, GridThickness), At(plot, FloorDb, FloorDb), At(plot, 0, 0));
        ctx.DrawLine(new Pen(DrawKit.Brush(this, "BrushCaution"), MarkerThickness), At(plot, item.Threshold, FloorDb), At(plot, item.Threshold, 0));
        var curve = _curve is null || _built != (item.Threshold, item.Ratio, item.Knee, Bounds.Size) ? Build(item, plot) : _curve;
        ctx.DrawGeometry(null, new Pen(DrawKit.Brush(this, "BrushAccent"), CurveThickness), curve);
        if (item.InputDb is { } input)
        {
            var shown = Math.Round(input / DotStepDb) * DotStepDb; // half a dB steps: the dot does not shiver
            var dot = At(plot, shown, CompressorCurve.OutputDb(shown, item.Threshold, item.Ratio, item.Knee));
            ctx.DrawEllipse(DrawKit.Brush(this, "BrushOn"), new Pen(DrawKit.Brush(this, "BrushBackground"), GridThickness), dot, DotRadius, DotRadius);
        }
    }

    private StreamGeometry Build(DynamicsItem item, Rect plot)
    {
        _built = (item.Threshold, item.Ratio, item.Knee, Bounds.Size);
        var curve = new StreamGeometry();
        using (var g = curve.Open())
        {
            for (var db = FloorDb; db <= 0; db += CurveStepDb)
            {
                var point = At(plot, db, CompressorCurve.OutputDb(db, item.Threshold, item.Ratio, item.Knee));
                if (db == FloorDb) g.BeginFigure(point, false); else g.LineTo(point);
            }
            g.EndFigure(false);
        }
        return _curve = curve;
    }
}
