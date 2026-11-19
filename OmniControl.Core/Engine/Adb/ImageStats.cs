namespace OmniControl.Core.Engine;

/// A rectangle of a picture in pixels (right and bottom not included).
public readonly record struct PixelBox(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    /// The box cut to the picture; empty (zero width) when it lies outside.
    public PixelBox Within(RgbaImage image) =>
        new(Math.Clamp(Left, 0, image.Width), Math.Clamp(Top, 0, image.Height), Math.Clamp(Right, 0, image.Width), Math.Clamp(Bottom, 0, image.Height));
}

/// The colour measures a state reading compares. Colours are points on the hue-saturation disc (<c>[S·cos H, S·sin H]</c>), never raw RGB,
/// so a lighter or darker theme moves them little; the shape is a 64-bit difference hash of the brightness. Each region is sampled on a grid
/// of at most <see cref="CoreTiming.StateSampleBudget"/> points, so a big card costs as little as a small button.
public static class ImageStats
{
    private const int HueBins = 12, GreyBins = 3;

    /// Hue in degrees (0-360), saturation and value (0-1).
    public static (double H, double S, double V) Hsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var delta = max - min;
        var h = delta == 0 ? 0 : max == rf ? 60 * (((gf - bf) / delta) % 6) : max == gf ? 60 * ((bf - rf) / delta + 2) : 60 * ((rf - gf) / delta + 4);
        return (h < 0 ? h + 360 : h, max == 0 ? 0 : delta / max, max);
    }

    private static double[] Point(double h, double s) => new[] { s * Math.Cos(h * Math.PI / 180), s * Math.Sin(h * Math.PI / 180) };

    private static IEnumerable<(byte R, byte G, byte B)> Samples(RgbaImage image, PixelBox box)
    {
        box = box.Within(image);
        if (box.Width <= 0 || box.Height <= 0) yield break;
        var step = Math.Max(1, (int)Math.Sqrt((double)box.Width * box.Height / CoreTiming.StateSampleBudget));
        for (var y = box.Top; y < box.Bottom; y += step)
            for (var x = box.Left; x < box.Right; x += step)
                yield return image.At(x, y);
    }

    /// The region's average colour as a hue-saturation point; null when the region is empty.
    public static double[]? MeanColour(RgbaImage image, PixelBox box)
    {
        long r = 0, g = 0, b = 0, n = 0;
        foreach (var p in Samples(image, box)) { r += p.R; g += p.G; b += p.B; n++; }
        if (n == 0) return null;
        var (h, s, _) = Hsv((byte)(r / n), (byte)(g / n), (byte)(b / n));
        return Point(h, s);
    }

    /// The region's dominant colour as a hue-saturation point: the most common hue family among its clearly coloured pixels when they are at
    /// least <see cref="CoreTiming.StateDominantMinShare"/> of it, else the grey (the point at the centre); null when the region is empty.
    public static double[]? DominantColour(RgbaImage image, PixelBox box)
    {
        var counts = new int[HueBins + GreyBins];
        var sums = new double[HueBins, 2];
        var total = 0;
        foreach (var p in Samples(image, box))
        {
            total++;
            var (h, s, v) = Hsv(p.R, p.G, p.B);
            if (!IsColoured(s, v)) { counts[HueBins + Math.Min(GreyBins - 1, (int)(v * GreyBins))]++; continue; }
            var bin = Math.Min(HueBins - 1, (int)(h / (360.0 / HueBins)));
            counts[bin]++;
            var point = Point(h, s);
            sums[bin, 0] += point[0];
            sums[bin, 1] += point[1];
        }
        if (total == 0) return null;
        var best = Enumerable.Range(0, HueBins).OrderByDescending(i => counts[i]).First();
        return counts[best] >= total * CoreTiming.StateDominantMinShare ? new[] { sums[best, 0] / counts[best], sums[best, 1] / counts[best] } : new[] { 0.0, 0.0 };
    }

    /// The share of the region that is clearly coloured (not grey, white or black), as a one-value measure; null when empty.
    public static double[]? Colourful(RgbaImage image, PixelBox box)
    {
        int coloured = 0, total = 0;
        foreach (var p in Samples(image, box))
        {
            total++;
            var (_, s, v) = Hsv(p.R, p.G, p.B);
            if (IsColoured(s, v)) coloured++;
        }
        return total == 0 ? null : new[] { (double)coloured / total };
    }

    private static bool IsColoured(double s, double v) => s >= CoreTiming.StateGreySaturation && v >= CoreTiming.StateDarkValue;

    /// A 64-bit difference hash of the region (9 by 8 cells of average brightness, each compared with its right neighbour), as 64 values of 0
    /// or 1; null when the region is too small to cut into cells.
    public static double[]? ShapeHash(RgbaImage image, PixelBox box)
    {
        box = box.Within(image);
        const int columns = 9, rows = 8;
        if (box.Width < columns || box.Height < rows) return null;
        var cells = new double[rows, columns];
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var cell = new PixelBox(box.Left + box.Width * column / columns, box.Top + box.Height * row / rows,
                    box.Left + box.Width * (column + 1) / columns, box.Top + box.Height * (row + 1) / rows);
                double sum = 0;
                var n = 0;
                foreach (var p in Samples(image, cell)) { sum += 0.299 * p.R + 0.587 * p.G + 0.114 * p.B; n++; }
                cells[row, column] = n == 0 ? 0 : sum / n;
            }
        var bits = new double[rows * (columns - 1)];
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns - 1; column++)
                bits[row * (columns - 1) + column] = cells[row, column] > cells[row, column + 1] ? 1 : 0;
        return bits;
    }
}
