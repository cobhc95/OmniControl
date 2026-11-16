using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace OmniControl.App.Icons;

/// <summary>
/// An SVG file's drawing, read for a user icon: its shapes as one path-data text in the file's own coordinates, its view box, and whether it
/// is drawn as lines (an outline icon, like the library's) with how thick. Only shapes are read (path, circle, ellipse, rect, line,
/// polyline, polygon); transforms, text, gradients and pictures inside it are not drawn.
/// </summary>
public sealed record SvgDrawing(string PathData, double X, double Y, double Width, double Height, bool Stroked, double StrokeWidth);

/// <summary>Reads an SVG file's shapes (pure: no UI). Null when the text is not an SVG with at least one shape.</summary>
public static class SvgShapes
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static SvgDrawing? Read(string text)
    {
        XElement root;
        try
        {
            // A DOCTYPE (drawing programs write one) is skipped, never fetched.
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            root = XElement.Load(reader);
        }
        catch (XmlException) { return null; } // not XML: not an SVG we can read
        if (root.Name.LocalName != "svg") return null;
        var parts = root.Descendants().Where(Drawn(root)).Select(PathOf).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return null;
        var (x, y, w, h) = ViewBox(root);
        if (w <= 0 || h <= 0) return null;
        var stroked = Attr(root, "fill") == "none" && Attr(root, "stroke") is { } stroke && stroke != "none";
        var width = Number(Attr(root, "stroke-width")) ?? 1;
        return new SvgDrawing(string.Join(' ', parts), x, y, w, h, stroked, width);
    }

    /// <summary>A shape counts unless it says it is neither filled nor stroked (Tabler's invisible frame).</summary>
    private static Func<XElement, bool> Drawn(XElement root) => e => !(Attr(e, "stroke") == "none" && (Attr(e, "fill") ?? Attr(root, "fill")) == "none");

    private static string PathOf(XElement e)
    {
        double N(string name) => Number(Attr(e, name)) ?? 0;
        string F(double v) => v.ToString("0.###", Inv);
        switch (e.Name.LocalName)
        {
            case "path": return Attr(e, "d") ?? string.Empty;
            case "line": return $"M{F(N("x1"))},{F(N("y1"))} L{F(N("x2"))},{F(N("y2"))}";
            case "rect":
                var (rx, ry, rw, rh) = (N("x"), N("y"), N("width"), N("height"));
                return rw > 0 && rh > 0 ? $"M{F(rx)},{F(ry)} H{F(rx + rw)} V{F(ry + rh)} H{F(rx)} Z" : string.Empty;
            case "circle": return Ellipse(N("cx"), N("cy"), N("r"), N("r"), F);
            case "ellipse": return Ellipse(N("cx"), N("cy"), N("rx"), N("ry"), F);
            case "polyline":
            case "polygon":
                var points = (Attr(e, "points") ?? string.Empty).Split(new[] { ' ', ',', '\n', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (points.Length < 4) return string.Empty;
                var pairs = Enumerable.Range(0, points.Length / 2).Select(i => $"{points[i * 2]},{points[i * 2 + 1]}");
                return "M" + string.Join(" L", pairs) + (e.Name.LocalName == "polygon" ? " Z" : string.Empty);
            default: return string.Empty;
        }
    }

    private static string Ellipse(double cx, double cy, double rx, double ry, Func<double, string> f) =>
        rx <= 0 || ry <= 0 ? string.Empty
            : $"M{f(cx - rx)},{f(cy)} A{f(rx)},{f(ry)} 0 1 0 {f(cx + rx)},{f(cy)} A{f(rx)},{f(ry)} 0 1 0 {f(cx - rx)},{f(cy)} Z";

    private static (double X, double Y, double W, double H) ViewBox(XElement root)
    {
        var box = (Attr(root, "viewBox") ?? string.Empty).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => Number(s) ?? 0).ToArray();
        if (box.Length == 4) return (box[0], box[1], box[2], box[3]);
        return (0, 0, Number(Attr(root, "width")) ?? 24, Number(Attr(root, "height")) ?? 24);
    }

    private static string? Attr(XElement e, string name) => e.Attribute(name)?.Value.Trim();

    private static double? Number(string? text) =>
        double.TryParse(new string((text ?? string.Empty).TakeWhile(c => char.IsDigit(c) || c is '.' or '-' or '+' or 'e' or 'E').ToArray()), NumberStyles.Float, Inv, out var v) ? v : null;
}
