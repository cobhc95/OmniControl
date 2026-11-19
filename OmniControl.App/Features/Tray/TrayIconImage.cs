using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OmniControl.App.Features.Icons;

namespace OmniControl.App.Features.Tray;

/// Draws the tray icon and the colour dots of its menu, only when the look changes. The icon holds a frame for every scale Windows may draw
/// the tray at; Avalonia makes the HICON for the scale in use from it and releases it with the icon.
internal static class TrayIconImage
{
    private static readonly Uri LogoAsset = new("avares://OmniControl/Assets/OmniControl.ico");

    /// The tray's small-icon size at 100 % to 400 % scale.
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64 };

    /// The menu's dots are drawn at twice their 16 px so they stay sharp at 200 %.
    private const int DotPixels = 32, DotDpi = 192;

    /// <paramref name="icon"/> null is the logo: as it is when <paramref name="ink"/> is null, else its white parts in that colour on its own tile.
    public static WindowIcon Make(IconImage? icon, Color? ink)
    {
        if (ink is not { } colour) return new WindowIcon(AssetLoader.Open(LogoAsset));
        var frames = icon is null ? Recoloured(colour) : Sizes.Select(size => (size, Draw(icon, colour, size))).ToList();
        return new WindowIcon(new MemoryStream(Ico(frames)));
    }

    /// A colour dot of the menu: a thin <paramref name="ring"/>, dashed for Follow Windows; the chosen one has a thicker accent ring.
    public static Bitmap Dot(IBrush fill, bool chosen, bool dashed, IBrush ring)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(DotPixels, DotPixels), new Vector(DotDpi, DotDpi));
        var size = bitmap.Size.Width;
        var width = chosen ? 2 : 1;
        using (var context = bitmap.CreateDrawingContext())
        {
            var pen = new Pen(chosen ? new SolidColorBrush(AccentService.Current) : ring, width, dashed && !chosen ? new DashStyle(new double[] { 2, 2 }, 0) : null);
            var radius = size / 2 - width;
            context.DrawEllipse(fill, pen, new Point(size / 2, size / 2), radius, radius);
        }
        return bitmap;
    }

    private static byte[] Draw(IconImage icon, Color ink, int size)
    {
        var glyph = new IconGlyph { Source = icon, Foreground = new SolidColorBrush(ink) };
        var box = new Rect(0, 0, size, size);
        glyph.Measure(box.Size);
        glyph.Arrange(box);
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        bitmap.Render(glyph);
        return Png(bitmap);
    }

    /// How far a pixel may be from the logo's edge colour and still count as the edge (rounding of a half-transparent pixel).
    private const double EdgeTolerance = 3;

    /// The logo's colours, read from its largest frame so none is retyped here: the tile (its top band), the tile's thin edge (its top
    /// row) and the dial (its most colourful pixel).
    private readonly record struct LogoColours(Color Tile, Color Edge, Color Dial);

    /// The logo's own frames with every white part (and the pale mixes, such as the slider tails, by how pale they are) moved toward
    /// <paramref name="ink"/>; the tile, its edge and the dial keep their colours.
    private static List<(int Size, byte[] Png)> Recoloured(Color ink)
    {
        using var asset = AssetLoader.Open(LogoAsset);
        using var copy = new MemoryStream();
        asset.CopyTo(copy);
        var frames = Frames(copy.ToArray());
        var largest = frames.MaxBy(f => f.Size);
        using var sample = WriteableBitmap.Decode(new MemoryStream(largest.Png));
        var colours = Colours(sample);
        return frames.Select(f => (f.Size, Tint(f.Png, colours, ink))).ToList();
    }

    private static LogoColours Colours(WriteableBitmap bitmap)
    {
        using var buffer = bitmap.Lock();
        var pixels = Pixels(buffer);
        var bgra = buffer.Format == PixelFormat.Bgra8888;
        var premul = bitmap.AlphaFormat == AlphaFormat.Premul;
        Color At(int at)
        {
            var (r, g, b) = Read(pixels, at, bgra, premul);
            return Color.FromRgb(ToByte(r), ToByte(g), ToByte(b));
        }
        var (width, height) = (buffer.Size.Width, buffer.Size.Height);
        var tile = At(height * 3 / 32 * buffer.RowBytes + width / 2 * 4);
        var edge = At(width / 2 * 4);
        var (dial, spread) = (tile, 0.0);
        for (var at = 0; at < pixels.Length; at += 4)
        {
            if (pixels[at + 3] != 255) continue;
            var (r, g, b) = Read(pixels, at, bgra, premul);
            var here = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            if (here > spread) (dial, spread) = (At(at), here);
        }
        return new LogoColours(tile, edge, dial);
    }

    private static byte[] Tint(byte[] png, LogoColours colours, Color ink)
    {
        using var bitmap = WriteableBitmap.Decode(new MemoryStream(png));
        using (var buffer = bitmap.Lock())
        {
            var pixels = Pixels(buffer);
            var bgra = buffer.Format == PixelFormat.Bgra8888;
            var premul = bitmap.AlphaFormat == AlphaFormat.Premul;
            for (var at = 0; at < pixels.Length; at += 4)
            {
                var alpha = pixels[at + 3];
                if (alpha == 0) continue;
                var pixel = Read(pixels, at, bgra, premul);
                if (Distance(pixel, colours.Edge) <= EdgeTolerance) continue;
                var (from, paleness) = Base(pixel, colours);
                if (paleness <= 0) continue;
                var keep = premul ? alpha / 255.0 : 1;
                byte Mix(byte start, byte to) => (byte)Math.Round((start + (to - start) * paleness) * keep);
                (pixels[at + (bgra ? 2 : 0)], pixels[at + 1], pixels[at + (bgra ? 0 : 2)]) = (Mix(from.R, ink.R), Mix(from.G, ink.G), Mix(from.B, ink.B));
            }
            Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }
        return Png(bitmap);
    }

    /// Which logo colour a pixel is a mix of with white (the tile or the dial, whichever explains it better) and how pale it is.
    private static (Color From, double Paleness) Base((double R, double G, double B) pixel, LogoColours colours)
    {
        var onTile = Paleness(pixel, colours.Tile);
        var onDial = Paleness(pixel, colours.Dial);
        return Distance(pixel, Toward(colours.Dial, onDial)) < Distance(pixel, Toward(colours.Tile, onTile))
            ? (colours.Dial, onDial)
            : (colours.Tile, onTile);
    }

    /// How far toward white a pixel is from <paramref name="base"/>, read on the base's darkest channel (the widest range).
    private static double Paleness((double R, double G, double B) pixel, Color @base)
    {
        var (value, start) = @base.R <= @base.G && @base.R <= @base.B ? (pixel.R, @base.R)
            : @base.G <= @base.B ? (pixel.G, @base.G)
            : (pixel.B, @base.B);
        return start >= 255 ? 0 : Math.Clamp((value - start) / (255.0 - start), 0, 1);
    }

    private static (double R, double G, double B) Toward(Color @base, double paleness) =>
        (@base.R + (255 - @base.R) * paleness, @base.G + (255 - @base.G) * paleness, @base.B + (255 - @base.B) * paleness);

    private static double Distance((double R, double G, double B) a, Color b) => Distance(a, (b.R, b.G, b.B));

    private static double Distance((double R, double G, double B) a, (double R, double G, double B) b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    /// One pixel's red, green and blue without the alpha folded in.
    private static (double R, double G, double B) Read(byte[] pixels, int at, bool bgra, bool premul)
    {
        var scale = premul && pixels[at + 3] > 0 ? 255.0 / pixels[at + 3] : 1;
        return (pixels[at + (bgra ? 2 : 0)] * scale, pixels[at + 1] * scale, pixels[at + (bgra ? 0 : 2)] * scale);
    }

    private static byte[] Pixels(ILockedFramebuffer buffer)
    {
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        return pixels;
    }

    private static byte[] Png(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    /// The PNG frames of an .ico (the logo's are all PNG).
    private static List<(int Size, byte[] Png)> Frames(byte[] ico)
    {
        var frames = new List<(int, byte[])>();
        for (var i = 0; i < BitConverter.ToUInt16(ico, 4); i++)
        {
            var entry = 6 + 16 * i;
            var (length, offset) = (BitConverter.ToInt32(ico, entry + 8), BitConverter.ToInt32(ico, entry + 12));
            frames.Add((ico[entry] == 0 ? 256 : ico[entry], ico[offset..(offset + length)]));
        }
        return frames;
    }

    /// An .ico of PNG frames (Windows Vista's format, which Avalonia reads).
    private static byte[] Ico(IReadOnlyList<(int Size, byte[] Png)> frames)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)frames.Count);
        var offset = 6 + 16 * frames.Count;
        foreach (var (size, png) in frames)
        {
            var side = (byte)(size >= 256 ? 0 : size);
            writer.Write(side);
            writer.Write(side);
            writer.Write((short)0); // no palette, reserved
            writer.Write((short)1); // planes
            writer.Write((short)32); // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in frames) writer.Write(png);
        writer.Flush();
        return stream.ToArray();
    }
}
