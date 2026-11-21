using System.Threading.Channels;
using OmniControl.App.Features.OtherPcs;

namespace OmniControl.Tests;

/// The pairing code check (CPace on Windows' own ECDH), the pairing exchange, the session proof with its fresh keys and the one-at-a-time
/// pairing attempt, headless: the two PCs talk over an in-memory pipe, no socket, no credential store, no window.
public class PcPairingTests
{
    private const string InitiatorId = "0123456789abcdef0123456789abcdef", ResponderId = "fedcba9876543210fedcba9876543210";

    /// One end of an in-memory two-way connection: what one end writes the other reads; disposing an end ends the other's reading.
    private sealed class PipeEnd(Channel<byte[]> input, Channel<byte[]> output) : Stream
    {
        private ReadOnlyMemory<byte> _left;

        public static (PipeEnd A, PipeEnd B) Pair()
        {
            var ab = Channel.CreateUnbounded<byte[]>();
            var ba = Channel.CreateUnbounded<byte[]>();
            return (new PipeEnd(ba, ab), new PipeEnd(ab, ba));
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_left.IsEmpty)
            {
                try
                {
                    _left = await input.Reader.ReadAsync(ct);
                }
                catch (ChannelClosedException)
                {
                    return 0;
                }
            }
            var n = Math.Min(buffer.Length, _left.Length);
            _left[..n].CopyTo(buffer);
            _left = _left[n..];
            return n;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            output.Writer.TryWrite(buffer.ToArray());
            return default;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Write(byte[] buffer, int offset, int count) => output.Writer.TryWrite(buffer.AsSpan(offset, count).ToArray());
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            output.Writer.TryComplete();
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------- the code check

    [Fact]
    public void SameCode_SameContext_KeysAgree()
    {
        var context = PcCrypto.Random(32);
        var (one, two) = (PcCrypto.CodeCurve("123456", context), PcCrypto.CodeCurve("123456", context));
        var a = PcCrypto.NewKey(one);
        var b = PcCrypto.NewKey(two);
        using var keyA = a.Key;
        using var keyB = b.Key;

        var ab = PcCrypto.Agree(a.Key, b.Point, one);
        var ba = PcCrypto.Agree(b.Key, a.Point, two);

        Assert.NotNull(ab);
        Assert.Equal(ab, ba);
    }

    [Theory]
    [InlineData("123457", false)] // another code
    [InlineData("123456", true)] // the same code in another exchange
    public void OtherCodeOrExchange_KeysDisagree(string otherCode, bool otherContext)
    {
        var context = PcCrypto.Random(32);
        var mine = PcCrypto.CodeCurve("123456", context);
        var theirs = PcCrypto.CodeCurve(otherCode, otherContext ? PcCrypto.Random(32) : context);
        var a = PcCrypto.NewKey(mine);
        var b = PcCrypto.NewKey(theirs);
        using var keyA = a.Key;
        using var keyB = b.Key;

        var ab = PcCrypto.Agree(a.Key, b.Point, mine);
        var ba = PcCrypto.Agree(b.Key, a.Point, theirs);

        Assert.NotEqual(ab, ba);
    }

    [Fact]
    public void PointNotOnTheCurve_IsRefused()
    {
        var curve = PcCrypto.CodeCurve("123456", PcCrypto.Random(32));
        var own = PcCrypto.NewKey(curve);
        var other = PcCrypto.NewKey(curve);
        var plain = PcCrypto.NewKey();
        var plainOther = PcCrypto.NewKey();
        using var ownKey = own.Key;
        using var otherKey = other.Key;
        using var plainKey = plain.Key;
        using var plainOtherKey = plainOther.Key;
        var bent = (byte[])other.Point.Clone();
        bent[^1] ^= 1;
        var bentPlain = (byte[])plainOther.Point.Clone();
        bentPlain[^1] ^= 1;

        Assert.Null(PcCrypto.Agree(own.Key, bent, curve));
        Assert.Null(PcCrypto.Agree(plain.Key, bentPlain));
        Assert.Null(PcCrypto.Agree(plain.Key, plainOther.Point[..10]));
    }

    [Fact]
    public void NewCode_IsSixDigits()
    {
        for (var i = 0; i < 50; i++) Assert.Matches("^[0-9]{6}$", PcCrypto.NewCode());
    }

    // ---------------------------------------------------------------- the pairing exchange

    private sealed record Ends(PairResult Initiator, PairResult Responder, string Shown);

    // The responder's side reads the connection's first byte itself, as the listener does before it hands the stream over.
    private static async Task<Ends> PairAsync(Func<string, string> typed, bool confirm = true)
    {
        var pipes = PipeEnd.Pair();
        using var a = pipes.A;
        using var b = pipes.B;
        var shown = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var responder = Task.Run(async () =>
        {
            Assert.Equal(PcProtocol.WantsPairing, (await PcWire.ReadExactAsync(b, 1, default))![0]);
            return await PcPairing.RespondAsync(b, ResponderId, "Desk", string.Empty, _ => false, (_, code) => shown.TrySetResult(code), (_, _) => Task.FromResult(confirm), default);
        });
        var initiator = await PcPairing.InitiateAsync(a, InitiatorId, "Laptop", string.Empty, _ => false, async _ => typed(await shown.Task), default);
        return new Ends(initiator, await responder.WaitAsync(TimeSpan.FromSeconds(20)), await shown.Task);
    }

    [Fact]
    public async Task RightCode_BothPaired_SameSecret()
    {
        var ends = await PairAsync(code => $"{code[..3]} {code[3..]}"); // typed with the space the code is shown with

        Assert.True(ends.Initiator.Paired, ends.Initiator.Problem);
        Assert.True(ends.Responder.Paired, ends.Responder.Problem);
        Assert.Equal(ends.Initiator.Secret, ends.Responder.Secret);
        Assert.Equal(ResponderId, ends.Initiator.PeerId);
        Assert.Equal(InitiatorId, ends.Responder.PeerId);
    }

    [Fact]
    public async Task WrongCode_NothingPaired_ResponderCountsIt()
    {
        var ends = await PairAsync(code => code == "000000" ? "000001" : "000000");

        Assert.False(ends.Initiator.Paired);
        Assert.False(ends.Responder.Paired);
        Assert.True(ends.Responder.WrongCode);
        Assert.Contains("did not match", ends.Initiator.Problem);
    }

    [Fact]
    public async Task ResponderSaysNo_NothingPaired()
    {
        var ends = await PairAsync(code => code, confirm: false);

        Assert.False(ends.Initiator.Paired);
        Assert.False(ends.Responder.Paired);
        Assert.False(ends.Responder.WrongCode);
        Assert.Contains("cancelled on Desk", ends.Initiator.Problem);
    }

    [Fact]
    public async Task EveryExchange_ShowsItsOwnCode()
    {
        var first = await PairAsync(code => code);
        var second = await PairAsync(code => code);

        Assert.NotEqual(first.Initiator.Secret, second.Initiator.Secret);
    }

    [Fact]
    public async Task OlderVersion_IsRefusedWithOneSentence()
    {
        var pipes = PipeEnd.Pair();
        using var a = pipes.A;
        using var b = pipes.B;
        await PcWire.WriteJsonAsync(a, new PairMsg { V = 2, Id = InitiatorId, Name = "Old", Nonce = PcCrypto.Random(PcProtocol.KeyBytes) }, default);

        var result = await PcPairing.RespondAsync(b, ResponderId, "Desk", string.Empty, _ => false, (_, _) => Assert.Fail("no code for an older PC"), (_, _) => Task.FromResult(true), default);

        Assert.False(result.Paired);
        Assert.Contains("another version", result.Problem);
        Assert.True((await PcWire.ReadJsonAsync<PairMsg>(a, default))!.Abort);
    }

    [Fact]
    public async Task NotAPairing_NothingIsSaid()
    {
        var pipes = PipeEnd.Pair();
        var a = pipes.A;
        using var b = pipes.B;
        a.Dispose(); // the other end goes away without a word

        var result = await PcPairing.RespondAsync(b, ResponderId, "Desk", string.Empty, _ => false, (_, _) => Assert.Fail("no code"), (_, _) => Task.FromResult(true), default);

        Assert.False(result.Paired);
        Assert.Null(result.Problem);
    }

    // ---------------------------------------------------------------- the session proof

    private static async Task<(PcLink? Dialled, PcLink? Accepted)> SessionAsync(byte[] diallerSecret, byte[] listenerSecret, byte dialKind = PcProtocol.WantsSession, byte acceptKind = PcProtocol.WantsSession)
    {
        var (a, b) = PipeEnd.Pair();
        var accept = Task.Run(async () =>
        {
            Assert.Equal(dialKind, (await PcWire.ReadExactAsync(b, 1, default))![0]);
            return await PcSession.AcceptAsync(b, acceptKind, ResponderId, id => id == InitiatorId ? listenerSecret : null, default);
        });
        var dialled = await PcSession.DialAsync(a, InitiatorId, ResponderId, diallerSecret, default, dialKind);
        if (dialled is null) a.Dispose(); // as the caller does: the listener's wait for the proof ends at once
        return (dialled, await accept.WaitAsync(TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public async Task Session_SameKey_CarriesMessagesBothWays()
    {
        var secret = PcCrypto.Random(PcProtocol.KeyBytes);
        var (dialled, accepted) = await SessionAsync(secret, secret);
        using var dialledLink = dialled;
        using var acceptedLink = accepted;
        Assert.NotNull(dialled);
        Assert.NotNull(accepted);
        var heard = new TaskCompletionSource<PcMsg>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = accepted!.RunAsync((message, _) =>
        {
            heard.TrySetResult(message);
            return Task.CompletedTask;
        });

        Assert.True(await dialled!.SendAsync(new PcMsg { T = PcMsgType.Ping, Text = "hello" }));

        Assert.Equal("hello", (await heard.Task.WaitAsync(TimeSpan.FromSeconds(10))).Text);
    }

    [Fact]
    public async Task Session_OtherKey_NoLinkEitherSide()
    {
        var (dialled, accepted) = await SessionAsync(PcCrypto.Random(PcProtocol.KeyBytes), PcCrypto.Random(PcProtocol.KeyBytes));

        Assert.Null(dialled);
        Assert.Null(accepted);
    }

    [Fact]
    public async Task Probe_CannotPassAsSession()
    {
        var secret = PcCrypto.Random(PcProtocol.KeyBytes);
        var (dialled, accepted) = await SessionAsync(secret, secret, PcProtocol.WantsProbe, PcProtocol.WantsSession);

        Assert.Null(dialled);
        Assert.Null(accepted);
    }

    // ---------------------------------------------------------------- one pairing at a time

    [Fact]
    public void Attempts_OneAtATime_GivenUpIsDropped()
    {
        using var outer = new CancellationTokenSource();
        var attempts = new PairAttempts(outer.Token);

        var first = attempts.TryBegin()!.Value;
        Assert.Null(attempts.TryBegin());
        Assert.True(attempts.Counts(first.Id));
        attempts.GiveUp();
        Assert.True(first.Stop.IsCancellationRequested);
        Assert.False(attempts.Counts(first.Id));
        Assert.False(attempts.End(first.Id));

        var second = attempts.TryBegin()!.Value;
        Assert.False(attempts.End(first.Id)); // an older attempt's late end changes nothing
        Assert.True(attempts.Counts(second.Id));
        Assert.True(attempts.End(second.Id));
        Assert.NotNull(attempts.TryBegin());
    }

    [Fact]
    public void Attempts_StopWithTheService()
    {
        using var outer = new CancellationTokenSource();
        var attempts = new PairAttempts(outer.Token);
        var attempt = attempts.TryBegin()!.Value;

        outer.Cancel();

        Assert.True(attempt.Stop.IsCancellationRequested);
    }
}
