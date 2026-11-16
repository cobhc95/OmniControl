using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using OmniControl.App.Services;

namespace OmniControl.App.Controls;

/// <summary>
/// THE icon glyph: draws an <see cref="IconImage"/> in its own box, in its foreground colour (inherited, so a lit tile's accent reaches it).
/// A stroked icon (the library's) keeps its 24 x 24 grid scaled to the box, so every icon has the same size and line weight; a filled one
/// (a page's icon) is fitted to the box like a PathIcon; a picture is fitted keeping its shape. Its size comes from the context's style.
/// </summary>
public sealed class IconGlyph : Control
{
    private const double Grid = 24;

    public static readonly StyledProperty<IconImage?> SourceProperty = AvaloniaProperty.Register<IconGlyph, IconImage?>(nameof(Source));

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<IconGlyph>();

    static IconGlyph() => AffectsRender<IconGlyph>(SourceProperty, ForegroundProperty);

    public IconImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } source || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var box = new Rect(Bounds.Size);
        if (source.Picture is { } picture)
        {
            context.DrawImage(picture, Fit(new Rect(picture.Size), box));
            return;
        }
        if (source.Shape is not { } shape) return;
        var frame = source.Stroked ? new Rect(0, 0, Grid, Grid) : shape.Bounds;
        if (frame.Width <= 0 || frame.Height <= 0) return;
        var target = Fit(frame, box);
        var scale = target.Width / frame.Width;
        var matrix = Matrix.CreateTranslation(-frame.X, -frame.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(target.X, target.Y);
        using (context.PushTransform(matrix))
        {
            if (source.Stroked)
                context.DrawGeometry(null, new Pen(Foreground, source.StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), shape);
            else context.DrawGeometry(Foreground, null, shape);
        }
    }

    /// <summary>The largest rectangle of <paramref name="content"/>'s shape that fits <paramref name="box"/>, centred.</summary>
    private static Rect Fit(Rect content, Rect box)
    {
        var scale = Math.Min(box.Width / content.Width, box.Height / content.Height);
        var size = new Size(content.Width * scale, content.Height * scale);
        return new Rect(box.X + (box.Width - size.Width) / 2, box.Y + (box.Height - size.Height) / 2, size.Width, size.Height);
    }
}
