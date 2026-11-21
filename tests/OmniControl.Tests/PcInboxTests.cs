using OmniControl.App.Features.OtherPcs;

namespace OmniControl.Tests;

/// What a paired PC sends waits in a bounded inbox before the UI thread: newer catalogs and state values replace waiting ones, one post at a
/// time, and a full inbox holds the reader back. Headless: the "UI thread" is a list of posted drains run by hand.
public class PcInboxTests
{
    private sealed class Rig
    {
        public readonly List<Action> Posted = new();
        public readonly List<PcMsg> Handled = new();
        public readonly PcInbox Inbox;

        public Rig(int max = 16) => Inbox = new PcInbox(max, Handled.Add, Posted.Add);

        public void RunPosted()
        {
            var posted = Posted.ToList();
            Posted.Clear();
            foreach (var drain in posted) drain();
        }
    }

    private static PcMsg State(string id, double value) => new() { T = PcMsgType.State, Id = id, Number = value };

    [Fact]
    public async Task NewerCatalogAndStateValues_ReplaceWaitingOnes_InOnePost()
    {
        var rig = new Rig();

        await rig.Inbox.PutAsync(new PcMsg { T = PcMsgType.Catalog, Text = "old" }, default);
        await rig.Inbox.PutAsync(State("volume", 10), default);
        await rig.Inbox.PutAsync(State("brightness", 50), default);
        await rig.Inbox.PutAsync(new PcMsg { T = PcMsgType.Catalog, Text = "new" }, default);
        await rig.Inbox.PutAsync(State("volume", 20), default);
        Assert.Single(rig.Posted);
        rig.RunPosted();

        Assert.Equal(new[] { "brightness", null, "volume" }, rig.Handled.Select(m => m.Id));
        Assert.Equal("new", rig.Handled[1].Text);
        Assert.Equal(20, rig.Handled[2].Number);
    }

    [Fact]
    public async Task OtherMessages_AreKeptInOrder()
    {
        var rig = new Rig();

        for (var i = 0; i < 3; i++) await rig.Inbox.PutAsync(new PcMsg { T = PcMsgType.Run, Id = "a", Rid = i }, default);
        rig.RunPosted();

        Assert.Equal(new int?[] { 0, 1, 2 }, rig.Handled.Select(m => m.Rid));
    }

    [Fact]
    public async Task FullInbox_HoldsTheReader_UntilDrained()
    {
        var rig = new Rig(max: 2);
        await rig.Inbox.PutAsync(new PcMsg { T = PcMsgType.Run, Rid = 1 }, default);
        await rig.Inbox.PutAsync(new PcMsg { T = PcMsgType.Run, Rid = 2 }, default);

        var third = rig.Inbox.PutAsync(new PcMsg { T = PcMsgType.Run, Rid = 3 }, default);
        await Task.Delay(50);
        Assert.False(third.IsCompleted);

        rig.RunPosted();
        await third.WaitAsync(TimeSpan.FromSeconds(5));
        rig.RunPosted();

        Assert.Equal(new int?[] { 1, 2, 3 }, rig.Handled.Select(m => m.Rid));
    }

    [Fact]
    public async Task FullInbox_StillTakesANewerValueOfAWaitingState()
    {
        var rig = new Rig(max: 1);
        await rig.Inbox.PutAsync(State("volume", 1), default);

        await rig.Inbox.PutAsync(State("volume", 2), default).WaitAsync(TimeSpan.FromSeconds(5));
        rig.RunPosted();

        Assert.Equal(2, Assert.Single(rig.Handled).Number);
    }

    [Fact]
    public async Task ClosedLink_EndsTheWait()
    {
        var rig = new Rig(max: 1);
        await rig.Inbox.PutAsync(new PcMsg { T = PcMsgType.Run }, default);
        using var stop = new CancellationTokenSource();

        var waiting = rig.Inbox.PutAsync(new PcMsg { T = PcMsgType.Run }, stop.Token);
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
