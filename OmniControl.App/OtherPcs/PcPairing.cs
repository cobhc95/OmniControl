using System.Security.Cryptography;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Tailscale;

namespace OmniControl.App.OtherPcs;

/// <summary>One message of a pairing (every field optional, the step decides which are used).</summary>
internal sealed class PairMsg
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public byte[]? Commit { get; set; }
    public byte[]? Pk { get; set; }
    public byte[]? Nonce { get; set; }

    /// <summary>The sender's Tailscale address (initiator: only when pairing over the internet; responder: only as the answer to one), so either PC can dial the other later.</summary>
    public string? Addr { get; set; }
    public byte[]? Tag { get; set; }
    public bool Abort { get; set; }
}

/// <summary>How a pairing ended: the other PC and the secret both now keep, or one plain sentence saying why not.</summary>
internal sealed record PairResult(string? PeerId, string? PeerName, byte[]? Secret, string? Problem, string PeerAddress = "")
{
    public bool Paired => Secret is not null;
    public static PairResult Failed(string problem) => new(null, null, null, problem);
}

/// <summary>
/// The pairing exchange: a numeric comparison. The PC that asks to pair (the initiator) first commits to its key and nonce, the other
/// (the responder) answers with its own, then the initiator reveals; both then compute the same 6-digit code from everything said, the
/// responder shows it and the person types it on the initiator. A PC in the middle has had to commit to its key before seeing the other
/// side's nonce, so the two PCs show different codes unless it guessed (one chance in a million, and the person sees the mismatch).
/// When the typed code matches, the shared secret (ECDH of the two keys, through HKDF) is the pairing key, and each PC proves it holds the
/// same key with a MAC over the transcript. Nothing is stored before both proofs check.
/// </summary>
internal static class PcPairing
{
    private const string SecretPurpose = "omnicontrol/pair/secret";

    public static async Task<PairResult> InitiateAsync(Stream stream, string ownId, string ownName, string ownAddress, Func<string, Task<string?>> askCode, CancellationToken ct)
    {
        using var keyPair = PcCrypto.NewKeyPair().Key;
        var ownKey = keyPair.ExportSubjectPublicKeyInfo();
        var ownNonce = PcCrypto.Random(PcProtocol.KeyBytes);
        await stream.WriteAsync(new[] { PcProtocol.WantsPairing }, ct).ConfigureAwait(false);
        await PcWire.WriteJsonAsync(stream, new PairMsg { Id = ownId, Name = ownName, Addr = ownAddress, Commit = PcCrypto.Hash(ownKey, ownNonce) }, ct).ConfigureAwait(false);

        using var firstStep = PcWire.Step(ct);
        if (await PcWire.ReadJsonAsync<PairMsg>(stream, firstStep.Token).ConfigureAwait(false) is not { Pk: { } peerKey, Nonce: { } peerNonce } reply
            || !PcIds.IsValid(reply.Id))
            return PairResult.Failed("The other PC did not answer as a PC pairing with OmniControl does.");
        await PcWire.WriteJsonAsync(stream, new PairMsg { Pk = ownKey, Nonce = ownNonce }, ct).ConfigureAwait(false);

        var peerName = PcIds.CleanName(reply.Name);
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
        var transcript = PcCrypto.Hash(PcCrypto.Text(ownId), PcCrypto.Text(reply.Id!), ownKey, peerKey, ownNonce, peerNonce, PcCrypto.Text(ownAddress), PcCrypto.Text(reply.Addr ?? string.Empty));
        await PcWire.WriteJsonAsync(stream, new PairMsg { Tag = PcCrypto.Mac(secret, PcCrypto.Text("initiator"), transcript) }, ct).ConfigureAwait(false);

        using var lastStep = PcWire.Step(ct);
        if (await PcWire.ReadJsonAsync<PairMsg>(stream, lastStep.Token).ConfigureAwait(false) is not { Tag: { } proof }
            || !PcCrypto.Same(proof, PcCrypto.Mac(secret, PcCrypto.Text("responder"), transcript)))
            return PairResult.Failed("The other PC could not prove it holds the same key. Nothing was paired.");
        return new PairResult(reply.Id, peerName, secret, null, peerAddress);
    }

    /// <param name="show">Shows the code to the person (who types it on the other PC), with the name the asking PC gave.</param>
    public static async Task<PairResult> RespondAsync(Stream stream, string ownId, string ownName, string ownAddress, Action<string, string> show, CancellationToken ct)
    {
        using var firstStep = PcWire.Step(ct);
        if (await PcWire.ReadJsonAsync<PairMsg>(stream, firstStep.Token).ConfigureAwait(false) is not { Commit: { } commit } hello || !PcIds.IsValid(hello.Id))
            return PairResult.Failed("The other PC did not ask as a PC pairing with OmniControl does.");
        using var keyPair = PcCrypto.NewKeyPair().Key;
        var ownKey = keyPair.ExportSubjectPublicKeyInfo();
        var ownNonce = PcCrypto.Random(PcProtocol.KeyBytes);
        var peerAddress = Address(hello.Addr);
        var answerAddress = peerAddress.Length > 0 ? ownAddress : string.Empty; // an address is given back only to a PC that gave one
        await PcWire.WriteJsonAsync(stream, new PairMsg { Id = ownId, Name = ownName, Addr = answerAddress, Pk = ownKey, Nonce = ownNonce }, ct).ConfigureAwait(false);

        using var revealStep = PcWire.Step(ct);
        if (await PcWire.ReadJsonAsync<PairMsg>(stream, revealStep.Token).ConfigureAwait(false) is not { Pk: { } peerKey, Nonce: { } peerNonce }
            || !PcCrypto.Same(commit, PcCrypto.Hash(peerKey, peerNonce)))
            return PairResult.Failed("The other PC changed its answer while pairing. Nothing was paired.");

        var peerName = PcIds.CleanName(hello.Name);
        show(peerName, PcCrypto.PairingCode(peerKey, ownKey, peerNonce, ownNonce, hello.Id!, ownId));

        // The person types the code on the other PC, so this step waits as long as pairing stays open.
        using var typingStep = PcWire.Step(ct, Timing.PcPairingOpen);
        if (await PcWire.ReadJsonAsync<PairMsg>(stream, typingStep.Token).ConfigureAwait(false) is not { Abort: false, Tag: { } proof })
            return PairResult.Failed("The code was not typed correctly on the other PC. Nothing was paired.");
        if (PcCrypto.Agree(keyPair, peerKey) is not { } agreed) return PairResult.Failed("The other PC sent a key that cannot be used.");
        var secret = PcCrypto.Derive(agreed, PcCrypto.Hash(peerNonce, ownNonce), SecretPurpose);
        var transcript = PcCrypto.Hash(PcCrypto.Text(hello.Id!), PcCrypto.Text(ownId), peerKey, ownKey, peerNonce, ownNonce, PcCrypto.Text(hello.Addr ?? string.Empty), PcCrypto.Text(answerAddress));
        if (!PcCrypto.Same(proof, PcCrypto.Mac(secret, PcCrypto.Text("initiator"), transcript)))
            return PairResult.Failed("The other PC could not prove it holds the same key. Nothing was paired.");
        await PcWire.WriteJsonAsync(stream, new PairMsg { Tag = PcCrypto.Mac(secret, PcCrypto.Text("responder"), transcript) }, ct).ConfigureAwait(false);
        return new PairResult(hello.Id, peerName, secret, null, peerAddress);
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

/// <summary>A PC's id (32 lowercase hex digits, a random 128-bit number) and the name it gives itself.</summary>
internal static class PcIds
{
    public static string New() => Convert.ToHexString(RandomNumberGenerator.GetBytes(PcProtocol.IdBytes)).ToLowerInvariant();

    public static bool IsValid(string? id) => id is { Length: PcProtocol.IdBytes * 2 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static byte[] Bytes(string id) => Convert.FromHexString(id);

    public static string FromBytes(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private const int NameMax = 40;

    /// <summary>A name another PC gave: trimmed, no control characters, not too long (it is shown and put in the master lists).</summary>
    public static string CleanName(string? name)
    {
        var clean = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length == 0 ? "PC" : clean.Length > NameMax ? clean[..NameMax] : clean;
    }
}
