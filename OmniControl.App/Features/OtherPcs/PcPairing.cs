using System.Security.Cryptography;
using OmniControl.Core.Engine.Tailscale;

namespace OmniControl.App.Features.OtherPcs;

/// One message of a pairing (every field optional, the step decides which are used).
internal sealed class PairMsg
{
    /// The sender's protocol version (<see cref="PcProtocol.Version"/>); a PC on another version is refused with one sentence.
    public int? V { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public byte[]? Pk { get; set; }
    public byte[]? Nonce { get; set; }

    /// The sender's Tailscale address (initiator: only when pairing over the internet; responder: only as the answer to one), so either PC can dial the other later.
    public string? Addr { get; set; }
    public byte[]? Tag { get; set; }
    public bool Abort { get; set; }
}

/// How a pairing ended: the other PC and the secret both now keep, or one plain sentence saying why not (none for a connection that was
/// not a pairing at all); <see cref="WrongCode"/> when the other PC did not prove it knew the code shown here.
internal sealed record PairResult(string? PeerId, string? PeerName, byte[]? Secret, string? Problem, string PeerAddress = "")
{
    public bool Paired => Secret is not null;
    public bool WrongCode { get; init; }
    public static PairResult Failed(string? problem) => new(null, null, null, problem);
}

/// The pairing exchange (protocol 3): the PC that is asked (the responder) makes a random 6-digit code for this one exchange and shows it; the
/// person types it on the PC that asked (the initiator). The code is never sent: both PCs make an ECDH key on a curve whose generator is
/// hashed from the code and everything said so far (CPace, <see cref="PcCrypto.CodeCurve"/>), so their keys agree only when the typed code is
/// the shown one, and a PC that does not know it gets one guess per exchange (one in a million) and nothing to search offline. The responder
/// proves first, then the initiator, so the responder knows the code was typed on the PC it talks to; its person then confirms "Pair with
/// &lt;name&gt;?" and it sends a last proof. Every proof is a MAC over the transcript (version, ids, names, addresses, nonces, both keys), and the
/// pairing key comes from the shared secret through HKDF. A PC already paired is refused until it is unpaired; nothing is stored before both
/// proofs check.
internal static class PcPairing
{
    private const int Version = PcProtocol.Version;

    private const string OtherVersion = "The other PC runs another version of OmniControl. Update both PCs, then pair.";

    /// <param name="isPaired">True for a PC id this PC is already paired with (refused until it is unpaired).</param>
    /// <param name="askCode">Asks the person here for the code the other PC shows (null: given up).</param>
    public static async Task<PairResult> InitiateAsync(Stream stream, string ownId, string ownName, string ownAddress, Func<string, bool> isPaired, Func<string, Task<string?>> askCode, CancellationToken ct)
    {
        var ownNonce = PcCrypto.Random(PcProtocol.KeyBytes);
        await stream.WriteAsync(new[] { PcProtocol.WantsPairing }, ct).ConfigureAwait(false);
        await PcWire.WriteJsonAsync(stream, new PairMsg { V = Version, Id = ownId, Name = ownName, Addr = ownAddress, Nonce = ownNonce }, ct).ConfigureAwait(false);

        using var firstStep = PcWire.Step(ct);
        var reply = await PcWire.ReadJsonAsync<PairMsg>(stream, firstStep.Token).ConfigureAwait(false);
        if (reply is { Abort: true }) return PairResult.Failed("The other PC refused: it is already paired with this PC (unpair it there first) or runs another version of OmniControl.");
        if (reply is not { Nonce.Length: PcProtocol.KeyBytes } || !PcIds.IsValid(reply.Id))
            return PairResult.Failed("The other PC did not answer as a PC pairing with OmniControl does. Both PCs need the same version.");
        var peerName = PcIds.CleanName(reply.Name);
        if (reply.V != Version || isPaired(reply.Id!))
        {
            await Tell(stream, new PairMsg { Abort = true }, ct).ConfigureAwait(false);
            return PairResult.Failed(reply.V != Version ? OtherVersion : $"{peerName} is already paired. Unpair it first to pair again.");
        }

        var code = await askCode(peerName).ConfigureAwait(false);
        if (code is null)
        {
            await Tell(stream, new PairMsg { Abort = true }, ct).ConfigureAwait(false);
            return PairResult.Failed("Pairing was cancelled.");
        }
        var exchange = new Exchange(ownId, reply.Id!, ownName, reply.Name, ownAddress, reply.Addr, ownNonce, reply.Nonce!);
        var curve = exchange.Curve(code);
        var made = PcCrypto.NewKey(curve);
        using var key = made.Key;
        var ownPoint = made.Point;
        await PcWire.WriteJsonAsync(stream, new PairMsg { Pk = ownPoint }, ct).ConfigureAwait(false);

        using var answerStep = PcWire.Step(ct);
        var answer = await PcWire.ReadJsonAsync<PairMsg>(stream, answerStep.Token).ConfigureAwait(false);
        if (answer is { Abort: true }) return PairResult.Failed($"{peerName} gave the pairing up. Nothing was paired.");
        if (answer is not { Pk: { } peerPoint, Tag: { } peerTag } || PcCrypto.Agree(key, peerPoint, curve) is not { } shared)
            return PairResult.Failed("The other PC sent a key that cannot be used. Nothing was paired.");
        var keys = exchange.KeysFrom(shared, ownPoint, peerPoint);
        if (!PcCrypto.Same(peerTag, keys.Tag(Responder)))
        {
            await Tell(stream, new PairMsg { Abort = true }, ct).ConfigureAwait(false);
            return PairResult.Failed("The code did not match. Nothing was paired. Pick the PC again for a new code.");
        }
        await PcWire.WriteJsonAsync(stream, new PairMsg { Tag = keys.Tag(Initiator) }, ct).ConfigureAwait(false);

        // The person at the other PC confirms the pairing there first.
        using var lastStep = PcWire.Step(ct, Timing.PcPairingOpen);
        var last = await PcWire.ReadJsonAsync<PairMsg>(stream, lastStep.Token).ConfigureAwait(false);
        if (last is { Abort: true }) return PairResult.Failed($"Pairing was cancelled on {peerName}. Nothing was paired.");
        if (last is not { Tag: { } done } || !PcCrypto.Same(done, keys.Tag(Confirmed)))
            return PairResult.Failed("The other PC could not prove it holds the same key. Nothing was paired.");
        return new PairResult(reply.Id, peerName, keys.Secret, null, Address(reply.Addr));
    }

    /// <param name="isPaired">True for a PC id this PC is already paired with (refused until it is unpaired).</param>
    /// <param name="show">Shows this exchange's code to the person (who types it on the other PC), with the name the asking PC gave.</param>
    /// <param name="confirm">Asks the person here "Pair with &lt;name&gt;?" once the other PC proved it knew the code; false gives the pairing up.</param>
    public static async Task<PairResult> RespondAsync(Stream stream, string ownId, string ownName, string ownAddress, Func<string, bool> isPaired, Action<string, string> show,
        Func<string, CancellationToken, Task<bool>> confirm, CancellationToken ct)
    {
        using var firstStep = PcWire.Step(ct);
        if (await PcWire.ReadJsonAsync<PairMsg>(stream, firstStep.Token).ConfigureAwait(false) is not { Nonce.Length: PcProtocol.KeyBytes } hello || !PcIds.IsValid(hello.Id))
            return PairResult.Failed(null); // not a pairing at all: nothing is said
        var peerName = PcIds.CleanName(hello.Name);
        if (hello.V != Version || isPaired(hello.Id!))
        {
            await Tell(stream, new PairMsg { Abort = true }, ct).ConfigureAwait(false);
            return PairResult.Failed(hello.V != Version ? OtherVersion : $"{peerName} is already paired. Unpair it first to pair again.");
        }
        var ownNonce = PcCrypto.Random(PcProtocol.KeyBytes);
        var peerAddress = Address(hello.Addr);
        var answerAddress = peerAddress.Length > 0 ? ownAddress : string.Empty; // an address is given back only to a PC that gave one
        await PcWire.WriteJsonAsync(stream, new PairMsg { V = Version, Id = ownId, Name = ownName, Addr = answerAddress, Nonce = ownNonce }, ct).ConfigureAwait(false);
        var code = PcCrypto.NewCode();
        show(peerName, code);

        // The person types the code on the other PC, so this step waits as long as pairing stays open.
        using var typingStep = PcWire.Step(ct, Timing.PcPairingOpen);
        var typed = await PcWire.ReadJsonAsync<PairMsg>(stream, typingStep.Token).ConfigureAwait(false);
        if (typed is { Abort: true }) return PairResult.Failed($"Pairing was cancelled on {peerName}. Nothing was paired.");
        if (typed is not { Pk: { } peerPoint }) return PairResult.Failed("The code was not typed on the other PC. Nothing was paired.");
        var exchange = new Exchange(hello.Id!, ownId, hello.Name, ownName, hello.Addr, answerAddress, hello.Nonce!, ownNonce);
        var curve = exchange.Curve(code);
        var made = PcCrypto.NewKey(curve);
        using var key = made.Key;
        var ownPoint = made.Point;
        if (PcCrypto.Agree(key, peerPoint, curve) is not { } shared) return PairResult.Failed("The other PC sent a key that cannot be used. Nothing was paired.");
        var keys = exchange.KeysFrom(shared, peerPoint, ownPoint);
        await PcWire.WriteJsonAsync(stream, new PairMsg { Pk = ownPoint, Tag = keys.Tag(Responder) }, ct).ConfigureAwait(false);

        // From here every end but the right proof counts as a wrong code: the other PC has had its one guess.
        using var proofStep = PcWire.Step(ct);
        if (await PcWire.ReadJsonAsync<PairMsg>(stream, proofStep.Token).ConfigureAwait(false) is not { Tag: { } proof } || !PcCrypto.Same(proof, keys.Tag(Initiator)))
            return PairResult.Failed($"A wrong code was typed on {peerName}. Nothing was paired.") with { WrongCode = true };
        using var confirmStep = PcWire.Step(ct, Timing.PcPairingOpen);
        if (!await confirm(peerName, confirmStep.Token).ConfigureAwait(false))
        {
            await Tell(stream, new PairMsg { Abort = true }, ct).ConfigureAwait(false);
            return PairResult.Failed("Pairing was cancelled. Nothing was paired.");
        }
        await PcWire.WriteJsonAsync(stream, new PairMsg { Tag = keys.Tag(Confirmed) }, ct).ConfigureAwait(false);
        return new PairResult(hello.Id, peerName, keys.Secret, null, peerAddress);
    }

    private const string Initiator = "initiator", Responder = "responder", Confirmed = "confirmed";

    /// What both PCs said before the keys, initiator first (the names as sent, so a name changed on the way fails the proofs): the context the
    /// code's generator, the keys and the proofs are bound to.
    private sealed record Exchange(string InitiatorId, string ResponderId, string? InitiatorName, string? ResponderName, string? InitiatorAddress, string? ResponderAddress,
        byte[] InitiatorNonce, byte[] ResponderNonce)
    {
        private byte[] Context => PcCrypto.Hash(PcCrypto.Text($"omnicontrol/pair/{Version}"), PcCrypto.Text(InitiatorId), PcCrypto.Text(ResponderId),
            PcCrypto.Text(InitiatorName ?? string.Empty), PcCrypto.Text(ResponderName ?? string.Empty), PcCrypto.Text(InitiatorAddress ?? string.Empty),
            PcCrypto.Text(ResponderAddress ?? string.Empty), InitiatorNonce, ResponderNonce);

        public ECCurve Curve(string code) => PcCrypto.CodeCurve(code.Replace(" ", string.Empty), Context);

        public Keys KeysFrom(byte[] shared, byte[] initiatorPoint, byte[] responderPoint)
        {
            var transcript = PcCrypto.Hash(Context, initiatorPoint, responderPoint);
            return new Keys(PcCrypto.Derive(shared, transcript, "omnicontrol/pair/secret"), PcCrypto.Derive(shared, transcript, "omnicontrol/pair/confirm"), transcript);
        }
    }

    private sealed record Keys(byte[] Secret, byte[] Confirm, byte[] Transcript)
    {
        public byte[] Tag(string role) => PcCrypto.Mac(Confirm, PcCrypto.Text(role), Transcript);
    }

    // An address the other PC gave is kept only when it is one Tailscale hands out; anything else is ignored (it is part of what both PCs prove, so it cannot be changed on the way).
    private static string Address(string? given) => TailscaleStatusReader.IsTailscaleAddress(given) ? given! : string.Empty;

    // The abort is a courtesy: the other side learns at once instead of waiting out its time.
    private static async Task Tell(Stream stream, PairMsg message, CancellationToken ct)
    {
        try
        {
            await PcWire.WriteJsonAsync(stream, message, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (PcWire.IsNetworkError(ex))
        {
            // The other PC already went away; nothing is left to tell it.
        }
    }
}

/// A PC's id (32 lowercase hex digits, a random 128-bit number) and the name it gives itself.
internal static class PcIds
{
    public static string New() => Convert.ToHexString(RandomNumberGenerator.GetBytes(PcProtocol.IdBytes)).ToLowerInvariant();

    public static bool IsValid(string? id) => id is { Length: PcProtocol.IdBytes * 2 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static byte[] Bytes(string id) => Convert.FromHexString(id);

    public static string FromBytes(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private const int NameMax = 40;

    /// A name another PC gave: trimmed, no control or format characters (a right-to-left override would make it read as another name), not too long (it is shown and put in the master lists).
    public static string CleanName(string? name)
    {
        var clean = PcProtocol.Clip(name, int.MaxValue);
        return clean.Length == 0 ? "PC" : clean.Length > NameMax ? clean[..NameMax] : clean;
    }
}
