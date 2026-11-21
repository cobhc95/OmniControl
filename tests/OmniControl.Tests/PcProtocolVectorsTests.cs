using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using OmniControl.App.Features.OtherPcs;

namespace OmniControl.Tests;

/// Known-answer vectors for the pairing (CPace on P-256, protocol 3) and the session keys: fixed inputs (ids, names, nonces, code, private
/// keys) give fixed outputs, so a change to any step fails here. Every value is checked twice: against the hex the implementation gave when
/// the vectors were made, and against a re-derivation from the protocol's steps written here with only SHA-256 and BigInteger (its own
/// HMAC, HKDF, curve constants and point arithmetic, none of the implementation's helpers or .NET's HMAC, HKDF or ECDH).
public class PcProtocolVectorsTests
{
    private const string InitiatorId = "0123456789abcdef0123456789abcdef", ResponderId = "fedcba9876543210fedcba9876543210";

    private static byte[] Run(byte from) => Enumerable.Range(from, 32).Select(i => (byte)i).ToArray();

    // ---------------------------------------------------------------- the pairing

    private const string Code = "123456";
    private static readonly byte[] InitiatorNonce = Run(0x00), ResponderNonce = Run(0x20);
    private static readonly BigInteger InitiatorKey = Spec.Hex("1111111111111111111111111111111111111111111111111111111111111111");
    private static readonly BigInteger ResponderKey = Spec.Hex("2222222222222222222222222222222222222222222222222222222222222222");

    private const string GeneratorX = "3188b0889525ce05b423efedcade2deb401d016b5c1f18753e118aa152a1c345";
    private const string GeneratorY = "4b08e7b23bd276a6a476bec1d919dea02b768b5d686974211c7478a943a15704";
    private const string InitiatorPoint = "c22d848ddd56f1ecdbfc41f86f5bf3491618d8f9ca3aab72766fee07b072d8cd48a84249e968277007982e5913618e0052017624da9734486b3990e761c5aa2d";
    private const string ResponderPoint = "c97bef2bc1e748af36ee63e43ccef6ff8af9a502fd739b9cddc317cec181f420efd77c48615ff7670f40ba56d6c1323be6d4c8a228443564f6b85d6567269d13";
    private const string PairShared = "a4b322ce9656524c1bdabdbc6ad05e76b7e4cf68d18bc52749a81ba2e8cbde31";
    private const string PairTranscript = "82053f7b32f35d4a20150e3ac90f5e64902eaf0fbf06e8ae00cfdca708b0afe6";
    private const string PairSecret = "02720ece6467b3f3469e5a95d8b216685672008fea551cb5c26806decd702341";
    private const string PairConfirm = "762021da22719016c53ccd8ac2d90a91122b6355546074f82310d15d4772266e";
    private const string InitiatorTag = "7791fb493cd1f5710d5d071b6c1e145d77dabc11b9aef4f238f9d8d15ef04d38";
    private const string ResponderTag = "50c5ebf7e4f7f1d12407ce0d766aae3d7fb962d95f1cace5e6349692c06fbeee";
    private const string ConfirmedTag = "582e1e48bbf418739f2aea04cb209c9a989203f3a1420a0b5f64eb487176ef2b";

    [Fact]
    public void Pairing_FixedInputs_GiveTheVectors()
    {
        var exchange = new PcPairing.Exchange(InitiatorId, ResponderId, "Laptop", "Desk", string.Empty, string.Empty, InitiatorNonce, ResponderNonce);
        var curve = exchange.Curve("123 456"); // typed with the space the code is shown with
        using var initiator = Import(curve, InitiatorKey);
        using var responder = Import(curve, ResponderKey);
        var initiatorPoint = PointOf(initiator);
        var responderPoint = PointOf(responder);
        var shared = PcCrypto.Agree(initiator, responderPoint, curve)!;
        var keys = exchange.KeysFrom(shared, initiatorPoint, responderPoint);

        var context = Spec.Hash(Spec.Text("omnicontrol/pair/3"), Spec.Text(InitiatorId), Spec.Text(ResponderId), Spec.Text("Laptop"), Spec.Text("Desk"),
            Spec.Text(string.Empty), Spec.Text(string.Empty), InitiatorNonce, ResponderNonce);
        var g = Spec.CodeGenerator(Code, context);
        var specInitiator = Spec.Times(InitiatorKey, g);
        var specResponder = Spec.Times(ResponderKey, g);
        var specShared = Spec.Bytes(Spec.Times(InitiatorKey, specResponder).X);
        var transcript = Spec.Hash(context, Spec.Bytes(specInitiator), Spec.Bytes(specResponder));
        var confirm = Spec.Hkdf(specShared, transcript, "omnicontrol/pair/confirm");

        var wrong = new List<string>();
        Check(wrong, "generator x", GeneratorX, curve.G.X!, Spec.Bytes(g.X));
        Check(wrong, "generator y", GeneratorY, curve.G.Y!, Spec.Bytes(g.Y));
        Check(wrong, "initiator point", InitiatorPoint, initiatorPoint, Spec.Bytes(specInitiator));
        Check(wrong, "responder point", ResponderPoint, responderPoint, Spec.Bytes(specResponder));
        Check(wrong, "shared", PairShared, shared, specShared);
        Check(wrong, "shared, responder's side", PairShared, PcCrypto.Agree(responder, initiatorPoint, curve)!, specShared);
        Check(wrong, "transcript", PairTranscript, keys.Transcript, transcript);
        Check(wrong, "secret", PairSecret, keys.Secret, Spec.Hkdf(specShared, transcript, "omnicontrol/pair/secret"));
        Check(wrong, "confirm key", PairConfirm, keys.Confirm, confirm);
        Check(wrong, "initiator tag", InitiatorTag, keys.Tag("initiator"), Spec.Hmac(confirm, Spec.Hash(Spec.Text("initiator"), transcript)));
        Check(wrong, "responder tag", ResponderTag, keys.Tag("responder"), Spec.Hmac(confirm, Spec.Hash(Spec.Text("responder"), transcript)));
        Check(wrong, "confirmed tag", ConfirmedTag, keys.Tag("confirmed"), Spec.Hmac(confirm, Spec.Hash(Spec.Text("confirmed"), transcript)));
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    // ---------------------------------------------------------------- the session

    private static readonly byte[] PairingKey = Run(0x40), DiallerNonce = Run(0x60), ListenerNonce = Run(0x80);
    private static readonly BigInteger DiallerKey = Spec.Hex("3333333333333333333333333333333333333333333333333333333333333333");
    private static readonly BigInteger ListenerKey = Spec.Hex("4444444444444444444444444444444444444444444444444444444444444444");

    private const string DiallerPoint = "51a7580833898ea1b183cbd7350a4099078c6ef1c1e18e970cd7683035f25e7d0110522712b0b5a7cff081685486984a94e6831edac46e7360fa9d834a7a81a1";
    private const string ListenerPoint = "5b36890dacbd7c9a96bb74a1ee28b3d2d75b72e09a20ef25cf8e6fd8a9f0350d0e14bed8d4682a34d83538bdff5b96e89a6666ec0db5745d02fa1210072df75a";
    private const string SessionShared = "5238f9f956812e75918895390fa057a2063aa66d4d5eefbe6ecdcf78342d2922";
    private const string SessionSaid = "5171237929519882b4f04b256ffe79fe960128e5f7e8b181434d02d670769884";
    private const string ListenerProof = "20c2cb136993b5fce0ba2b00779a43422be95cbbb8db8be76024aa8ae30722f4";
    private const string DiallerProof = "5ecc456504ca9cc0ee25d99733622ac867c5243e18c3877e0fb429f68dec9bb9";
    private const string ToListenerKey = "51dc1bbe44b802c1c9ac7c98c570bb63998553754aeeff6932a033ff553e8350";
    private const string ToDiallerKey = "45817fc8008f2a7b93ed450c886796b25c4fc97a6e659e6b1de1d2f6b7d7c561";

    [Fact]
    public void Session_FixedInputs_GiveTheVectors()
    {
        var curve = ECCurve.NamedCurves.nistP256;
        using var dialler = Import(curve, DiallerKey);
        using var listener = Import(curve, ListenerKey);
        var diallerPoint = PointOf(dialler);
        var listenerPoint = PointOf(listener);
        var shared = PcCrypto.Agree(dialler, listenerPoint)!;
        var said = PcSession.Said(PcProtocol.WantsSession, DiallerNonce, ListenerNonce, diallerPoint, listenerPoint, InitiatorId, ResponderId);
        var (toListener, toDialler) = PcSession.DirectionKeys(shared, PairingKey, said);

        var specDialler = Spec.Times(DiallerKey, Spec.G);
        var specListener = Spec.Times(ListenerKey, Spec.G);
        var specShared = Spec.Bytes(Spec.Times(DiallerKey, specListener).X);
        var specSaid = Spec.Hash(Spec.Text("omnicontrol/session/3"), new[] { PcProtocol.WantsSession }, DiallerNonce, ListenerNonce,
            Spec.Bytes(specDialler), Spec.Bytes(specListener), Spec.Text(InitiatorId), Spec.Text(ResponderId));
        var both = specShared.Concat(PairingKey).ToArray();

        var wrong = new List<string>();
        Check(wrong, "dialler point", DiallerPoint, diallerPoint, Spec.Bytes(specDialler));
        Check(wrong, "listener point", ListenerPoint, listenerPoint, Spec.Bytes(specListener));
        Check(wrong, "shared", SessionShared, shared, specShared);
        Check(wrong, "shared, listener's side", SessionShared, PcCrypto.Agree(listener, diallerPoint)!, specShared);
        Check(wrong, "said", SessionSaid, said, specSaid);
        Check(wrong, "listener proof", ListenerProof, PcCrypto.Mac(PairingKey, PcCrypto.Text("listener"), said), Spec.Hmac(PairingKey, Spec.Hash(Spec.Text("listener"), specSaid)));
        Check(wrong, "dialler proof", DiallerProof, PcCrypto.Mac(PairingKey, PcCrypto.Text("dialler"), said), Spec.Hmac(PairingKey, Spec.Hash(Spec.Text("dialler"), specSaid)));
        Check(wrong, "dialler-to-listener key", ToListenerKey, toListener, Spec.Hkdf(both, specSaid, "omnicontrol/session/dialler-to-listener"));
        Check(wrong, "listener-to-dialler key", ToDiallerKey, toDialler, Spec.Hkdf(both, specSaid, "omnicontrol/session/listener-to-dialler"));
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    // ---------------------------------------------------------------- the re-derivation's own pieces

    [Fact]
    public void Spec_MatchesPublishedVectors()
    {
        // RFC 4231 test case 2, RFC 5869 test case 1.
        Assert.Equal("5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843", Hex(Spec.Hmac(Encoding.ASCII.GetBytes("Jefe"), Encoding.ASCII.GetBytes("what do ya want for nothing?"))));
        var okm = Spec.Hkdf(Enumerable.Repeat((byte)0x0b, 22).ToArray(), Enumerable.Range(0, 13).Select(i => (byte)i).ToArray(),
            Enumerable.Range(0xf0, 10).Select(i => (byte)i).ToArray(), 42);
        Assert.Equal("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865", Hex(okm));
        // (n-1)G = -G: the constants and the arithmetic describe P-256's group.
        var minus = Spec.Times(Spec.N - 1, Spec.G);
        Assert.Equal((Spec.G.X, Spec.P - Spec.G.Y), (minus.X, minus.Y));
    }

    // ---------------------------------------------------------------- helpers

    private static void Check(List<string> wrong, string name, string expected, byte[] implementation, byte[] spec)
    {
        if (Hex(implementation) != Hex(spec)) wrong.Add($"{name}: implementation {Hex(implementation)}, steps {Hex(spec)}");
        if (Hex(implementation) != expected) wrong.Add($"{name}: expected {expected}, got {Hex(implementation)}");
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    // The implementation's key from a fixed private scalar; Windows works out the public point.
    private static ECDiffieHellman Import(ECCurve curve, BigInteger d) => ECDiffieHellman.Create(new ECParameters { Curve = curve, D = Spec.Bytes(d) });

    // The public point as the implementation sends it (PcCrypto.NewKey): X then Y, 32 bytes each.
    private static byte[] PointOf(ECDiffieHellman key)
    {
        var q = key.ExportParameters(false).Q;
        return Spec.Bytes((Spec.Number(q.X!), Spec.Number(q.Y!)));
    }

    /// The protocol's steps written out again from their definitions: length-prefixed SHA-256, HMAC-SHA256 (RFC 2104), HKDF-SHA256
    /// (RFC 5869), P-256 (SEC 2) in affine coordinates, and the code's generator by try and increment.
    private static class Spec
    {
        public static readonly BigInteger P = Hex("ffffffff00000001000000000000000000000000ffffffffffffffffffffffff");
        public static readonly BigInteger A = P - 3;
        public static readonly BigInteger B = Hex("5ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b");
        public static readonly BigInteger N = Hex("ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551");
        public static readonly (BigInteger X, BigInteger Y) G =
            (Hex("6b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296"), Hex("4fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5"));

        public static BigInteger Hex(string hex) => BigInteger.Parse("0" + hex, NumberStyles.HexNumber);

        public static BigInteger Number(byte[] bigEndian) => new(bigEndian, isUnsigned: true, isBigEndian: true);

        public static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

        /// 32 bytes, big-endian.
        public static byte[] Bytes(BigInteger value)
        {
            var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            return new byte[32 - raw.Length].Concat(raw).ToArray();
        }

        public static byte[] Bytes((BigInteger X, BigInteger Y) point) => Bytes(point.X).Concat(Bytes(point.Y)).ToArray();

        /// SHA-256 over each part as its length (4 bytes, little-endian) then its bytes.
        public static byte[] Hash(params byte[][] parts)
        {
            var all = new List<byte>();
            foreach (var part in parts)
            {
                var length = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(length, part.Length);
                all.AddRange(length);
                all.AddRange(part);
            }
            return SHA256.HashData(all.ToArray());
        }

        public static byte[] Hmac(byte[] key, byte[] message)
        {
            var block = new byte[64];
            (key.Length > 64 ? SHA256.HashData(key) : key).CopyTo(block, 0);
            var inner = SHA256.HashData(block.Select(b => (byte)(b ^ 0x36)).Concat(message).ToArray());
            return SHA256.HashData(block.Select(b => (byte)(b ^ 0x5c)).Concat(inner).ToArray());
        }

        public static byte[] Hkdf(byte[] secret, byte[] salt, string purpose) => Hkdf(secret, salt, Encoding.ASCII.GetBytes(purpose), 32);

        public static byte[] Hkdf(byte[] secret, byte[] salt, byte[] info, int length)
        {
            var prk = Hmac(salt, secret);
            var okm = new List<byte>();
            var t = Array.Empty<byte>();
            for (byte i = 1; okm.Count < length; i++)
            {
                t = Hmac(prk, t.Concat(info).Append(i).ToArray());
                okm.AddRange(t);
            }
            return okm.Take(length).ToArray();
        }

        /// The code's generator: x from the hash of "omnicontrol/pair/generator", the code, the context and a counter byte (0, 1, ...), taken
        /// mod p; the first x with a point on the curve gives it, its even y.
        public static (BigInteger X, BigInteger Y) CodeGenerator(string code, byte[] context)
        {
            for (var i = 0; i < 256; i++)
            {
                var x = Number(Hash(Text("omnicontrol/pair/generator"), Text(code), context, new[] { (byte)i })) % P;
                var rhs = Mod(x * x * x + A * x + B);
                if (rhs.IsZero || BigInteger.ModPow(rhs, (P - 1) / 2, P) != BigInteger.One) continue;
                var y = BigInteger.ModPow(rhs, (P + 1) / 4, P);
                Assert.Equal(rhs, Mod(y * y));
                return (x, y.IsEven ? y : P - y);
            }
            throw new InvalidOperationException("No point for the code.");
        }

        public static (BigInteger X, BigInteger Y) Times(BigInteger k, (BigInteger X, BigInteger Y) point)
        {
            (BigInteger X, BigInteger Y)? sum = null, addend = point;
            for (; k > 0; k >>= 1)
            {
                if (!k.IsEven) sum = Add(sum, addend);
                addend = Add(addend, addend);
            }
            return sum!.Value;
        }

        // null = the point at infinity.
        private static (BigInteger X, BigInteger Y)? Add((BigInteger X, BigInteger Y)? first, (BigInteger X, BigInteger Y)? second)
        {
            if (first is not { } a) return second;
            if (second is not { } b) return first;
            BigInteger slope;
            if (a.X == b.X)
            {
                if (Mod(a.Y + b.Y).IsZero) return null;
                slope = Mod((3 * a.X * a.X + A) * Inverse(2 * a.Y));
            }
            else slope = Mod((b.Y - a.Y) * Inverse(b.X - a.X));
            var x = Mod(slope * slope - a.X - b.X);
            return (x, Mod(slope * (a.X - x) - a.Y));
        }

        private static BigInteger Mod(BigInteger value)
        {
            var m = value % P;
            return m.Sign < 0 ? m + P : m;
        }

        private static BigInteger Inverse(BigInteger value) => BigInteger.ModPow(Mod(value), P - 2, P);
    }
}
