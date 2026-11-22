using System.Buffers.Binary;
using OmniControl.App.Features.Volume.Audio.Ir;

namespace OmniControl.Tests;

/// Impulse response WAV files: every encoding, channel count, length and damage decodes to exactly the samples written.
public class IrWaveFileTests
{
    [Theory]
    [InlineData(1, 8, 1)]
    [InlineData(1, 16, 1)]
    [InlineData(2, 16, 1)]
    [InlineData(1, 24, 1)]
    [InlineData(2, 24, 1)]
    [InlineData(2, 32, 1)]
    [InlineData(1, 32, 3)]
    [InlineData(2, 32, 3)]
    [InlineData(2, 64, 3)]
    [InlineData(6, 24, 1)]
    [InlineData(3, 32, 3)]
    public void DecodesEveryEncoding(int channels, int bits, int format)
    {
        AssertDecodes(Wav(channels, bits, format, 48000, 1000), channels, bits, format, 1000);
        AssertDecodes(Wav(channels, bits, format, 48000, 1000, extensible: true), channels, bits, format, 1000);
    }

    [Fact]
    public void ALongFileIsCutAtTenSeconds() =>
        AssertDecodes(Wav(2, 16, 1, 8000, 8000 * 12), 2, 16, 1, 8000 * 10);

    [Fact]
    public void ATruncatedFileKeepsItsWholeFrames()
    {
        var bytes = Wav(2, 24, 1, 44100, 500);
        AssertDecodes(bytes[..^4], 2, 24, 1, 499);
    }

    [Fact]
    public void OtherChunksAndAFormatAfterTheDataAreRead()
    {
        var data = Data(2, 16, 1, 300);
        var bytes = Riff(Chunk("LIST", new byte[5]), Chunk("data", data), Chunk("fmt ", Fmt(2, 16, 1, 48000, false)));
        AssertDecodes(bytes, 2, 16, 1, 300);
    }

    [Fact]
    public void BadFilesSayWhy()
    {
        Assert.Throws<InvalidDataException>(() => ReadBytes("RIFF"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => ReadBytes(Riff(Chunk("fmt ", Fmt(2, 16, 1, 48000, false)))));
        Assert.Throws<InvalidDataException>(() => ReadBytes(Wav(1, 12, 1, 48000, 10)));
    }

    private static void AssertDecodes(byte[] bytes, int channels, int bits, int format, int frames)
    {
        var audio = ReadBytes(bytes);
        Assert.Equal(Math.Min(channels, 2), audio.Channels.Length);
        Assert.Equal(frames, audio.Length);
        for (var c = 0; c < audio.Channels.Length; c++)
            for (var f = 0; f < frames; f++)
                Assert.Equal(Expected(f, c, bits, format), audio.Channels[c][f]);
    }

    private static IrAudio ReadBytes(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-wav-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, bytes);
        try
        {
            return IrWaveFile.Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// A whole WAV file of the test pattern.
    internal static byte[] Wav(int channels, int bits, int format, int rate, int frames, bool extensible = false) =>
        Riff(Chunk("fmt ", Fmt(channels, bits, format, rate, extensible)), Chunk("data", Data(channels, bits, format, frames)));

    private static long Raw(int f, int c, int bits) => bits switch
    {
        8 => (f * 7 + c * 31) % 256,
        _ => ((f * 7919L + c * 104729L) % (1L << bits)) - (1L << (bits - 1)),
    };

    private static double Real(int f, int c) => ((f * 13 + c * 5) % 400) / 200.0 - 1 + 1e-9 * f;

    private static float Expected(int f, int c, int bits, int format) => (format, bits) switch
    {
        (3, _) => (float)Real(f, c),
        (_, 8) => (Raw(f, c, 8) - 128) / 128f,
        (_, 16) => Raw(f, c, 16) / 32768f,
        (_, 24) => Raw(f, c, 24) / 8388608f,
        _ => Raw(f, c, 32) / 2147483648f,
    };

    private static byte[] Data(int channels, int bits, int format, int frames)
    {
        var bytesPer = bits / 8;
        var data = new byte[frames * channels * bytesPer];
        for (var f = 0; f < frames; f++)
            for (var c = 0; c < channels; c++)
            {
                var at = data.AsSpan((f * channels + c) * bytesPer, bytesPer);
                if (format == 3 && bits == 32) BinaryPrimitives.WriteSingleLittleEndian(at, (float)Real(f, c));
                else if (format == 3) BinaryPrimitives.WriteDoubleLittleEndian(at, Real(f, c));
                else
                {
                    var raw = Raw(f, c, bits);
                    for (var b = 0; b < bytesPer; b++) at[b] = (byte)(raw >> (8 * b));
                }
            }
        return data;
    }

    private static byte[] Fmt(int channels, int bits, int format, int rate, bool extensible)
    {
        var fmt = new byte[extensible ? 40 : 16];
        BinaryPrimitives.WriteUInt16LittleEndian(fmt, (ushort)(extensible ? 0xFFFE : format));
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(2), (ushort)channels);
        BinaryPrimitives.WriteInt32LittleEndian(fmt.AsSpan(4), rate);
        BinaryPrimitives.WriteInt32LittleEndian(fmt.AsSpan(8), rate * channels * bits / 8);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(12), (ushort)(channels * bits / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(14), (ushort)bits);
        if (extensible)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(16), 22);
            BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(24), (ushort)format);
        }
        return fmt;
    }

    private static byte[] Chunk(string id, byte[] body)
    {
        var chunk = new byte[8 + body.Length + (body.Length & 1)];
        System.Text.Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)body.Length);
        body.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] Riff(params byte[][] chunks)
    {
        var body = chunks.SelectMany(c => c).ToArray();
        var riff = new byte[12 + body.Length];
        "RIFF"u8.CopyTo(riff);
        BinaryPrimitives.WriteUInt32LittleEndian(riff.AsSpan(4), (uint)(4 + body.Length));
        "WAVE"u8.CopyTo(riff.AsSpan(8));
        body.CopyTo(riff, 12);
        return riff;
    }
}
