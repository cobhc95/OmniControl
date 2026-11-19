namespace OmniControl.Core.Engine.Screenshot;

/// The blur tool's picture: an area made coarse, one pixel per square of the source (each the average colour of its square).
public static class Pixelation
{
    /// The coarse form of <paramref name="area"/> (held to the picture): one pixel per <paramref name="block"/> x <paramref name="block"/> square,
    /// the last row and column of squares cut short at the area's edge. Drawn stretched over the area without smoothing, it hides what was there.
    public static ScreenImage Blocks(ScreenImage source, ScreenRect area, int block)
    {
        area = area.Intersect(source.Bounds);
        if (area.IsEmpty) throw new ArgumentException("The area is outside the picture.", nameof(area));
        block = Math.Max(1, block);
        var columns = (area.Width + block - 1) / block;
        var rows = (area.Height + block - 1) / block;
        var pixels = new byte[columns * rows * ScreenImage.BytesPerPixel];
        for (var by = 0; by < rows; by++)
        for (var bx = 0; bx < columns; bx++)
        {
            long b = 0, g = 0, r = 0;
            var x0 = area.X + bx * block;
            var y0 = area.Y + by * block;
            var x1 = Math.Min(x0 + block, area.Right);
            var y1 = Math.Min(y0 + block, area.Bottom);
            for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var i = y * source.Stride + x * ScreenImage.BytesPerPixel;
                b += source.Pixels[i];
                g += source.Pixels[i + 1];
                r += source.Pixels[i + 2];
            }
            var count = (x1 - x0) * (y1 - y0);
            var o = (by * columns + bx) * ScreenImage.BytesPerPixel;
            pixels[o] = (byte)(b / count);
            pixels[o + 1] = (byte)(g / count);
            pixels[o + 2] = (byte)(r / count);
            pixels[o + 3] = 255;
        }
        return new ScreenImage(columns, rows, pixels);
    }
}
