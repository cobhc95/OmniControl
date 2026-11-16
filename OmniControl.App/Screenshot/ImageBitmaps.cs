using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Screenshot;

/// <summary>Between Core's <see cref="ScreenImage"/> and Avalonia bitmaps: a captured picture to show, and the finished selection with its drawings back to pixels.</summary>
public static class ImageBitmaps
{
    private static readonly Vector ScreenDpi = new(96, 96);

    /// <summary>A bitmap holding a copy of the picture's pixels. The caller disposes it.</summary>
    public static WriteableBitmap ToBitmap(ScreenImage image)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), ScreenDpi, PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var buffer = bitmap.Lock();
        for (var y = 0; y < image.Height; y++)
            Marshal.Copy(image.Pixels, y * image.Stride, buffer.Address + y * buffer.RowBytes, image.Stride);
        return bitmap;
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
        var pixels = new byte[selection.Width * selection.Height * ScreenImage.BytesPerPixel];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new PixelRect(size), pin.AddrOfPinnedObject(), pixels.Length, selection.Width * ScreenImage.BytesPerPixel);
        }
        finally
        {
            pin.Free();
        }
        for (var i = 3; i < pixels.Length; i += ScreenImage.BytesPerPixel) pixels[i] = 255; // a screenshot is opaque
        return new ScreenImage(selection.Width, selection.Height, pixels);
    }
}
