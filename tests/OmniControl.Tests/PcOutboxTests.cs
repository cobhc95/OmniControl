using System.Text.Json;
using OmniControl.App.Features.OtherPcs;

namespace OmniControl.Tests;

/// What a link sends waits in a bounded outbox for its one writer: newer catalogs and state values replace waiting ones, commands keep their
/// order and are never dropped, and a full outbox or a failed write stalls the link (every waiting message fails, the owner closes it once).
/// The "link" is a fake writer that can be held.
public class PcOutboxTests
{
    private sealed class Rig
    {
        public readonly List<PcMsg> Written = new();
        public TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Stalls;
        public bool Fail;
        public readonly PcOutbox Outbox;

        public Rig(int max = 16, int maxBytes = 1 << 20, bool held = false)
        {
            if (!held) Gate.SetResult();
            Outbox = new PcOutbox(max, maxBytes, WriteAsync, () => Interlocked.Increment(ref Stalls));
        }

        public readonly TaskCompletionSource Writing = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// Puts a first command and waits until the writer holds it.
        public async Task<Task<bool>> PutHeldAsync()
        {
            var sent = Outbox.Put(new PcMsg { T = PcMsgType.Run, Rid = -1 });
            await Writing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return sent;
        }

        private async Task WriteAsync(byte[] bytes)
        {
            Writing.TrySetResult();
            await Gate.Task;
            if (Fail) throw new IOException("stalled");
            lock (Written) Written.Add(JsonSerializer.Deserialize<PcMsg>(bytes, PcProtocol.Json)!);
        }
    }

    private static PcMsg State(string id, double value) => new() { T = PcMsgType.State, Id = id, Number = value };

    [Fact]
    public async Task Commands_AreWrittenInOrder()
    {
        var rig = new Rig();

        var sent = Enumerable.Range(0, 5).Select(i => rig.Outbox.Put(new PcMsg { T = PcMsgType.Run, Rid = i })).ToList();

        Assert.All(await Task.WhenAll(sent).WaitAsync(TimeSpan.FromSeconds(5)), ok => Assert.True(ok));
        Assert.Equal(new int?[] { 0, 1, 2, 3, 4 }, rig.Written.Select(m => m.Rid));
    }

    [Fact]
    public async Task WaitingStateAndCatalog_AreReplacedByNewerOnes()
    {
        var rig = new Rig(held: true);
        var first = await rig.PutHeldAsync();
        var oldVolume = rig.Outbox.Put(State("volume", 10));
        _ = rig.Outbox.Put(new PcMsg { T = PcMsgType.Catalog, Text = "old" });
        _ = rig.Outbox.Put(State("brightness", 50));
        var newVolume = rig.Outbox.Put(State("volume", 20));
        var last = rig.Outbox.Put(new PcMsg { T = PcMsgType.Catalog, Text = "new" });

        rig.Gate.SetResult();
        await Task.WhenAll(first, oldVolume, newVolume, last).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { PcMsgType.Run, PcMsgType.State, PcMsgType.State, PcMsgType.Catalog }, rig.Written.Select(m => m.T));
        Assert.Equal(new double?[] { null, 50, 20, null }, rig.Written.Select(m => m.Number));
        Assert.Equal("new", rig.Written[3].Text);
        Assert.True(await oldVolume);
    }

    [Fact]
    public async Task FullOutbox_FailsTheMessage_AndStallsTheLinkOnce()
    {
        var rig = new Rig(max: 2, held: true);
        var held = await rig.PutHeldAsync();
        var waiting = Enumerable.Range(0, 2).Select(i => rig.Outbox.Put(new PcMsg { T = PcMsgType.Run, Rid = i })).ToList();

        Assert.False(await rig.Outbox.Put(new PcMsg { T = PcMsgType.Run, Rid = 9 }));
        Assert.False(await rig.Outbox.Put(new PcMsg { T = PcMsgType.Run, Rid = 10 })); // closed now

        Assert.Equal(1, rig.Stalls);
        Assert.False(await waiting[0]);
        Assert.False(await waiting[1]);
        rig.Gate.SetResult();
        await held.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new int?[] { -1 }, rig.Written.Select(m => m.Rid)); // what was being written finishes; nothing after it
    }

    [Fact]
    public async Task ByteBudget_IsKept_ButOneLargeMessageFits()
    {
        var rig = new Rig(maxBytes: 200, held: true);
        var held = await rig.PutHeldAsync();
        var big = rig.Outbox.Put(new PcMsg { T = PcMsgType.ImportAnswer, Payload = new string('x', 1000) });
        var small = rig.Outbox.Put(new PcMsg { T = PcMsgType.Run, Rid = 1 });

        Assert.False(await small); // the big one alone fills the budget
        Assert.False(await big);
        Assert.Equal(1, rig.Stalls);
        rig.Gate.SetResult();
        Assert.True(await held.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task FailedWrite_FailsWhatWaits_AndStallsTheLink()
    {
        var rig = new Rig(held: true) { Fail = true };
        var first = rig.Outbox.Put(new PcMsg { T = PcMsgType.Run, Rid = 1 });
        var second = rig.Outbox.Put(new PcMsg { T = PcMsgType.Run, Rid = 2 });

        rig.Gate.SetResult();

        Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, rig.Stalls);
    }

    [Fact]
    public async Task Ask_DeadlineCoversTheWrite()
    {
        var requests = new PcRequests();
        var stream = new StalledStream();
        using var link = new PcLink("lap", stream, new byte[32], new byte[32], dialled: false);

        var answer = await requests.AskAsync(link, new PcMsg { T = PcMsgType.Devices }, TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(answer);
    }

    // A connection whose other end takes nothing: every write waits until it is cancelled.
    private sealed class StalledStream : MemoryStream
    {
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => await Task.Delay(Timeout.Infinite, ct);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => await Task.Delay(Timeout.Infinite, ct);
    }
}
