using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;

namespace OmniControl.App.Features.OtherPcs;

/// Reading and writing whole messages on a stream: a 4-byte length (big endian) and the bytes. A length over the caller's limit, a
/// stream that ends, or a step that takes longer than <see cref="Timing.PcHandshakeStep"/> ends the exchange; nothing here trusts what it reads.
internal static class PcWire
{
    /// What a failed read, write or wait on a socket throws: the caller treats every one as "the other PC is gone".
    public static bool IsNetworkError(Exception ex) => ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException;

    /// A token that cancels after one handshake step's time, or when <paramref name="outer"/> does.
    public static CancellationTokenSource Step(CancellationToken outer, TimeSpan? limit = null)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(outer);
        linked.CancelAfter(limit ?? Timing.PcHandshakeStep);
        return linked;
    }

    /// Exactly <paramref name="count"/> bytes, or null when the stream ended first.
    public static async Task<byte[]?> ReadExactAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var got = 0;
        while (got < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(got, count - got), ct).ConfigureAwait(false);
            if (n == 0) return null;
            got += n;
        }
        return buffer;
    }

    /// One message, or null when the stream ended or the length is over <paramref name="max"/>.
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, int max, CancellationToken ct)
    {
        if (await ReadExactAsync(stream, 4, ct).ConfigureAwait(false) is not { } head) return null;
        var length = BinaryPrimitives.ReadInt32BigEndian(head);
        if (length < 0 || length > max) return null;
        return length == 0 ? Array.Empty<byte>() : await ReadExactAsync(stream, length, ct).ConfigureAwait(false);
    }

    public static async Task WriteFrameAsync(Stream stream, byte[] data, CancellationToken ct)
    {
        var frame = new byte[4 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, data.Length);
        data.CopyTo(frame, 4);
        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static Task WriteJsonAsync<T>(Stream stream, T value, CancellationToken ct) =>
        WriteFrameAsync(stream, JsonSerializer.SerializeToUtf8Bytes(value, PcProtocol.Json), ct);

    /// One small json message of an unproven PC, or null when it is not one.
    public static async Task<T?> ReadJsonAsync<T>(Stream stream, CancellationToken ct) where T : class
    {
        if (await ReadFrameAsync(stream, PcProtocol.MaxOpenFrame, ct).ConfigureAwait(false) is not { } bytes) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, PcProtocol.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
