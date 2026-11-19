using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// The drawing tools of the area overlay; None = the selection itself is moved and resized.
public enum DrawTool { None, Pen, Line, Arrow, Box, Marker, Text, Blur }

/// How the drawings look, in screen units (multiplied by the monitor's scale, so they look the same on every monitor): the sizes each
/// tool steps through (Ctrl+Shift+. and Ctrl+Shift+, or A+ / A-; the line widths of the pen, line, arrow and rectangle, the marker's width,
/// the text's size) and the one each starts at, the marker's see-through strength, the arrow head and the blur's square.
public static class MarkLook
{
    public static readonly IReadOnlyList<double> StrokeSizes = new double[] { 1, 2, 3, 5, 8, 12 };
    public static readonly IReadOnlyList<double> MarkerSizes = new double[] { 8, 14, 20, 28, 36 };
    public static readonly IReadOnlyList<double> TextSizes = new double[] { 12, 14, 16, 20, 24, 32, 40, 56 };
    public const double Stroke = 3, MarkerStroke = 14, TextSize = 20;
    public const double MarkerOpacity = 0.4, ArrowHead = 4;
    public const int BlurBlock = 10;

    /// The sizes a tool steps through and the one it starts at; null for a tool without a size (the blur, no tool).
    public static (IReadOnlyList<double> Sizes, double Start)? SizesOf(DrawTool tool) => tool switch
    {
        DrawTool.Pen or DrawTool.Line or DrawTool.Arrow or DrawTool.Box => (StrokeSizes, Stroke),
        DrawTool.Marker => (MarkerSizes, MarkerStroke),
        DrawTool.Text => (TextSizes, TextSize),
        _ => null,
    };

    /// A recording's "Highlight clicks" circle: its radius (screen units) and how strongly it tints (colour <c>Palette.Caution</c>).
    public const double ClickRadius = 18, ClickOpacity = 0.35;
}

/// One drawing on a screenshot, kept as a shape (vector) until the picture is exported. Every coordinate is in the picture's pixels, so
/// moving or resizing the selection leaves the drawings where they are; the overlay and the export clip them to the selection.
public abstract class Mark : IDisposable
{
    /// The finished drawing, as shown and exported.
    public abstract void Draw(DrawingContext context);

    /// The drawing at full strength, for a layer that is faded once (<see cref="InProgressOpacity"/> below 1); the finished drawing by default.
    public virtual void DrawSolid(DrawingContext context) => Draw(context);

    /// While it is being drawn: the part that no longer changes, kept as shapes that are not built again on each pointer move. Nothing by default.
    public virtual void DrawSettled(DrawingContext context) { }

    /// While it is being drawn: the part that changes on each pointer move (the whole drawing by default).
    public virtual void DrawGrowing(DrawingContext context) => Draw(context);

    /// The see-through strength of the drawing (1 = solid): it is drawn solid in a layer that is faded once with the others of its
    /// kind next to it, so where they overlap is not darker.
    public virtual double InProgressOpacity => 1;

    /// Where the finished drawing paints, as a few rectangles that hug its shape (picture pixels, the pen's width and its soft edge
    /// included): what the overlay paints again when the drawing comes or goes.
    public abstract IEnumerable<Rect> Covers();

    /// How far the drawing in progress has got (a free-hand line: its points); 0 for one that changes as a whole.
    public virtual int Version => 0;

    /// Where the drawing in progress may look different from how it looked at <paramref name="version"/> (everywhere by default).
    public virtual IEnumerable<Rect> CoversChangedSince(int version) => Covers();

    /// How far a pen's anti-aliased edge reaches beyond half its width.
    protected const double SoftEdge = 2;

    /// The rectangle around two points, widened by <paramref name="reach"/> on every side.
    protected static Rect Around(Point a, Point b, double reach) => MarkMath.Enclosing(a, b).Inflate(reach);

    public virtual void Dispose() { }

    protected static IPen PenOf(Color color, double width, double opacity = 1) =>
        new ImmutablePen(new ImmutableSolidColorBrush(color, opacity), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
}

/// A free-hand line: the pen, or the wide see-through marker. Finished, it is one line. While it is drawn it is cut into settled parts of
/// <see cref="PartLength"/> steps and the growing rest, so each pointer move builds and repaints only the short rest, not the whole line
///. With round ends and joins the parts together are exactly the one line.
public sealed class StrokeMark : Mark
{
    /// Steps per settled part: small enough that the growing rest stays cheap, large enough that parts settle rarely.
    private const int PartLength = 64;

    private readonly List<Point> _points;
    private readonly IPen _pen;      // finished: the colour at the tool's see-through strength
    private readonly IPen _solidPen; // in progress: the same colour solid; the overlay applies the strength to all parts at once
    private readonly double _opacity;
    private readonly List<StreamGeometry> _settled = new();
    private StreamGeometry? _whole, _growing; // built once per change of the points, not on every frame

    public StrokeMark(Point start, Color color, double width, double opacity)
    {
        _points = new List<Point> { start };
        _opacity = opacity;
        _pen = PenOf(color, width, opacity);
        _solidPen = opacity < 1 ? PenOf(color, width) : _pen;
    }

    public void Extend(Point point)
    {
        if (point == _points[^1]) return; // a move that went nowhere would only make the line longer to draw
        _points.Add(point);
        _whole = _growing = null;
        var start = GrowingStart;
        if (_points.Count - 1 - start == PartLength) _settled.Add(Line(start, _points.Count - 1));
    }

    /// The point the growing rest starts at: the last settled part's end.
    private int GrowingStart => _settled.Count * PartLength;

    public override double InProgressOpacity => _opacity;

    public override void Draw(DrawingContext context)
    {
        if (_points.Count == 1) context.DrawLine(_pen, _points[0], _points[0]);
        else context.DrawGeometry(null, _pen, _whole ??= Line(0, _points.Count - 1));
    }

    public override void DrawSolid(DrawingContext context)
    {
        if (_points.Count == 1) context.DrawLine(_solidPen, _points[0], _points[0]);
        else context.DrawGeometry(null, _solidPen, _whole ??= Line(0, _points.Count - 1));
    }

    public override void DrawSettled(DrawingContext context)
    {
        foreach (var part in _settled) context.DrawGeometry(null, _solidPen, part);
    }

    public override void DrawGrowing(DrawingContext context)
    {
        var start = GrowingStart;
        var last = _points.Count - 1;
        if (last > start) context.DrawGeometry(null, _solidPen, _growing ??= Line(start, last));
        else if (start == 0) context.DrawLine(_solidPen, _points[0], _points[0]); // a dot; a settled part's round end already covers its own last point
    }

    public override int Version => _points.Count;

    /// One rectangle per settled part's length of points, so a long scribble is not repainted as one big box.
    public override IEnumerable<Rect> Covers()
    {
        for (var from = 0; from < _points.Count; from += PartLength) yield return Bounds(from, Math.Min(from + PartLength, _points.Count - 1));
    }

    /// The points added since <paramref name="version"/>, with the one before them (the join to the line drawn already).
    public override IEnumerable<Rect> CoversChangedSince(int version)
    {
        yield return Bounds(Math.Clamp(version - 1, 0, _points.Count - 1), _points.Count - 1);
    }

    private Rect Bounds(int from, int to)
    {
        double left = _points[from].X, top = _points[from].Y, right = left, bottom = top;
        for (var i = from + 1; i <= to; i++)
        {
            var p = _points[i];
            left = Math.Min(left, p.X);
            top = Math.Min(top, p.Y);
            right = Math.Max(right, p.X);
            bottom = Math.Max(bottom, p.Y);
        }
        return Around(new Point(left, top), new Point(right, bottom), _pen.Thickness / 2 + SoftEdge);
    }

    /// The line through the points from index <paramref name="from"/> to <paramref name="to"/>.
    private StreamGeometry Line(int from, int to)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(_points[from], false);
        for (var i = from + 1; i <= to; i++) g.LineTo(_points[i]);
        g.EndFigure(false);
        return geometry;
    }
}

/// A straight line, with an arrow head at its end for the arrow tool.
public sealed class LineMark : Mark
{
    private readonly IPen _pen;
    private readonly IBrush _fill;
    private readonly double _width;
    private readonly bool _arrow;

    public Point From { get; }
    public Point To { get; set; }

    public LineMark(Point from, Color color, double width, bool arrow)
    {
        From = To = from;
        _pen = PenOf(color, width);
        _fill = new ImmutableSolidColorBrush(color);
        _width = width;
        _arrow = arrow;
    }

    public override void Draw(DrawingContext context)
    {
        if (!_arrow)
        {
            context.DrawLine(_pen, From, To);
            return;
        }
        var length = Math.Sqrt(Math.Pow(To.X - From.X, 2) + Math.Pow(To.Y - From.Y, 2));
        if (length < 1)
        {
            context.DrawLine(_pen, From, To);
            return;
        }
        var head = _width * MarkLook.ArrowHead;
        var ux = (To.X - From.X) / length;
        var uy = (To.Y - From.Y) / length;
        var baseX = To.X - ux * Math.Min(head, length);
        var baseY = To.Y - uy * Math.Min(head, length);
        context.DrawLine(_pen, From, new Point(baseX, baseY));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(To, true);
            g.LineTo(new Point(baseX - uy * head / 2, baseY + ux * head / 2));
            g.LineTo(new Point(baseX + uy * head / 2, baseY - ux * head / 2));
            g.EndFigure(true);
        }
        context.DrawGeometry(_fill, _pen, geometry);
    }

    /// The line in pieces of <see cref="PieceLength"/> pixels (a long slanted line is not repainted as one big box), and the arrow's head.
    public override IEnumerable<Rect> Covers()
    {
        var reach = _width / 2 + SoftEdge;
        var length = Math.Sqrt(Math.Pow(To.X - From.X, 2) + Math.Pow(To.Y - From.Y, 2));
        var pieces = Math.Max(1, (int)Math.Ceiling(length / PieceLength));
        for (var i = 0; i < pieces; i++) yield return Around(At((double)i / pieces), At((double)(i + 1) / pieces), reach);
        // the head's far corners are about 1.12 head lengths from its tip
        if (_arrow) yield return Around(To, To, _width * MarkLook.ArrowHead * 1.2 + reach);
    }

    private const double PieceLength = 128;

    private Point At(double t) => new(From.X + (To.X - From.X) * t, From.Y + (To.Y - From.Y) * t);
}

/// The pure geometry of the drawings.
public static class MarkMath
{
    /// The rectangle two corners enclose, whichever way the drag went. A <c>Rect(Point, Point)</c> made from a drag up or to the left has a
    /// negative width or height, and the renderer draws such a rectangle as nothing.
    public static Rect Enclosing(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

/// A rectangle outline.
public sealed class BoxMark : Mark
{
    private readonly IPen _pen;

    public Point From { get; }
    public Point To { get; set; }

    public BoxMark(Point from, Color color, double width)
    {
        From = To = from;
        _pen = PenOf(color, width);
    }

    public override void Draw(DrawingContext context) => context.DrawRectangle(null, _pen, MarkMath.Enclosing(From, To));

    /// Its four sides (the inside of a large box is never repainted for it).
    public override IEnumerable<Rect> Covers()
    {
        var box = MarkMath.Enclosing(From, To);
        var reach = _pen.Thickness / 2 + SoftEdge;
        yield return Around(box.TopLeft, box.TopRight, reach);
        yield return Around(box.BottomLeft, box.BottomRight, reach);
        yield return Around(box.TopLeft, box.BottomLeft, reach);
        yield return Around(box.TopRight, box.BottomRight, reach);
    }
}

/// A line of text written with the text tool.
public sealed class TextMark : Mark
{
    private readonly FormattedText _text;
    private readonly Point _at;

    public TextMark(Point at, string text, Color color, double size)
    {
        _at = at;
        _text = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, size, new ImmutableSolidColorBrush(color));
    }

    public override void Draw(DrawingContext context) => context.DrawText(_text, _at);

    /// The text's box, widened by a quarter of its line height for letters that reach past it.
    public override IEnumerable<Rect> Covers()
    {
        yield return new Rect(_at, new Size(_text.WidthIncludingTrailingWhitespace, _text.Height)).Inflate(_text.Height / 4 + SoftEdge);
    }
}

/// A blurred (pixelated) area: the coarse picture made from the screenshot when it was drawn, stretched over the area without smoothing.
public sealed class BlurMark : Mark
{
    private readonly Bitmap _coarse;
    private readonly ScreenRect _area;
    private readonly int _block;

    /// <param name="part">The screenshot's pixels inside <paramref name="area"/> (picture pixels), as <see cref="ImageBitmaps.Crop"/> gives them.</param>
    public BlurMark(ScreenImage part, ScreenRect area, int block)
    {
        _area = area;
        _block = block;
        _coarse = ImageBitmaps.ToBitmap(Pixelation.Blocks(part, part.Bounds, block));
    }

    public override void Draw(DrawingContext context)
    {
        using (context.PushClip(new Rect(_area.X, _area.Y, _area.Width, _area.Height)))
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            context.DrawImage(_coarse, new Rect(_area.X, _area.Y, _coarse.PixelSize.Width * _block, _coarse.PixelSize.Height * _block));
    }

    public override IEnumerable<Rect> Covers()
    {
        yield return new Rect(_area.X, _area.Y, _area.Width, _area.Height);
    }

    public override void Dispose() => _coarse.Dispose();
}
