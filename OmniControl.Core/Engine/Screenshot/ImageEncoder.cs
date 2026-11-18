using System.Runtime.InteropServices;
using SkiaSharp;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>The file formats a screenshot can be saved in (stored by number in the settings: never reorder).</summary>
public enum ImageFormat { Png, Jpg, WebP, Bmp }

/// <summary>The one table of the formats: their names, file endings and whether a quality applies.</summary>
public static class ImageFormats
{
    public static IReadOnlyList<ImageFormat> All { get; } = new[] { ImageFormat.Png, ImageFormat.Jpg, ImageFormat.WebP, ImageFormat.Bmp };

    /// <summary>What a list shows ("PNG", "WebP").</summary>
    public static string Name(ImageFormat format) => format switch
    {
        ImageFormat.Jpg => "JPG",
        ImageFormat.WebP => "WebP",
        ImageFormat.Bmp => "BMP",
        _ => "PNG",
    };

    /// <summary>The file ending, with its dot (".png").</summary>
    public static string Extension(ImageFormat format) => "." + Name(format).ToLowerInvariant();

    /// <summary>The format a shown name stands for; PNG for anything else.</summary>
    public static ImageFormat Parse(string name) => All.FirstOrDefault(f => string.Equals(Name(f), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for the formats that lose detail by a chosen quality (JPG, WebP).</summary>
    public static bool UsesQuality(ImageFormat format) => format is ImageFormat.Jpg or ImageFormat.WebP;

    public const int MinQuality = 1, MaxQuality = 100, DefaultQuality = 80;

    /// <summary>The format a screenshot is saved and copied in until the person picks another (the owner's choice: JPG at <see cref="DefaultQuality"/>).</summary>
    public const ImageFormat DefaultFormat = ImageFormat.Jpg;
}

/// <summary>Turns a <see cref="ScreenImage"/> into the bytes of an image file. PNG, JPG and WebP through SkiaSharp; BMP is written here (Skia has no BMP encoder).</summary>
public static class ImageEncoder
{
    private const int BmpFileHeader = 14, BmpInfoHeader = 40, BmpBytesPerPixel = 3;

    /// <param name="quality">1-100, used by JPG and WebP only.</param>
    public static byte[] Encode(ScreenImage image, ImageFormat format, int quality)
    {
        if (format == ImageFormat.Bmp) return Bmp(image);
        quality = Math.Clamp(quality, ImageFormats.MinQuality, ImageFormats.MaxQuality);
        var skFormat = format switch
        {
            ImageFormat.Jpg => SKEncodedImageFormat.Jpeg,
            ImageFormat.WebP => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        };
        var pin = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using var pixmap = new SKPixmap(info, pin.AddrOfPinnedObject(), image.Stride);
            using var data = pixmap.Encode(skFormat, ImageFormats.UsesQuality(format) ? quality : ImageFormats.MaxQuality)
                ?? throw new InvalidOperationException($"The picture could not be encoded as {ImageFormats.Name(format)}.");
            return data.ToArray();
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>A 24-bit BMP file (bottom row first, rows padded to four bytes).</summary>
    internal static byte[] Bmp(ScreenImage image)
    {
        var row = (image.Width * BmpBytesPerPixel + 3) & ~3;
        var size = BmpFileHeader + BmpInfoHeader + row * image.Height;
        var bytes = new byte[size];
        using var writer = new BinaryWriter(new MemoryStream(bytes));
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(size);
        writer.Write(0); // reserved
        writer.Write(BmpFileHeader + BmpInfoHeader);
        writer.Write(BmpInfoHeader);
        writer.Write(image.Width);
        writer.Write(image.Height); // positive: bottom row first
        writer.Write((short)1);
        writer.Write((short)(BmpBytesPerPixel * 8));
        writer.Write(0); // no compression
        writer.Write(row * image.Height);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); // resolution and palette: none
        for (var y = image.Height - 1; y >= 0; y--)
        {
            var start = BmpFileHeader + BmpInfoHeader + (image.Height - 1 - y) * row;
            for (var x = 0; x < image.Width; x++)
            {
                var from = y * image.Stride + x * ScreenImage.BytesPerPixel;
                Buffer.BlockCopy(image.Pixels, from, bytes, start + x * BmpBytesPerPixel, BmpBytesPerPixel);
            }
        }
        return bytes;
    }
}
