using System.Security.Cryptography;
using OmniControl.Core.Engine.Tailscale;

namespace OmniControl.App.Features.OtherPcs;

/// One message of a pairing (every field optional, the step decides which are used).
internal sealed class PairMsg
{
    /// The pairing exchange's version (<see cref="PcPairing.Version"/>); a PC on another version is refused with one sentence.
    public int? V { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public byte[]? Commit { get; set; }
    public byte[]? Pk { get; set; }
    public byte[]? Nonce { get; set; }

    /// The sender's Tailscale address (initiator: only when pairing over the internet; responder: only as the answer to one), so either PC can dial the other later.
    public string? Addr { get; set; }
    public byte[]? Tag { get; set; }
    public bool Abort { get; set; }
}

/// How a pairing ended: the other PC and the secret both now keep, or one plain sentence saying why not.
internal sealed record PairResult(string? PeerId, string? PeerName, byte[]? Secret, string? Problem, string PeerAddress = "")
{
    public bool Paired => Secret is not null;
    public static PairResult Failed(string problem) => new(null, null, null, problem);
}

/// The pairing exchange: a numeric comparison. The PC that asks to pair (the initiator) first commits to its key and nonce, the other
/// (the responder) answers with its own, then the initiator reveals; both then compute the same 6-digit code from everything said, the
/// responder shows it and the person types it on the initiator. A PC in the middle has had to commit to its key before seeing the other
/// side's nonce, so the two PCs show different codes unless it guessed (one chance in a million, and the person sees the mismatch).
/// When the typed code matches, the shared secret (ECDH of the two keys, through HKDF) is the pairing key, and each PC proves it holds the
/// same key with a MAC over the transcript (ids, names, keys, nonces, addresses). The PC that showed the code then asks its person
/// "Pair with &lt;name&gt;?" before it sends its proof, so both people agreed. A PC already paired is refused until it is unpaired.
/// Nothing is stored before both proofs check.
internal static class PcPairing
{
    /// The exchange's version: 2 put the names in the transcript and the confirmation on the PC showing the code.
    public const int Version = 2;

    private const string SecretPurpose = "omnicontrol/pair/secret";
    private const string OtherVersion = "The other PC runs another version of OmniControl. Update both PCs, then pair.";

    /// <param name="isPaired">True for a PC id this PC is already paired with (refused until it is unpaired).</param>
    public static async Task<PairResult> InitiateAsync(Stream stream, string ownId, string ownName, string ownAddress, Func<string, bool> isPaired, Func<string, Task<string?>> askCode, CancellationToken ct)
    {
        using var keyPair = PcCrypto.NewKeyPair().Key;
        var ownKey = keyPair.ExportSubjectPublicKeyInfo();
        var ownNonce = PcCrypto.Random(PcProtocol.KeyBytes);
        await stream.WriteAsync(new[] { PcProtocol.WantsPairing }, ct).ConfigureAwait(false);
        await PcWire.WriteJsonAsync(stream, new PairMsg { V = Version, Id = ownId, Name = ownName, Addr = ownAddress, Commit = PcCrypto.Hash(ownKey, ownNonce) }, ct).ConfigureAwait(false);

        using var firstStep = PcWire.Step(ct);
        var reply = await PcWire.ReadJsonAsync<PairMsg>(stream, firstStep.Token).ConfigureAwait(false);
        if (reply is { Abort: true }) return PairResult.Failed("The other PC refused: it is already paired with this PC (unpair it there first) or runs another version of OmniControl.");
        if (reply is not { Pk: { } peerKey, Nonce: { } peerNonce } || !PcIds.IsValid(reply.Id))
            return PairResult.Failed("The other PC did not answer as a PC pairing with OmniControl does.");
        var peerName = PcIds.CleanName(reply.Name);
        if (reply.V != Version || isPaired(reply.Id!))
        {
            await Tell(stream, new PairMsg { Abort = true }, ct).ConfigureAwait(false);
            return PairResult.Failed(reply.V != Version ? OtherVersion : $"{peerName} is already paired. Unpair it first to pair again.");
        }
        await PcWire.WriteJsonAsync(stream, new PairMsg { Pk = ownKey, Nonce = ownNonce }, ct).ConfigureAwait(false);

        var code = PcCrypto.PairingCode(ownKey, peerKey, ownNonce, peerNonce, ownId, reply.Id!);
        var typed = await askCode(peerName).ConfigureAwait(false);
        if (typed is null || typed.Replace(" ", string.Empty) != code)
        {
            await Tell(stream, new PairMsg { Abort = true }, ct).ConfigureAwait(false);
            return PairResult.Failed(typed is null ? "Pairing was cancelled." : "The code did not match. Nothing was paired.");
        }

        if (PcCrypto.Agree(keyPair, peerKey) is not { } agreed) return PairResult.Failed("The other PC sent a key that cannot be used.");
        var secret = PcCrypto.Derive(agreed, PcCrypto.Hash(ownNonce, peerNonce), SecretPurpose);
        var peerAddress = Address(reply.Addr);
        var transcript = Transcript(ownId, reply.Id!, ownName, reply.Name, ownKey, peerKey, ownNonce, peerNonce, ownAddress, reply.Addr);
        await PcWire.WriteJsonAsync(stream, new PairMsg { Tag = PcCrypto.Mac(secret, PcCrypto.Text("initiator"), transcript) }, ct).ConfigureAwait(false);

        // The person at the other PC confirms the pairing there first.
        using var lastStep = PcWire.Step(ct, Timing.PcPairingOpen);
        var last = await PcWire.ReadJsonAsync<PairMsg>(stream, lastStep.Token).ConfigureAwait(false);
        if (last is { Abort: true }) return PairResult.Failed($"Pairing was cancelled on {peerName}. Nothing was paired.");
        if (last is not { Tag: { } proof } || !PcCrypto.Same(proof, PcCrypto.Mac(secret, PcCrypto.Text("responder"), transcript)))
            return PairResult.Failed("The other PC could not prove it holds the same key. Nothing was paired.");
        return new PairResult(reply.Id, peerName, secret, null, peerAddress);
    }

    /// <param name="isPaired">True for a PC id this PC is already paired with (refused until it is unpaired).</param>
    /// <param name="show">Shows the code to the person (who types it on the other PC), with the name the asking PC gave.</param>
    /// <param name="confirm">Asks the person here "Pair with &lt;name&gt;?" once the code was typed correctly on the other PC; false gives the pairing up.</param>
    public static async Task<PairResult> RespondAsync(Stream stream, string ownId, string ownName, string ownAddress, Func<string, bool> isPaired, Action<string, string> show,
        Func<string, CancellationToken, Task<bool>> confirm, CancellationToken ct)
    {
        using var firstStep = PcWire.Step(ct);
        if (await PcWire.ReadJsonAsync<PairMsg>(stream, firstStep.Token).ConfigureAwait(false) is not { Commit: { } commit } hello || !PcIds.IsValid(hello.Id))
            return PairResult.Failed("The other PC did not ask as a PC pairing with OmniControl does.");
        var peerName = PcIds.CleanName(hello.Name);
        if (hello.V != Version || isPaired(hello.Id!))
        {
            await Tell(stream, new PairMsg { Abort = true }, ct).ConfigureAwait(false);
            return PairResult.Failed(hello.V != Version ? OtherVersion : $"{peerName} is already paired. Unpair it first to pair again.");
        }
        using var keyPair = PcCrypto.NewKeyPair().Key;
        var ownKey = keyPair.ExportSubjectPublicKeyInfo();
        var ownNonce = PcCrypto.Random(PcProtocol.KeyBytes);
        var peerAddress = Address(hello.Addr);
        var answerAddress = peerAddress.Length > 0 ? ownAddress : string.Empty; // an address is given back only to a PC that gave one
        await PcWire.WriteJsonAsync(stream, new PairMsg { V = Version, Id = ownId, Name = ownName, Addr = answerAddress, Pk = ownKey, Nonce = ownNonce }, ct).ConfigureAwait(false);

        using var revealStep = PcWire.Step(ct);
        if (await PcWire.ReadJsonAsync<PairMsg>(stream, revealStep.Token).ConfigureAwait(false) is not { Pk: { } peerKey, Nonce: { } peerNonce }
            || !PcCrypto.Same(commit, PcCrypto.Hash(peerKey, peerNonce)))
            return PairResult.Failed("The other PC changed its answer while pairing. Nothing was paired.");

        show(peerName, PcCrypto.PairingCode(peerKey, ownKey, peerNonce, ownNonce, hello.Id!, ownId));

        // The person types the code on the other PC, so this step waits as long as pairing stays open.
        using var typingStep = PcWire.Step(ct, Timing.PcPairingOpen);
        if (await PcWire.ReadJsonAsync<PairMsg>(stream, typingStep.Token).ConfigureAwait(false) is not { Abort: false, Tag: { } proof })
            return PairResult.Failed("The code was not typed correctly on the other PC. Nothing was paired.");
        if (PcCrypto.Agree(keyPair, peerKey) is not { } agreed) return PairResult.Failed("The other PC sent a key that cannot be used.");
        var secret = PcCrypto.Derive(agreed, PcCrypto.Hash(peerNonce, ownNonce), SecretPurpose);
        var transcript = Transcript(hello.Id!, ownId, hello.Name, ownName, peerKey, ownKey, peerNonce, ownNonce, hello.Addr, answerAddress);
        if (!PcCrypto.Same(proof, PcCrypto.Mac(secret, PcCrypto.Text("initiator"), transcript)))
            return PairResult.Failed("The other PC could not prove it holds the same key. Nothing was paired.");
        using var confirmStep = PcWire.Step(ct, Timing.PcPairingOpen);
        if (!await confirm(peerName, confirmStep.Token).ConfigureAwait(false))
        {
            await Tell(stream, new PairMsg { Abort = true }, ct).ConfigureAwait(false);
            return PairResult.Failed("Pairing was cancelled. Nothing was paired.");
        }
        await PcWire.WriteJsonAsync(stream, new PairMsg { Tag = PcCrypto.Mac(secret, PcCrypto.Text("responder"), transcript) }, ct).ConfigureAwait(false);
        return new PairResult(hello.Id, peerName, secret, null, peerAddress);
    }

    // Everything both PCs said, initiator first; the names as sent, so a name changed on the way fails the proofs.
    private static byte[] Transcript(string initiatorId, string responderId, string? initiatorName, string? responderName, byte[] initiatorKey, byte[] responderKey,
        byte[] initiatorNonce, byte[] responderNonce, string? initiatorAddress, string? responderAddress) =>
        PcCrypto.Hash(PcCrypto.Text(initiatorId), PcCrypto.Text(responderId), PcCrypto.Text(initiatorName ?? string.Empty), PcCrypto.Text(responderName ?? string.Empty),
            initiatorKey, responderKey, initiatorNonce, responderNonce, PcCrypto.Text(initiatorAddress ?? string.Empty), PcCrypto.Text(responderAddress ?? string.Empty));

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
