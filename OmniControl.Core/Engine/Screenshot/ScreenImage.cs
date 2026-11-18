using System.Numerics;
using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>A rectangle in physical screen pixels (virtual-desktop coordinates for a place on screen, image pixels inside a picture).</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>The part both rectangles cover; empty when they do not meet.</summary>
    public ScreenRect Intersect(ScreenRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? default : new ScreenRect(left, top, right - left, bottom - top);
    }

    public ScreenRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>The smallest rectangle holding both.</summary>
    public ScreenRect Union(ScreenRect other)
    {
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        return new ScreenRect(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }
}

/// <summary>
/// A picture held in memory: 32-bit BGRA pixels, top row first, <see cref="Stride"/> = 4 bytes per pixel with no padding. Managed memory only,
/// so nothing needs releasing.
/// </summary>
public sealed class ScreenImage
{
    public const int BytesPerPixel = 4;

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public int Stride => Width * BytesPerPixel;

    public ScreenImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "An image needs at least one pixel.");
        if (pixels.Length != width * height * BytesPerPixel) throw new ArgumentException("The pixel buffer does not match the size.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public ScreenRect Bounds => new(0, 0, Width, Height);

    /// <summary>
    /// Sets every pixel's alpha to full (a screenshot has no transparency), a vector's width of pixels at a time: on a 4K picture this is
    /// one quick pass instead of eight million single-byte writes.
    /// </summary>
    public static void MakeOpaque(Span<byte> bgra)
    {
        const uint alpha = 0xFF000000;
        var pixels = MemoryMarshal.Cast<byte, uint>(bgra);
        var vectors = MemoryMarshal.Cast<uint, Vector<uint>>(pixels);
        var full = new Vector<uint>(alpha);
        for (var i = 0; i < vectors.Length; i++) vectors[i] |= full;
        for (var i = vectors.Length * Vector<uint>.Count; i < pixels.Length; i++) pixels[i] |= alpha;
    }

    /// <summary>
    /// Lays a wash of <paramref name="argb"/> (its alpha is its strength) over BGRA pixels the way drawing that colour over them would, and
    /// makes them opaque: the area overlay's dimmed copy of a picture, made once instead of blended on every frame.
    /// </summary>
    public static void Wash(Span<byte> bgra, uint argb)
    {
        var strength = (int)(argb >> 24);
        Span<byte> blue = stackalloc byte[256], green = stackalloc byte[256], red = stackalloc byte[256];
        for (var v = 0; v < 256; v++)
        {
            blue[v] = Blend(v, (int)(argb & 0xFF), strength);
            green[v] = Blend(v, (int)((argb >> 8) & 0xFF), strength);
            red[v] = Blend(v, (int)((argb >> 16) & 0xFF), strength);
        }
        var pixels = MemoryMarshal.Cast<byte, uint>(bgra);
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            pixels[i] = 0xFF000000u | (uint)red[(int)(p >> 16) & 0xFF] << 16 | (uint)green[(int)(p >> 8) & 0xFF] << 8 | blue[(int)p & 0xFF];
        }
    }

    private static byte Blend(int under, int over, int strength) => (byte)((under * (255 - strength) + over * strength + 127) / 255);

    /// <summary>A copy of the part of the picture inside <paramref name="area"/> (held to the picture).</summary>
    public ScreenImage Crop(ScreenRect area)
    {
        var part = area.Intersect(Bounds);
        if (part.IsEmpty) throw new ArgumentException("The area is outside the picture.", nameof(area));
        var pixels = new byte[part.Width * part.Height * BytesPerPixel];
        var row = part.Width * BytesPerPixel;
        for (var y = 0; y < part.Height; y++)
            Buffer.BlockCopy(Pixels, (part.Y + y) * Stride + part.X * BytesPerPixel, pixels, y * row, row);
        return new ScreenImage(part.Width, part.Height, pixels);
    }
}
