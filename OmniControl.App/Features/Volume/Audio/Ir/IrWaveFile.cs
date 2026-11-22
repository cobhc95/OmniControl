using System.Buffers.Binary;

namespace OmniControl.App.Features.Volume.Audio.Ir;

/// A decoded impulse response: one array per channel (one or two) at <paramref name="SampleRate"/>.
internal sealed record IrAudio(float[][] Channels, int SampleRate)
{
    public int Length => Channels.Length == 0 ? 0 : Channels[0].Length;
}

/// Reads impulse responses from WAV files (RIFF/WAVE: PCM 8/16/24/32-bit, IEEE float 32/64-bit, either also as WAVE_FORMAT_EXTENSIBLE), on a worker, never the audio thread; from TabForge, MIT, see NOTICE.txt. Only the first two channels are kept; longer than <see cref="MaxSeconds"/> is cut.
internal static class IrWaveFile
{
    private const int MaxSeconds = 10;
    private const int MaxFileBytes = 256 * 1024 * 1024;
    private const int ReadBlockBytes = 64 * 1024;

    /// Reads <paramref name="path"/>; throws <see cref="FileNotFoundException"/> or <see cref="InvalidDataException"/> with a readable message.
    /// Only the chunk headers, the format and the kept part of the audio are read: the rest is skipped on disk.
    public static IrAudio Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"The IR file was not found: {path}", path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        if (file.Length > MaxFileBytes) throw new InvalidDataException("The IR file is too large.");
        return Parse(file, (int)file.Length);
    }

    /// Decodes a WAV file of <paramref name="length"/> bytes; a chunk said to run past the end is cut at the end (a truncated file).
    private static IrAudio Parse(Stream file, int length)
    {
        Span<byte> head = stackalloc byte[12];
        if (length < 12) throw new InvalidDataException("The IR file is not a WAV file.");
        file.ReadExactly(head);
        if (!head[..4].SequenceEqual("RIFF"u8) || !head.Slice(8, 4).SequenceEqual("WAVE"u8)) throw new InvalidDataException("The IR file is not a WAV file.");
        int format = 0, channels = 0, rate = 0, bits = 0, dataAt = 0, dataSize = 0;
        Span<byte> fmt = stackalloc byte[26];
        var pos = 12;
        while (pos + 8 <= length)
        {
            file.Position = pos;
            file.ReadExactly(head[..8]);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(head[4..8]), (uint)(length - pos - 8));
            if (head[..4].SequenceEqual("fmt "u8) && size >= 16)
            {
                var body = fmt[..Math.Min(size, fmt.Length)];
                file.ReadExactly(body);
                format = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (format == 0xFFFE && size >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);   // extensible: the sub-format's first two bytes
            }
            else if (head[..4].SequenceEqual("data"u8)) (dataAt, dataSize) = (pos + 8, size);
            pos += 8 + size + (size & 1);
        }
        if (channels is < 1 or > 32 || rate is < 8000 or > 768000) throw new InvalidDataException("The IR file has no valid format.");
        var bytesPer = bits / 8;
        var isFloat = format == 3 && bits is 32 or 64;
        if (!isFloat && (format != 1 || bits is not (8 or 16 or 24 or 32))) throw new InvalidDataException($"This WAV encoding is not supported (format {format}, {bits}-bit). Use PCM or 32-bit float.");
        var frameBytes = bytesPer * channels;
        var frames = Math.Min(dataSize / frameBytes, MaxSeconds * rate);
        if (frames == 0) throw new InvalidDataException("The IR file holds no audio.");
        var kept = Math.Min(channels, 2);
        var result = new float[kept][];
        for (var c = 0; c < kept; c++) result[c] = new float[frames];
        file.Position = dataAt;
        var buffer = new byte[Math.Max(1, ReadBlockBytes / frameBytes) * frameBytes];
        for (var done = 0; done < frames;)
        {
            var count = Math.Min(frames - done, buffer.Length / frameBytes);
            var block = buffer.AsSpan(0, count * frameBytes);
            file.ReadExactly(block);
            for (var f = 0; f < count; f++)
                for (var c = 0; c < kept; c++)
                    result[c][done + f] = Sample(block.Slice(f * frameBytes + c * bytesPer, bytesPer), bits, isFloat);
            done += count;
        }
        return new IrAudio(result, rate);
    }
    private static float Sample(ReadOnlySpan<byte> s, int bits, bool isFloat) => (isFloat, bits) switch
    {
        (true, 32) => BinaryPrimitives.ReadSingleLittleEndian(s),
        (true, _) => (float)BinaryPrimitives.ReadDoubleLittleEndian(s),
        (_, 8) => (s[0] - 128) / 128f,
        (_, 16) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
        (_, 24) => ((s[0] | (s[1] << 8) | (s[2] << 16)) << 8 >> 8) / 8388608f,
        _ => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
    };
}
