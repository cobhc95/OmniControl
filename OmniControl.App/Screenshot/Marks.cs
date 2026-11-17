using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Screenshot;

/// <summary>The drawing tools of the area overlay; None = the selection itself is moved and resized.</summary>
public enum DrawTool { None, Pen, Line, Arrow, Box, Marker, Text, Blur }

/// <summary>
/// How the drawings look, in screen units (multiplied by the monitor's scale, so they look the same on every monitor): the sizes each
/// tool steps through (Ctrl+Shift+. and Ctrl+Shift+, or A+ / A-; the line widths of the pen, line, arrow and rectangle, the marker's width,
/// the text's size) and the one each starts at, the marker's see-through strength, the arrow head and the blur's square.
/// </summary>
public static class MarkLook
{
    public static readonly IReadOnlyList<double> StrokeSizes = new double[] { 1, 2, 3, 5, 8, 12 };
    public static readonly IReadOnlyList<double> MarkerSizes = new double[] { 8, 14, 20, 28, 36 };
    public static readonly IReadOnlyList<double> TextSizes = new double[] { 12, 14, 16, 20, 24, 32, 40, 56 };
    public const double Stroke = 3, MarkerStroke = 14, TextSize = 20;
    public const double MarkerOpacity = 0.4, ArrowHead = 4;
    public const int BlurBlock = 10;

    /// <summary>The sizes a tool steps through and the one it starts at; null for a tool without a size (the blur, no tool).</summary>
    public static (IReadOnlyList<double> Sizes, double Start)? SizesOf(DrawTool tool) => tool switch
    {
        DrawTool.Pen or DrawTool.Line or DrawTool.Arrow or DrawTool.Box => (StrokeSizes, Stroke),
        DrawTool.Marker => (MarkerSizes, MarkerStroke),
        DrawTool.Text => (TextSizes, TextSize),
        _ => null,
    };

    /// <summary>A recording's "Highlight clicks" circle: its radius (screen units) and how strongly it tints (colour <c>Palette.Caution</c>).</summary>
    public const double ClickRadius = 18, ClickOpacity = 0.35;
}

/// <summary>
/// One drawing on a screenshot, kept as a shape (vector) until the picture is exported. Every coordinate is in the picture's pixels, so
/// moving or resizing the selection leaves the drawings where they are; the overlay and the export clip them to the selection.
/// </summary>
public abstract class Mark : IDisposable
{
    public abstract void Draw(DrawingContext context);

    public virtual void Dispose() { }

    protected static IPen PenOf(Color color, double width, double opacity = 1) =>
        new ImmutablePen(new ImmutableSolidColorBrush(color, opacity), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
}

/// <summary>A free-hand line: the pen, or the wide see-through marker.</summary>
public sealed class StrokeMark : Mark
{
    private readonly List<Point> _points;
    private readonly IPen _pen;
    private StreamGeometry? _geometry; // built once per change of the points, not on every frame

    public StrokeMark(Point start, Color color, double width, double opacity)
    {
        _points = new List<Point> { start };
        _pen = PenOf(color, width, opacity);
    }

    public void Extend(Point point)
    {
        if (point == _points[^1]) return; // a move that went nowhere would only make the line longer to draw
        _points.Add(point);
        _geometry = null;
    }

    public override void Draw(DrawingContext context)
    {
        if (_points.Count == 1)
        {
            context.DrawLine(_pen, _points[0], _points[0]);
            return;
        }
        if (_geometry is null)
        {
            _geometry = new StreamGeometry();
            using var g = _geometry.Open();
            g.BeginFigure(_points[0], false);
            for (var i = 1; i < _points.Count; i++) g.LineTo(_points[i]);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, _pen, _geometry);
    }
}

/// <summary>A straight line, with an arrow head at its end for the arrow tool.</summary>
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
}

/// <summary>The pure geometry of the drawings.</summary>
public static class MarkMath
{
    /// <summary>
    /// The rectangle two corners enclose, whichever way the drag went. A <c>Rect(Point, Point)</c> made from a drag up or to the left has a
    /// negative width or height, and the renderer draws such a rectangle as nothing.
    /// </summary>
    public static Rect Enclosing(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

/// <summary>A rectangle outline.</summary>
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
}

/// <summary>A line of text written with the text tool.</summary>
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
}

/// <summary>A blurred (pixelated) area: the coarse picture made from the screenshot when it was drawn, stretched over the area without smoothing.</summary>
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

    public override void Dispose() => _coarse.Dispose();
}
