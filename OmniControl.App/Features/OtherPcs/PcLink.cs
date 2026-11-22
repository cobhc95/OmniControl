namespace OmniControl.App.Features.OtherPcs;

/// One connection to a paired PC after both proved they hold its pairing key (<see cref="PcSession"/>): every message is sealed with the
/// direction's own AES-GCM key, so what is read here came from the other PC and was neither changed, copied nor reordered. It reads on its
/// own task and hands each message to the owner's <c>deliver</c> (on that task; a session's goes through its <see cref="PcInbox"/>); one
/// <see cref="Closed"/> ends it.
internal sealed class PcLink : IDisposable
{
    private readonly Stream _stream;
    private readonly PcCipher _send;
    private readonly PcCipher _receive;
    private readonly PcOutbox _outbox;
    private readonly CancellationTokenSource _stop = new();
    private int _closed;

    public PcLink(string peerId, Stream stream, byte[] sendKey, byte[] receiveKey, bool dialled)
    {
        PeerId = peerId;
        Dialled = dialled;
        _stream = stream;
        _send = new PcCipher(sendKey);
        _receive = new PcCipher(receiveKey);
        _outbox = new PcOutbox(Timing.PcOutboxMax, Timing.PcOutboxBytes, WriteAsync, Close);
    }

    public string PeerId { get; }

    /// True when this PC dialled the other (false: the other PC dialled this one).
    public bool Dialled { get; }

    /// True when the other end's address is a Tailscale one: the link really runs over Tailscale.
    public bool ViaTailscale { get; set; }

    /// When the last message (a keep-alive too) arrived, in UTC.
    public DateTime LastReceived { get; private set; } = DateTime.UtcNow;

    /// The link ended (the other PC left, a message did not open, or <see cref="Close"/>); raised once.
    public event Action? Closed;

    /// Reads until the link ends; <paramref name="deliver"/> takes each message and its size in bytes (the next is read once it returned, so a
    /// waiting deliver slows the other PC).
    public async Task RunAsync(Func<PcMsg, int, CancellationToken, Task> deliver)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (await PcWire.ReadFrameAsync(_stream, PcProtocol.MaxFrame, _stop.Token).ConfigureAwait(false) is not { } sealedBytes) break;
                if (_receive.Open(sealedBytes) is not { } plain || PcMsg.From(plain) is not { } message) break; // a message that does not open ends the link
                LastReceived = DateTime.UtcNow;
                await deliver(message, sealedBytes.Length, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (PcWire.IsNetworkError(ex))
        {
            // The other PC went away or the link was closed: the normal end of a link.
        }
        finally
        {
            Close();
        }
    }

    /// Queues a message in the link's bounded outbox; true once it was written, false when the link is closed or stalled (which closes it).
    public Task<bool> SendAsync(PcMsg message) => _outbox.Put(message);

    // The one writer: sealing here keeps the cipher's sequence in write order.
    private async Task WriteAsync(byte[] plain)
    {
        using var deadline = PcWire.Step(_stop.Token, Timing.PcWriteDeadline);
        await PcWire.WriteFrameAsync(_stream, _send.Seal(plain), deadline.Token).ConfigureAwait(false);
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _outbox.Close();
        _stop.Cancel();
        _stream.Dispose();
        Closed?.Invoke();
    }

    public void Dispose()
    {
        Close();
        _send.Dispose();
        _receive.Dispose();
        _stop.Dispose();
    }
}

/// How a connection to an already paired PC proves itself (<see cref="PcProtocol.WantsSession"/>, <see cref="PcProtocol.WantsProbe"/>): the
/// dialling PC says who it is and sends a fresh nonce and a fresh ECDH key; the listening PC looks the id up (an unknown id gets no answer at
/// all), sends its own nonce, its own fresh key and a MAC over everything under the pairing key; the dialling PC checks it and answers with
/// its own MAC. Only then do both derive one key per direction from the two fresh keys' shared secret and the pairing key: a copied handshake
/// is useless (the nonces are new each time), and a pairing key that leaks later does not open what was recorded (the fresh keys are gone).
internal static class PcSession
{
    private const int HelloBytes = PcProtocol.IdBytes + PcProtocol.KeyBytes + PcCrypto.PointBytes, AnswerBytes = PcProtocol.KeyBytes + PcCrypto.PointBytes + PcProtocol.KeyBytes;

    public static async Task<PcLink?> DialAsync(Stream stream, string ownId, string peerId, byte[] secret, CancellationToken ct, byte kind = PcProtocol.WantsSession)
    {
        var ownNonce = PcCrypto.Random(PcProtocol.KeyBytes);
        var made = PcCrypto.NewKey();
        using var key = made.Key;
        await stream.WriteAsync(Join(new[] { kind }, PcIds.Bytes(ownId), ownNonce, made.Point), ct).ConfigureAwait(false);

        using var step = PcWire.Step(ct);
        if (await PcWire.ReadExactAsync(stream, AnswerBytes, step.Token).ConfigureAwait(false) is not { } answer) return null;
        var peerNonce = answer[..PcProtocol.KeyBytes];
        var peerPoint = answer[PcProtocol.KeyBytes..^PcProtocol.KeyBytes];
        var said = Said(kind, ownNonce, peerNonce, made.Point, peerPoint, ownId, peerId);
        if (!PcCrypto.Same(answer[^PcProtocol.KeyBytes..], PcCrypto.Mac(secret, PcCrypto.Text("listener"), said))) return null;
        if (PcCrypto.Agree(key, peerPoint) is not { } shared) return null;
        await stream.WriteAsync(PcCrypto.Mac(secret, PcCrypto.Text("dialler"), said), ct).ConfigureAwait(false);
        return Link(peerId, stream, secret, shared, said, dialling: true);
    }

    /// Answers a session or probe connection whose first byte (<paramref name="kind"/>) was read; null (and nothing sent) when the id is not a
    /// paired PC's or a proof fails.
    public static async Task<PcLink?> AcceptAsync(Stream stream, byte kind, string ownId, Func<string, byte[]?> secretOf, CancellationToken ct)
    {
        using var step = PcWire.Step(ct);
        if (await PcWire.ReadExactAsync(stream, HelloBytes, step.Token).ConfigureAwait(false) is not { } hello) return null;
        var peerId = PcIds.FromBytes(hello[..PcProtocol.IdBytes]);
        if (secretOf(peerId) is not { } secret) return null;
        var peerNonce = hello[PcProtocol.IdBytes..(PcProtocol.IdBytes + PcProtocol.KeyBytes)];
        var peerPoint = hello[(PcProtocol.IdBytes + PcProtocol.KeyBytes)..];
        var ownNonce = PcCrypto.Random(PcProtocol.KeyBytes);
        var made = PcCrypto.NewKey();
        using var key = made.Key;
        var said = Said(kind, peerNonce, ownNonce, peerPoint, made.Point, peerId, ownId);
        await stream.WriteAsync(Join(ownNonce, made.Point, PcCrypto.Mac(secret, PcCrypto.Text("listener"), said)), ct).ConfigureAwait(false);
        if (await PcWire.ReadExactAsync(stream, PcProtocol.KeyBytes, step.Token).ConfigureAwait(false) is not { } proof
            || !PcCrypto.Same(proof, PcCrypto.Mac(secret, PcCrypto.Text("dialler"), said))) return null;
        if (PcCrypto.Agree(key, peerPoint) is not { } shared) return null;
        return Link(peerId, stream, secret, shared, said, dialling: false);
    }

    // Everything said, dialler first; the connection's kind (session or probe) is part of it, so one can never be passed off as the other.
    internal static byte[] Said(byte kind, byte[] diallerNonce, byte[] listenerNonce, byte[] diallerPoint, byte[] listenerPoint, string diallerId, string listenerId) =>
        PcCrypto.Hash(PcCrypto.Text($"omnicontrol/session/{PcProtocol.Version}"), new[] { kind }, diallerNonce, listenerNonce, diallerPoint, listenerPoint,
            PcCrypto.Text(diallerId), PcCrypto.Text(listenerId));

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    /// One key per direction from the fresh keys' shared secret and the pairing key, bound to everything said.
    internal static (byte[] ToListener, byte[] ToDialler) DirectionKeys(byte[] shared, byte[] secret, byte[] said)
    {
        var both = Join(shared, secret);
        return (PcCrypto.Derive(both, said, "omnicontrol/session/dialler-to-listener"), PcCrypto.Derive(both, said, "omnicontrol/session/listener-to-dialler"));
    }

    private static PcLink Link(string peerId, Stream stream, byte[] secret, byte[] shared, byte[] said, bool dialling)
    {
        var (toListener, toDialler) = DirectionKeys(shared, secret, said);
        return dialling ? new PcLink(peerId, stream, toListener, toDialler, dialled: true) : new PcLink(peerId, stream, toDialler, toListener, dialled: false);
    }
}
