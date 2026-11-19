using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace OmniControl.Tests;

/// The shared search pipeline: the one match rule, the index, the runner (newest wins, cancel, flush), the row diff and the progressive fill.
public class SearchPipelineTests
{
    private sealed record Item(string Name, string Group);

    private static SearchIndex<Item> IndexOf(params Item[] items) => new(items, i => new[] { i.Name, i.Group });

    [Fact]
    public void EveryWordMustMatchSomeField_IgnoringCase_InListOrder()
    {
        var index = IndexOf(new Item("Brightness up", "Displays"), new Item("Volume up", "Volume"), new Item("Sleep", "Power"));

        Assert.Equal(new[] { "Brightness up", "Volume up" }, index.Match("UP").Select(i => i.Name));
        Assert.Equal(new[] { "Brightness up" }, index.Match("up displ").Select(i => i.Name));
        Assert.Empty(index.Match("up power"));
        Assert.Equal(3, index.Match("  ").Count);
    }

    [Fact]
    public void AWordNeverMatchesAcrossTwoFields()
    {
        var index = IndexOf(new Item("Volume", "Up"));

        Assert.Empty(index.Match("volumeup"));
        Assert.True(TextMatch.Any("vol up", "Volume", "Up"));
        Assert.False(TextMatch.Any("eu", "Volume", "Up"));
    }

    [Fact]
    public void ACancelledSearchStops()
    {
        var index = IndexOf(new Item("A", "B"));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => index.Match("a", cancel.Token));
    }

    /// Runs posted UI work on the test thread, as the dispatcher would.
    private sealed class Posts
    {
        private readonly BlockingCollection<Action> _queue = new();

        public void Post(Action action) => _queue.Add(action);

        /// Runs what arrives until <paramref name="done"/> holds or the time is up.
        public void RunUntil(Func<bool> done, TimeSpan within)
        {
            var end = DateTime.UtcNow + within;
            while (!done() && DateTime.UtcNow < end)
                if (_queue.TryTake(out var action, TimeSpan.FromMilliseconds(20))) action();
        }

        public void RunPending(TimeSpan within) => RunUntil(() => false, within);
    }

    [Fact]
    public void TheNewestQueryWins_AnOlderResultIsDropped()
    {
        var posts = new Posts();
        var applied = new List<string>();
        var runner = new SearchRunner<string>(applied.Add, post: posts.Post, debounce: TimeSpan.FromMilliseconds(10));
        using var gate = new ManualResetEventSlim();

        runner.Request(_ => { gate.Wait(TimeSpan.FromSeconds(5)); return "old"; });
        runner.Request(_ => "new");
        gate.Set();
        posts.RunUntil(() => applied.Count > 0, TimeSpan.FromSeconds(5));
        posts.RunPending(TimeSpan.FromMilliseconds(200)); // the old search's late result, if it posted one

        Assert.Equal(new[] { "new" }, applied);
        Assert.False(runner.IsPending);
    }

    [Fact]
    public void CancelDropsTheSearch()
    {
        var posts = new Posts();
        var applied = new List<string>();
        var runner = new SearchRunner<string>(applied.Add, post: posts.Post);

        runner.Request(_ => "dropped");
        runner.Cancel();
        posts.RunPending(TimeSpan.FromMilliseconds(200));

        Assert.Empty(applied);
        Assert.False(runner.IsPending);
    }

    [Fact]
    public void FlushShowsTheWaitingSearchAtOnce()
    {
        var posts = new Posts();
        var applied = new List<string>();
        var runner = new SearchRunner<string>(applied.Add, post: posts.Post, debounce: TimeSpan.FromSeconds(30));

        runner.Request(_ => "first");
        runner.Request(_ => "second"); // waits the (long) debounce
        runner.Flush();

        Assert.Equal("second", applied.Last());
        Assert.False(runner.IsPending);
        posts.RunPending(TimeSpan.FromMilliseconds(200));
        Assert.Equal("second", applied.Last());
    }

    private static List<NotifyCollectionChangedAction> Watch(ObservableCollection<Item> list)
    {
        var seen = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => seen.Add(e.Action);
        return seen;
    }

    [Fact]
    public void TheDiffReachesTheNewList_KeepsTheSameRows_AndNeverResets()
    {
        Item a = new("a", ""), b = new("b", ""), c = new("c", ""), d = new("d", ""), x = new("x", "");
        var list = new ObservableCollection<Item>(new[] { a, b, c, d });
        var seen = Watch(list);

        ListDiff.Apply(list, new[] { d, b, x }, i => i.Name);

        Assert.Equal(new[] { d, b, x }, list);
        Assert.Same(d, list[0]);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, seen);
    }

    [Fact]
    public void ARowWhoseContentChangedIsReplaced()
    {
        var list = new ObservableCollection<Item>(new[] { new Item("a", "old") });
        var changed = new Item("a", "new");

        ListDiff.Apply(list, new[] { changed }, i => i.Name, (shown, wanted) => shown == wanted);

        Assert.Same(changed, Assert.Single(list));
    }

    [Fact]
    public void AChangedRowIsRemovedAndInserted_NeverReplacedInPlace()
    {
        var list = new ObservableCollection<Item>(new[] { new Item("a", "old"), new Item("b", "") });
        var seen = Watch(list);

        ListDiff.Apply(list, new[] { new Item("a", "new"), list[1] }, i => i.Name, (shown, wanted) => shown == wanted);

        Assert.DoesNotContain(NotifyCollectionChangedAction.Replace, seen);
        Assert.Equal(2, list.Count);
        Assert.Equal("new", list[0].Group);
    }

    [Fact]
    public void ProgressiveFillShowsAScreenfulFirst_AndANewerFillStopsTheOlder()
    {
        var list = new ObservableCollection<Item>();
        var later = new Queue<Action>();
        var fill = new ProgressiveFill<Item>(list, i => i.Name, later.Enqueue);
        var many = Enumerable.Range(0, 50).Select(i => new Item($"i{i}", "")).ToList();

        fill.Show(many);
        Assert.Equal(ProgressiveFill<Item>.FirstScreen, list.Count);
        later.Dequeue()();
        Assert.Equal(ProgressiveFill<Item>.FirstScreen + ProgressiveFill<Item>.Batch, list.Count);

        fill.Show(many.Take(3).ToList());
        while (later.Count > 0) later.Dequeue()();
        Assert.Equal(many.Take(3), list);
    }
}
