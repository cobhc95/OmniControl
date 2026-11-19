namespace OmniControl.App.Features.Volume.Fx;

/// Where a response graph draws: a plot rectangle, frequency across on a log scale and dB upwards (values outside are held at the edge).
internal readonly record struct PlotFrame(Rect Area, double MinHz, double MaxHz, double TopDb, double BottomDb)
{
    public double X(double hz) => Area.Left + Area.Width * Math.Log(hz / MinHz) / Math.Log(MaxHz / MinHz);
    public double Y(double db) => Area.Top + Area.Height * (TopDb - Math.Clamp(db, BottomDb, TopDb)) / (TopDb - BottomDb);
    public double HzAt(double x) => MinHz * Math.Pow(MaxHz / MinHz, Math.Clamp((x - Area.Left) / Area.Width, 0, 1));
    public double DbAt(double y) => TopDb - (TopDb - BottomDb) * Math.Clamp((y - Area.Top) / Area.Height, 0, 1);

    /// The points of a curve given at <paramref name="hz"/>.
    public Point[] Points(IReadOnlyList<double> hz, IReadOnlyList<double> db)
    {
        var points = new Point[hz.Count];
        for (var i = 0; i < points.Length; i++) points[i] = new Point(X(hz[i]), Y(db[i]));
        return points;
    }
}

/// The drawing every response graph shares (the Parametric's <see cref="EqGraph"/>, the graphic equaliser's curve, the FX window's
/// total response): the frequency and dB axes with their grid and labels, and a curve as a line or filled down to a level.
internal static class ResponsePlot
{
    /// Room around a full graph's plot for its labels: dB marks on the left, frequencies underneath.
    public const double AxisLeft = 34, AxisTop = 10, AxisRight = 10, AxisBottom = 22;

    /// The frequencies a full graph marks.
    private static readonly double[] GridFreqs = { 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 };

    /// A dB grid line every <see cref="GridStepDb"/>, labelled every <see cref="LabelStepDb"/>.
    private const double GridStepDb = 6, LabelStepDb = 12;

    /// Gap between the plot and its frequency labels, and between a dB label and the plot.
    private const double FreqLabelGap = 4, DbLabelGap = 6;

    /// A vertical line at each marked frequency (when <paramref name="fine"/> is given) and its label under the plot, kept inside <paramref name="width"/>.
    public static void FreqAxis(DrawingContext ctx, PlotFrame frame, IPen? fine, IBrush labels, double width)
    {
        foreach (var hz in GridFreqs)
        {
            var x = frame.X(hz);
            if (fine is not null) ctx.DrawLine(fine, new Point(x, frame.Area.Top), new Point(x, frame.Area.Bottom));
            var label = DrawKit.Text(DrawKit.ShortFreq(hz), DrawKit.LabelSize, labels);
            ctx.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, 0, width - label.Width), frame.Area.Bottom + FreqLabelGap));
        }
    }

    /// The 0 dB line (<paramref name="zero"/>), the other grid lines (when <paramref name="fine"/> is given) and the labels left of the plot.
    public static void DbAxis(DrawingContext ctx, PlotFrame frame, IPen zero, IPen? fine, IBrush labels)
    {
        for (var db = Math.Ceiling(frame.BottomDb / GridStepDb) * GridStepDb; db <= frame.TopDb; db += GridStepDb)
        {
            var y = frame.Y(db);
            if (db == 0) ctx.DrawLine(zero, new Point(frame.Area.Left, y), new Point(frame.Area.Right, y));
            else if (fine is not null) ctx.DrawLine(fine, new Point(frame.Area.Left, y), new Point(frame.Area.Right, y));
            if (db % LabelStepDb != 0) continue;
            var label = DrawKit.Text(db > 0 ? $"+{db:0}" : $"{db:0}", DrawKit.LabelSize, labels);
            ctx.DrawText(label, new Point(frame.Area.Left - DbLabelGap - label.Width, y - label.Height / 2));
        }
    }

    /// A curve through <paramref name="points"/>, as a line.
    public static StreamGeometry Line(IReadOnlyList<Point> points)
    {
        var line = new StreamGeometry();
        using var g = line.Open();
        g.BeginFigure(points[0], false);
        for (var i = 1; i < points.Count; i++) g.LineTo(points[i]);
        g.EndFigure(false);
        return line;
    }

    /// The area between a curve through <paramref name="points"/> and the level <paramref name="baseY"/> (a band's or a curve's share above or below 0 dB).
    public static StreamGeometry Fill(IReadOnlyList<Point> points, double baseY)
    {
        var fill = new StreamGeometry();
        using var g = fill.Open();
        g.BeginFigure(new Point(points[0].X, baseY), true);
        foreach (var p in points) g.LineTo(p);
        g.LineTo(new Point(points[^1].X, baseY));
        g.EndFigure(true);
        return fill;
    }
}
