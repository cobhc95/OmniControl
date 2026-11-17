using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Screenshot;

/// <summary>
/// Between Core's <see cref="ScreenImage"/> and Avalonia bitmaps: a captured picture to show, a part of it back as pixels, and the finished
/// selection with its drawings back to pixels. The bitmaps are immutable, so the renderer uploads a picture to the graphics card once and
/// reuses it on every frame, and reading a part back never makes it upload the picture again.
/// </summary>
public static class ImageBitmaps
{
    private static readonly Vector ScreenDpi = new(96, 96);

    /// <summary>A bitmap holding a copy of the picture's pixels. The caller disposes it.</summary>
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

    /// <summary>
    /// The screen inside <paramref name="area"/> as a bitmap, copied straight from Windows' copy (no managed picture in between, so a
    /// 4K monitor costs one picture in memory, not two). The caller disposes it.
    /// </summary>
    public static Bitmap Capture(ScreenRect area) =>
        ScreenCapture.Capture(area, (bits, stride) => FromPixels(bits, stride, new PixelSize(area.Width, area.Height)));

    private static Bitmap FromPixels(IntPtr bits, int stride, PixelSize size) =>
        new(PixelFormat.Bgra8888, AlphaFormat.Opaque, bits, size, ScreenDpi, stride);

    /// <summary>The part of the bitmap inside <paramref name="area"/> (held to it) as opaque pixels: for the QR reader and the blur.</summary>
    public static ScreenImage Crop(Bitmap source, ScreenRect area)
    {
        var part = area.Intersect(new ScreenRect(0, 0, source.PixelSize.Width, source.PixelSize.Height));
        if (part.IsEmpty) throw new ArgumentException("The area is outside the picture.", nameof(area));
        return CopyOut(source, new PixelRect(part.X, part.Y, part.Width, part.Height));
    }

    /// <summary>
    /// The picture inside <paramref name="selection"/> with the drawings on it, as pixels: the drawings are clipped to the selection, so
    /// nothing drawn outside it is exported. The render target is released before it returns.
    /// </summary>
    public static ScreenImage Export(Bitmap source, ScreenRect selection, IEnumerable<Mark> marks)
    {
        var size = new PixelSize(selection.Width, selection.Height);
        using var target = new RenderTargetBitmap(size, ScreenDpi);
        using (var context = target.CreateDrawingContext())
        {
            var area = new Rect(selection.X, selection.Y, selection.Width, selection.Height);
            context.DrawImage(source, area, new Rect(0, 0, selection.Width, selection.Height));
            using (context.PushClip(new Rect(0, 0, selection.Width, selection.Height)))
            using (context.PushTransform(Matrix.CreateTranslation(-selection.X, -selection.Y)))
                foreach (var mark in marks) mark.Draw(context);
        }
        return CopyOut(target, new PixelRect(size));
    }

    /// <summary>A rectangle of a bitmap's pixels, every one opaque (a screenshot has no transparency).</summary>
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
