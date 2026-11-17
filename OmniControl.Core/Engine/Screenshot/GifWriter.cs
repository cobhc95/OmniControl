using System.Text;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>
/// Writes an animated GIF straight to a file, frame by frame, so a long recording never sits in memory: each frame gets its own palette
/// (<see cref="GifPalette"/>) and its delay from the real time of the frame after it (<see cref="VideoMath.GifDelay"/>), so one frame is
/// always held back. A frame the same as the one before only makes that one last longer. The animation loops for ever.
/// </summary>
internal sealed class GifWriter : IVideoSink
{
    private readonly FileStream _file;
    private readonly int _width, _height, _colours;
    private readonly GifPalette _palette = new();
    private readonly byte[] _indices;
    private byte[]? _held;
    private long _heldAt;

    public GifWriter(string path, int width, int height, VideoQuality quality)
    {
        _width = width;
        _height = height;
        _colours = VideoMath.GifColourCount(quality);
        _indices = new byte[width * height];
        _file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        WriteHeader();
    }

    public long Length => _file.Length;

    public void WriteFrame(ReadOnlySpan<byte> bgra, int stride, long at)
    {
        var row = _width * 4;
        if (_held is { } held && SameAs(held, bgra, stride)) return; // the held frame just lasts longer
        if (_held is not null) Emit(_held, _heldAt, at);
        _held ??= new byte[row * _height];
        for (var y = 0; y < _height; y++) bgra.Slice(y * stride, row).CopyTo(_held.AsSpan(y * row, row));
        _heldAt = at;
    }

    public void Finish(long end)
    {
        if (_held is not null) Emit(_held, _heldAt, end);
        _file.WriteByte(0x3B); // trailer
        _file.Flush();
    }

    public void Dispose() => _file.Dispose();

    private bool SameAs(byte[] held, ReadOnlySpan<byte> bgra, int stride)
    {
        var row = _width * 4;
        for (var y = 0; y < _height; y++)
            if (!bgra.Slice(y * stride, row).SequenceEqual(held.AsSpan(y * row, row))) return false;
        return true;
    }

    private void WriteHeader()
    {
        _file.Write(Encoding.ASCII.GetBytes("GIF89a"));
        WriteShort(_width);
        WriteShort(_height);
        _file.WriteByte(0); // no global colour table: every frame has its own
        _file.WriteByte(0);
        _file.WriteByte(0);
        // Netscape application extension: loop for ever
        _file.Write(new byte[] { 0x21, 0xFF, 0x0B });
        _file.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        _file.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
    }

    private void Emit(byte[] frame, long at, long next)
    {
        _palette.Build(frame, _width, _height, _width * 4, _colours, _indices);
        var delay = VideoMath.GifDelay(at, next);
        _file.Write(new byte[] { 0x21, 0xF9, 0x04, 0x00 }); // graphic control: no transparency, no disposal
        WriteShort(delay);
        _file.Write(new byte[] { 0x00, 0x00 });
        _file.WriteByte(0x2C); // image descriptor
        WriteShort(0);
        WriteShort(0);
        WriteShort(_width);
        WriteShort(_height);
        _file.WriteByte((byte)(0x80 | (_palette.Bits - 1))); // a local colour table of 2^Bits colours
        _file.Write(_palette.Colours);
        GifLzw.Write(_indices, Math.Max(2, _palette.Bits), _file);
    }

    private void WriteShort(int value)
    {
        _file.WriteByte((byte)value);
        _file.WriteByte((byte)(value >> 8));
    }
}

/// <summary>
/// GIF's LZW compression of a frame's palette indices, written as the format's sub-blocks of at most 255 bytes. The code table is an
/// open-addressed hash of two small arrays (the classic GIF encoders' table: a prime size a little over 4096, double hashing), several
/// times faster than a dictionary on every pixel of a large frame and cleared in a moment when the table fills.
/// </summary>
internal static class GifLzw
{
    private const int MaxCodes = 4096, MaxCodeSize = 12;
    private const int TableSize = 5003, HashShift = 4; // HashShift keeps (index << shift) ^ prefix inside the table

    public static void Write(ReadOnlySpan<byte> indices, int minCodeSize, Stream output)
    {
        output.WriteByte((byte)minCodeSize);
        var bits = new BitPacker(output);
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var codes = new CodeTable();
        var codeSize = minCodeSize + 1;
        var next = end + 1;
        bits.Write(clear, codeSize);
        if (indices.IsEmpty)
        {
            bits.Write(end, codeSize);
            bits.Finish();
            return;
        }
        int prefix = indices[0];
        for (var i = 1; i < indices.Length; i++)
        {
            int k = indices[i];
            var slot = codes.Find(prefix, k);
            if (codes.Code(slot) is var code and >= 0)
            {
                prefix = code;
                continue;
            }
            bits.Write(prefix, codeSize);
            if (next < MaxCodes)
            {
                codes.Add(slot, prefix, k, next++);
                if (next > 1 << codeSize && codeSize < MaxCodeSize) codeSize++; // the reader widens its codes one entry later
            }
            else
            {
                bits.Write(clear, codeSize);
                codes.Clear();
                codeSize = minCodeSize + 1;
                next = end + 1;
            }
            prefix = k;
        }
        bits.Write(prefix, codeSize);
        bits.Write(end, codeSize);
        bits.Finish();
    }

    /// <summary>The string table: "prefix code followed by index k" to its code, keyed by both packed in one number.</summary>
    private sealed class CodeTable
    {
        private readonly int[] _keys = new int[TableSize];
        private readonly short[] _codes = new short[TableSize];

        public CodeTable() => Clear();

        public void Clear() => Array.Fill(_keys, -1);

        /// <summary>The slot that holds (prefix, k), or the empty slot where it would go.</summary>
        public int Find(int prefix, int k)
        {
            var key = k << MaxCodeSize | prefix;
            var slot = k << HashShift ^ prefix;
            if (_keys[slot] == key || _keys[slot] < 0) return slot;
            var step = slot == 0 ? 1 : TableSize - slot;
            while (true)
            {
                slot -= step;
                if (slot < 0) slot += TableSize;
                if (_keys[slot] == key || _keys[slot] < 0) return slot;
            }
        }

        /// <summary>The code in a slot <see cref="Find"/> gave, or -1 when the slot is empty.</summary>
        public int Code(int slot) => _keys[slot] < 0 ? -1 : _codes[slot];

        public void Add(int slot, int prefix, int k, int code)
        {
            _keys[slot] = k << MaxCodeSize | prefix;
            _codes[slot] = (short)code;
        }
    }

    /// <summary>Packs codes least significant bit first into sub-blocks.</summary>
    private sealed class BitPacker
    {
        private readonly Stream _output;
        private readonly byte[] _block = new byte[255];
        private int _used;
        private int _pending;
        private int _pendingBits;

        public BitPacker(Stream output) => _output = output;

        public void Write(int code, int size)
        {
            _pending |= code << _pendingBits;
            _pendingBits += size;
            while (_pendingBits >= 8)
            {
                Add((byte)_pending);
                _pending >>= 8;
                _pendingBits -= 8;
            }
        }

        public void Finish()
        {
            if (_pendingBits > 0) Add((byte)_pending);
            Flush();
            _output.WriteByte(0); // block terminator
        }

        private void Add(byte value)
        {
            _block[_used++] = value;
            if (_used == _block.Length) Flush();
        }

        private void Flush()
        {
            if (_used == 0) return;
            _output.WriteByte((byte)_used);
            _output.Write(_block, 0, _used);
            _used = 0;
        }
    }
}
