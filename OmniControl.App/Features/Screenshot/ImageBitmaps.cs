using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// Between Core's <see cref="ScreenImage"/> and Avalonia bitmaps: a captured picture to keep, a part of it back as pixels, and a part of
/// it with its drawings back to pixels (the export and the overlay's repainted parts). The bitmaps are immutable: reading a part back
/// never locks or copies the whole picture.
public static class ImageBitmaps
{
    private static readonly Vector ScreenDpi = new(96, 96);

    /// A bitmap holding a copy of the picture's pixels. The caller disposes it.
    public static Bitmap ToBitmap(ScreenImage image)
    {
        var pin = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            return FromPixels(pin.AddrOfPinnedObject(), image.Stride, new PixelSize(image.Width, image.Height));
        }
        finally
        {
            pin.Free();
        }
    }

    /// The screen inside <paramref name="area"/> as a bitmap, copied straight from Windows' copy (no managed picture in between, so a
    /// 4K monitor costs one picture in memory, not two). The caller disposes it.
    public static Bitmap Capture(ScreenRect area) =>
        ScreenCapture.Capture(area, (bits, stride) => FromPixels(bits, stride, new PixelSize(area.Width, area.Height)));

    private static Bitmap FromPixels(IntPtr bits, int stride, PixelSize size) =>
        new(PixelFormat.Bgra8888, AlphaFormat.Opaque, bits, size, ScreenDpi, stride);

    /// The part of the bitmap inside <paramref name="area"/> (held to it) as opaque pixels: for the QR reader and the blur.
    public static ScreenImage Crop(Bitmap source, ScreenRect area)
    {
        var part = area.Intersect(new ScreenRect(0, 0, source.PixelSize.Width, source.PixelSize.Height));
        if (part.IsEmpty) throw new ArgumentException("The area is outside the picture.", nameof(area));
        return CopyOut(source, new PixelRect(part.X, part.Y, part.Width, part.Height));
    }

    /// The picture inside <paramref name="area"/> with the drawings on it, as opaque pixels: the export (the selection), and each part
    /// of the area overlay's bright picture that is painted again, so what the overlay shows is what is exported. Drawings outside the
    /// area are cut off. A drawing still being made (<paramref name="inProgress"/>) is drawn from its settled parts and growing rest,
    /// faded as one. The render target is released before it returns.
    public static ScreenImage Render(Bitmap source, ScreenRect area, IEnumerable<Mark> marks, Mark? inProgress = null)
    {
        var size = new PixelSize(area.Width, area.Height);
        var inside = new Rect(area.X, area.Y, area.Width, area.Height);
        using var target = new RenderTargetBitmap(size, ScreenDpi);
        using (var context = target.CreateDrawingContext())
        {
            context.DrawImage(source, inside, new Rect(0, 0, area.Width, area.Height));
            using (context.PushClip(new Rect(0, 0, area.Width, area.Height)))
            using (context.PushTransform(Matrix.CreateTranslation(-area.X, -area.Y)))
            {
                var drawings = marks.Select(mark => (Mark: mark, Growing: false)).ToList();
                if (inProgress is not null) drawings.Add((inProgress, true));
                DrawLayered(context, drawings, inside);
            }
        }
        return CopyOut(target, new PixelRect(size));
    }

    /// Draws the drawings in order. A run of see-through ones (the marker) is drawn solid into one layer that is faded once (an opacity
    /// mask fades its group as one, where Avalonia's own opacity fades each shape on its own), so overlaps, a stroke crossing itself
    /// included, never get darker and the later colour wins. One still being made (<c>Growing</c>) is drawn from its settled parts and
    /// growing rest. The screen (<see cref="MarkPainter"/>) and the export both come through here.
    private static void DrawLayered(DrawingContext context, IReadOnlyList<(Mark Mark, bool Growing)> drawings, Rect area)
    {
        for (var i = 0; i < drawings.Count;)
        {
            var opacity = drawings[i].Mark.InProgressOpacity;
            if (opacity >= 1)
            {
                DrawSolid(context, drawings[i++]);
                continue;
            }
            var end = i;
            while (end < drawings.Count && drawings[end].Mark.InProgressOpacity == opacity) end++;
            using (context.PushOpacityMask(new ImmutableSolidColorBrush(Colors.Black, opacity), area)) // only the strength counts
                for (; i < end; i++) DrawSolid(context, drawings[i]);
        }
    }

    private static void DrawSolid(DrawingContext context, (Mark Mark, bool Growing) drawing)
    {
        if (!drawing.Growing)
        {
            drawing.Mark.DrawSolid(context);
            return;
        }
        drawing.Mark.DrawSettled(context);
        drawing.Mark.DrawGrowing(context);
    }

    /// A rectangle of a bitmap's pixels, every one opaque (a screenshot has no transparency).
    private static ScreenImage CopyOut(Bitmap bitmap, PixelRect rect)
    {
        var stride = rect.Width * ScreenImage.BytesPerPixel;
        var pixels = new byte[stride * rect.Height];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(rect, pin.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            pin.Free();
        }
        ScreenImage.MakeOpaque(pixels);
        return new ScreenImage(rect.Width, rect.Height, pixels);
    }
}
