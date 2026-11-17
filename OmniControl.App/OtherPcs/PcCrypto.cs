using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OmniControl.App.OtherPcs;

/// <summary>
/// The primitives of the link, all from .NET's own cryptography (no home-made cipher): SHA-256, HMAC-SHA256, HKDF, ECDH on P-256 and
/// AES-256-GCM. Pure functions, no state.
/// </summary>
internal static class PcCrypto
{
    public static byte[] Random(int length) => RandomNumberGenerator.GetBytes(length);

    /// <summary>SHA-256 of the parts, each length-prefixed so two different splits of the same bytes never hash alike.</summary>
    public static byte[] Hash(params byte[][] parts)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var part in parts)
        {
            sha.AppendData(BitConverter.GetBytes(part.Length));
            sha.AppendData(part);
        }
        return sha.GetHashAndReset();
    }

    public static byte[] Mac(byte[] key, params byte[][] parts) => HMACSHA256.HashData(key, Hash(parts));

    public static bool Same(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);

    public static byte[] Derive(byte[] secret, byte[] salt, string purpose) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, PcProtocol.KeyBytes, salt, Encoding.ASCII.GetBytes(purpose));

    public static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A new ephemeral key pair and its public half (what is sent to the other PC).</summary>
    public static (ECDiffieHellman Key, byte[] Public) NewKeyPair()
    {
        var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return (key, key.ExportSubjectPublicKeyInfo());
    }

    /// <summary>The secret both PCs share from their own private key and the other's public half; null when that half is not a valid P-256 key.</summary>
    public static byte[]? Agree(ECDiffieHellman own, byte[] otherPublic)
    {
        try
        {
            using var other = ECDiffieHellman.Create();
            other.ImportSubjectPublicKeyInfo(otherPublic, out _);
            return own.DeriveRawSecretAgreement(other.PublicKey);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>The 6-digit code both PCs compute from the whole exchange; a PC in the middle can only make the two show different codes.</summary>
    public static string PairingCode(byte[] initiatorKey, byte[] responderKey, byte[] initiatorNonce, byte[] responderNonce, string initiatorId, string responderId)
    {
        var digest = Hash(Text("code"), initiatorKey, responderKey, initiatorNonce, responderNonce, Text(initiatorId), Text(responderId));
        return (BinaryPrimitives.ReadUInt32BigEndian(digest) % 1_000_000).ToString("D6");
    }
}

/// <summary>
/// One direction of an encrypted link: AES-256-GCM with a counter as the nonce (never reused, since each direction has its own key and the
/// counter only goes up). The receiving side demands the counter it expects next, so a copied or reordered message fails to open.
/// </summary>
internal sealed class PcCipher : IDisposable
{
    private const int TagBytes = 16, NonceBytes = 12;
    private readonly AesGcm _aes;
    private ulong _counter;

    public PcCipher(byte[] key) => _aes = new AesGcm(key, TagBytes);

    private byte[] NextNonce()
    {
        var nonce = new byte[NonceBytes];
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), _counter++);
        return nonce;
    }

    /// <summary>The message sealed: ciphertext then tag.</summary>
    public byte[] Seal(byte[] plain)
    {
        var sealedBytes = new byte[plain.Length + TagBytes];
        _aes.Encrypt(NextNonce(), plain, sealedBytes.AsSpan(0, plain.Length), sealedBytes.AsSpan(plain.Length));
        return sealedBytes;
    }

    /// <summary>The message opened, or null when it was changed, replayed or is not from the key's owner.</summary>
    public byte[]? Open(byte[] sealedBytes)
    {
        if (sealedBytes.Length < TagBytes) return null;
        var plain = new byte[sealedBytes.Length - TagBytes];
        try
        {
            _aes.Decrypt(NextNonce(), sealedBytes.AsSpan(0, plain.Length), sealedBytes.AsSpan(plain.Length), plain);
            return plain;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Dispose() => _aes.Dispose();
}
