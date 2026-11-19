namespace OmniControl.Core.Engine.Screenshot;

/// One GIF frame's colours, made the light way: every pixel falls into one of 32,768 boxes (5 bits per colour), the busiest boxes become
/// the palette (each as the average of the colours in it), and every other box takes its nearest palette colour. A frame with no more
/// distinct colours than the palette allows keeps them exactly. The tables are reused frame after frame; not thread-safe.
public sealed class GifPalette
{
    private const int Boxes = 1 << 15;

    private readonly int[] _count = new int[Boxes];
    private readonly long[] _sumR = new long[Boxes], _sumG = new long[Boxes], _sumB = new long[Boxes];
    private readonly short[] _indexOf = new short[Boxes];
    private readonly List<int> _used = new();

    /// The palette of the last frame: red, green, blue for each colour, its length a power of two (the GIF table's size).
    public byte[] Colours { get; private set; } = Array.Empty<byte>();

    /// log2 of the table's size (1 to 8).
    public int Bits { get; private set; }

    /// Makes the palette of a BGRA frame (top row first, <paramref name="stride"/> bytes per row) with at most <paramref name="maxColours"/>
    /// colours and writes each pixel's palette index into <paramref name="indices"/> (width x height).
    public void Build(ReadOnlySpan<byte> bgra, int width, int height, int stride, int maxColours, Span<byte> indices)
    {
        Array.Clear(_count);
        Array.Clear(_sumR);
        Array.Clear(_sumG);
        Array.Clear(_sumB);
        _used.Clear();
        for (var y = 0; y < height; y++)
        {
            var row = bgra.Slice(y * stride, width * 4);
            for (var x = 0; x < row.Length; x += 4)
            {
                var box = BoxOf(row[x + 2], row[x + 1], row[x]);
                if (_count[box]++ == 0) _used.Add(box);
                _sumR[box] += row[x + 2];
                _sumG[box] += row[x + 1];
                _sumB[box] += row[x];
            }
        }

        _used.Sort((a, b) => _count[b].CompareTo(_count[a]));
        var size = Math.Clamp(Math.Min(_used.Count, maxColours), 1, 256);
        Bits = Math.Max(1, (int)Math.Ceiling(Math.Log2(size)));
        var colours = new byte[(1 << Bits) * 3];
        Array.Fill(_indexOf, (short)-1);
        for (var i = 0; i < size; i++)
        {
            var box = _used[i];
            colours[i * 3] = (byte)(_sumR[box] / _count[box]);
            colours[i * 3 + 1] = (byte)(_sumG[box] / _count[box]);
            colours[i * 3 + 2] = (byte)(_sumB[box] / _count[box]);
            _indexOf[box] = (short)i;
        }
        Colours = colours;

        for (var y = 0; y < height; y++)
        {
            var row = bgra.Slice(y * stride, width * 4);
            var target = indices.Slice(y * width, width);
            for (var x = 0; x < width; x++)
            {
                var box = BoxOf(row[x * 4 + 2], row[x * 4 + 1], row[x * 4]);
                var index = _indexOf[box];
                if (index < 0) _indexOf[box] = index = Nearest(row[x * 4 + 2], row[x * 4 + 1], row[x * 4], size);
                target[x] = (byte)index;
            }
        }
    }

    private static int BoxOf(byte r, byte g, byte b) => (r >> 3) << 10 | (g >> 3) << 5 | b >> 3;

    private short Nearest(int r, int g, int b, int size)
    {
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < size; i++)
        {
            int dr = Colours[i * 3] - r, dg = Colours[i * 3 + 1] - g, db = Colours[i * 3 + 2] - b;
            var distance = dr * dr + dg * dg + db * db;
            if (distance >= bestDistance) continue;
            best = i;
            bestDistance = distance;
        }
        return (short)best;
    }
}
