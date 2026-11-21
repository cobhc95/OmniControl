using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace OmniControl.App.Features.OtherPcs;

/// The primitives of the link, all from .NET's own cryptography (no home-made cipher): SHA-256, HMAC-SHA256, HKDF, ECDH on P-256 and
/// AES-256-GCM. Pure functions, no state.
internal static class PcCrypto
{
    /// A public key on the wire: the point's X then Y, 32 bytes each.
    public const int PointBytes = 64;

    public static byte[] Random(int length) => RandomNumberGenerator.GetBytes(length);

    /// SHA-256 of the parts, each length-prefixed so two different splits of the same bytes never hash alike.
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

    /// A new ephemeral key pair on <paramref name="curve"/> (P-256 when none) and its public point (what is sent to the other PC).
    public static (ECDiffieHellman Key, byte[] Point) NewKey(ECCurve? curve = null)
    {
        var key = ECDiffieHellman.Create(curve ?? ECCurve.NamedCurves.nistP256);
        var q = key.ExportParameters(false).Q;
        return (key, Fixed(q.X!).Concat(Fixed(q.Y!)).ToArray());
    }

    /// The secret both PCs share from their own private key and the other's public point; null when that point is not one of the curve's.
    /// Windows checks the point as it imports it (CNG validates public keys unless told not to); an invalid point on a named curve comes
    /// back as PlatformNotSupportedException, on an explicit one as CryptographicException.
    public static byte[]? Agree(ECDiffieHellman own, byte[] otherPoint, ECCurve? curve = null)
    {
        if (otherPoint.Length != PointBytes) return null;
        try
        {
            using var other = ECDiffieHellman.Create(new ECParameters
            {
                Curve = curve ?? ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = otherPoint[..(PointBytes / 2)], Y = otherPoint[(PointBytes / 2)..] },
            });
            return own.DeriveRawSecretAgreement(other.PublicKey);
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// A new 6-digit pairing code, shown on the PC that is asked and typed on the PC that asks.
    public static string NewCode() => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");

    /// P-256 with its generator replaced by a point hashed from the code and <paramref name="context"/> (CPace, draft-irtf-cfrg-cpace): two
    /// keys made on it agree only when both PCs used the same code, and a PC that does not know the code gets one guess per exchange, never an
    /// offline search. Hash to the curve by try and increment (about two tries); its time depends on the code, which is why the PC that shows
    /// the code builds it only after the other PC sent its key, and a code serves one exchange.
    public static ECCurve CodeCurve(string code, byte[] context)
    {
        using var named = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var curve = named.ExportExplicitParameters(false).Curve;
        var p = Number(curve.Prime!);
        var a = Number(curve.A!);
        var b = Number(curve.B!);
        for (var i = 0; i < 256; i++)
        {
            var x = Number(Hash(Text("omnicontrol/pair/generator"), Text(code), context, new[] { (byte)i })) % p;
            var rhs = (BigInteger.ModPow(x, 3, p) + a * x + b) % p;
            if (rhs.IsZero || BigInteger.ModPow(rhs, (p - 1) / 2, p) != BigInteger.One) continue; // no point has this x
            var y = BigInteger.ModPow(rhs, (p + 1) / 4, p); // P-256's prime is 3 mod 4
            if (!y.IsEven) y = p - y;
            curve.G = new ECPoint { X = Bytes(x), Y = Bytes(y) };
            curve.Seed = null; // the seed made the standard curve; with another generator it no longer describes this one
            curve.Hash = null;
            return curve;
        }
        throw new CryptographicException("No point was found for the pairing code.");
    }

    private static BigInteger Number(byte[] bigEndian) => new(bigEndian, isUnsigned: true, isBigEndian: true);

    private static byte[] Bytes(BigInteger value) => Fixed(value.ToByteArray(isUnsigned: true, isBigEndian: true));

    // A coordinate as exactly 32 bytes (a small value comes shorter).
    private static byte[] Fixed(byte[] value)
    {
        const int size = PointBytes / 2;
        if (value.Length == size) return value;
        var padded = new byte[size];
        value.CopyTo(padded, size - value.Length);
        return padded;
    }
}

/// One direction of an encrypted link: AES-256-GCM with a counter as the nonce (never reused, since each direction has its own key and the
/// counter only goes up). The receiving side demands the counter it expects next, so a copied or reordered message fails to open.
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

    /// The message sealed: ciphertext then tag.
    public byte[] Seal(byte[] plain)
    {
        var sealedBytes = new byte[plain.Length + TagBytes];
        _aes.Encrypt(NextNonce(), plain, sealedBytes.AsSpan(0, plain.Length), sealedBytes.AsSpan(plain.Length));
        return sealedBytes;
    }

    /// The message opened, or null when it was changed, replayed or is not from the key's owner.
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
