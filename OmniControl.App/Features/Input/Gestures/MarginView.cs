using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Gestures;

/// How a gesture area looks on screen. A screen-edge strip is a see-through solid accent fill with a dashed inner edge. The taskbar and
/// a window's title bar belong to a window, not the screen edge, so they look different: a light fill with diagonal hatching and a
/// dashed outline all round, set one pixel in so a title bar under a top strip reads as its own area. It draws to whatever size it is
/// given, so its exact pixel size is the area's size.
internal sealed class MarginArea : Control
{
    // See-through enough that what lies under the strip stays readable.
    private const byte EdgeFillAlpha = 0x60, WindowFillAlpha = 0x20;
    private const double HatchSpacing = 8, Gap = 1;
    private static readonly IReadOnlyList<double> Dash = new double[] { 4, 3 };

    private readonly PointerAreaKind _kind;
    private readonly IBrush _fill;
    private readonly IPen _edge;

    public MarginArea(PointerAreaKind kind, Color accent)
    {
        _kind = kind;
        var alpha = IsWindowOwned(kind) ? WindowFillAlpha : EdgeFillAlpha;
        _fill = new SolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B));
        _edge = new Pen(new SolidColorBrush(accent), 1, new DashStyle(Dash, 0));
        IsHitTestVisible = false;
    }

    private static bool IsWindowOwned(PointerAreaKind kind) => kind is PointerAreaKind.Taskbar or PointerAreaKind.TitleBar;

    public override void Render(DrawingContext context)
    {
        var box = new Rect(Bounds.Size);
        if (IsWindowOwned(_kind))
        {
            RenderWindowOwned(context, box.Deflate(Gap));
            return;
        }
        context.FillRectangle(_fill, box);
        // Half a line in from the edge, so the whole line is inside the area.
        var inner = box.Deflate(_edge.Thickness / 2);
        switch (_kind)
        {
            case PointerAreaKind.TopEdge: context.DrawLine(_edge, inner.BottomLeft, inner.BottomRight); break;
            case PointerAreaKind.LeftEdge: context.DrawLine(_edge, inner.TopRight, inner.BottomRight); break;
            default: context.DrawLine(_edge, inner.TopLeft, inner.BottomLeft); break;
        }
    }

    private void RenderWindowOwned(DrawingContext context, Rect box)
    {
        if (box.Width <= 0 || box.Height <= 0) return;
        context.FillRectangle(_fill, box);
        using (context.PushClip(box))
            for (var x = box.Left - box.Height; x < box.Right; x += HatchSpacing)
                context.DrawLine(_edge, new Point(x, box.Bottom), new Point(x + box.Height, box.Top));
        context.DrawRectangle(null, _edge, box.Deflate(_edge.Thickness / 2));
    }
}

/// The small name label of a gesture area and the edge strip pill, in the app's base text size. Edge strips and the pill: accent
/// background, dark text. Window-owned areas (<paramref name="outlined"/>): dark background, accent outline and text, matching their outlined look.
internal static class MarginLabel
{
    public static Control Build(string text, Color accent, bool outlined) => new Border
    {
        IsHitTestVisible = false,
        Background = new SolidColorBrush(outlined ? SurfaceService.PageOf(Avalonia.Styling.ThemeVariant.Dark) : accent),
        BorderBrush = new SolidColorBrush(accent),
        BorderThickness = new Thickness(outlined ? 1 : 0),
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(4, 0),
        Child = new TextBlock
        {
            Text = text,
            FontSize = BaseFontSize(),
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(outlined ? accent : SurfaceService.PageOf(Avalonia.Styling.ThemeVariant.Dark)),
        },
    };

    /// The size in screen pixels a label with <paramref name="text"/> takes at display scaling <paramref name="scale"/>.
    public static PixelSize Measure(string text, Color accent, bool outlined, double scale)
    {
        var label = Build(text, accent, outlined);
        label.Measure(Size.Infinity);
        return new PixelSize((int)Math.Ceiling(label.DesiredSize.Width * scale), (int)Math.Ceiling(label.DesiredSize.Height * scale));
    }

    // The app's one base text size (App.axaml), so the labels are as small as the program's own text.
    private static double BaseFontSize() =>
        Application.Current?.TryGetResource("ControlContentThemeFontSize", null, out var size) == true && size is double d ? d : TextBlock.FontSizeProperty.GetDefaultValue(typeof(TextBlock));
}
