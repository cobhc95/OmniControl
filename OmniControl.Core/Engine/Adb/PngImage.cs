using System.Buffers.Binary;
using System.IO.Compression;

namespace OmniControl.Core.Engine;

/// A decoded picture: <see cref="Pixels"/> holds red, green, blue and alpha for each pixel, row after row.
public sealed record RgbaImage(int Width, int Height, byte[] Pixels)
{
    public (byte R, byte G, byte B) At(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2]);
    }
}

/// Reads the PNG that <c>adb exec-out screencap -p</c> prints: 8 bits per channel, grey, grey with alpha, RGB or RGBA, not interlaced (what
/// Android writes). Anything else is not read (null), so a screenshot that cannot be read only means no colour is measured.
public static class Png
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// A picture larger than this (a 4K tablet has 8.3 million pixels) is not read: its pixels alone would take 64 MB and more.
    private const long MaxPixels = 16L * 1024 * 1024;

    public static RgbaImage? Decode(byte[]? data)
    {
        if (data is null || data.Length < 33 || !data.AsSpan(0, 8).SequenceEqual(Signature)) return null;
        int width = 0, height = 0, channels = 0;
        using var packed = new MemoryStream();
        var at = 8;
        while (at + 8 <= data.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at, 4));
            var type = System.Text.Encoding.ASCII.GetString(data, at + 4, 4);
            var body = at + 8;
            if (length < 0 || body + length > data.Length) return null;
            if (type == "IHDR")
            {
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(body, 4));
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(body + 4, 4));
                var depth = data[body + 8];
                channels = data[body + 9] switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
                if (depth != 8 || channels == 0 || data[body + 12] != 0 || width <= 0 || height <= 0) return null;
            }
            else if (type == "IDAT") packed.Write(data, body, length);
            else if (type == "IEND") break;
            at = body + length + 4; // the chunk's CRC is not checked: adb's pipe either delivers the file or cuts it short
        }
        if (channels == 0 || (long)width * height > MaxPixels) return null;
        try
        {
            packed.Position = 0;
            using var inflate = new ZLibStream(packed, CompressionMode.Decompress);
            // Only the rows the header promises are unpacked (a filter byte per row), never what a stream may hold beyond them.
            var raw = new byte[((long)width * channels + 1) * height];
            var filled = 0;
            for (int read; filled < raw.Length && (read = inflate.Read(raw, filled, raw.Length - filled)) > 0;) filled += read;
            return Unfilter(raw.AsSpan(0, filled), width, height, channels);
        }
        catch (InvalidDataException)
        {
            return null; // a cut or broken stream is no picture
        }
    }

    private static RgbaImage? Unfilter(ReadOnlySpan<byte> raw, int width, int height, int channels)
    {
        var stride = width * channels;
        if (raw.Length < (long)(stride + 1) * height) return null;
        var previous = new byte[stride];
        var row = new byte[stride];
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var line = raw.Slice(y * (stride + 1), stride + 1);
            var filter = line[0];
            for (var i = 0; i < stride; i++)
            {
                int left = i >= channels ? row[i - channels] : 0, up = previous[i], upLeft = i >= channels ? previous[i - channels] : 0;
                var value = line[i + 1];
                row[i] = filter switch
                {
                    1 => (byte)(value + left),
                    2 => (byte)(value + up),
                    3 => (byte)(value + (left + up) / 2),
                    4 => (byte)(value + Paeth(left, up, upLeft)),
                    _ => value,
                };
            }
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                var s = x * channels;
                (byte r, byte g, byte b, byte a) = channels switch
                {
                    1 => (row[s], row[s], row[s], (byte)255),
                    2 => (row[s], row[s], row[s], row[s + 1]),
                    3 => (row[s], row[s + 1], row[s + 2], (byte)255),
                    _ => (row[s], row[s + 1], row[s + 2], row[s + 3]),
                };
                (pixels[o], pixels[o + 1], pixels[o + 2], pixels[o + 3]) = (r, g, b, a);
            }
            (previous, row) = (row, previous);
        }
        return new RgbaImage(width, height, pixels);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
