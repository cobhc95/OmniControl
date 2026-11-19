using System.Collections.Concurrent;
using OmniControl.App.Features.Displays.Pages;

namespace OmniControl.Tests;

/// The hardware watches' retry after an announcement, with fake signals and a fake monitor source (nothing of Windows is touched): a source that
/// is ready only on the third look is found on the third try, and the schedule stops there.
public class HardwareRetryTests
{
    private static readonly TimeSpan[] FastSchedule = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(200)];
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    /// A monitor source that answers only from its <c>readyOn</c>-th refresh.
    private sealed class FakeMonitors(int readyOn)
    {
        private int _refreshes;
        public int Refreshes => Volatile.Read(ref _refreshes);
        public void Refresh() => Interlocked.Increment(ref _refreshes);
        public bool LooksStale() => Refreshes < readyOn;
    }

    private static (HardwareWatches Watches, ConcurrentQueue<string> Log) Make(FakeMonitors monitors)
    {
        var log = new ConcurrentQueue<string>();
        var watches = new HardwareWatches(FastSchedule, log.Enqueue);
        watches.Register(new HardwareWatch("displays.hardware", "Monitors", HardwareSignal.Display | HardwareSignal.Session | HardwareSignal.Resume,
            monitors.Refresh, monitors.LooksStale));
        return (watches, log);
    }

    private static bool WaitFor(ConcurrentQueue<string> log, string text)
    {
        var until = DateTime.UtcNow + Settle;
        while (DateTime.UtcNow < until)
        {
            if (log.Any(l => l.Contains(text))) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    [Fact]
    public void Session_signal_retries_until_the_monitors_answer_on_the_third_try()
    {
        var monitors = new FakeMonitors(readyOn: 3);
        var (watches, log) = Make(monitors);
        using (watches)
        {
            watches.Signal(HardwareSignal.Session, "fake console connect");
            Assert.True(WaitFor(log, "try 3 of 4 found it"));
            Thread.Sleep(FastSchedule[^1] * 2); // nothing more may run after it was found
            Assert.Equal(3, monitors.Refreshes);
            Assert.Contains(log, l => l.StartsWith("Signal Session (fake console connect): Monitors"));
            Assert.Contains(log, l => l.Contains("try 1 of 4 still looks stale"));
        }
    }

    [Fact]
    public void A_burst_from_both_listeners_runs_one_schedule()
    {
        var monitors = new FakeMonitors(readyOn: 3);
        var (watches, log) = Make(monitors);
        using (watches)
        {
            watches.Signal(HardwareSignal.Session, "WM_WTSSESSION_CHANGE 1");
            watches.Signal(HardwareSignal.Session, "SystemEvents session ConsoleConnect");
            watches.Signal(HardwareSignal.Display, "WM_DISPLAYCHANGE");
            Assert.True(WaitFor(log, "found it"));
            Thread.Sleep(FastSchedule[^1] * 2);
            Assert.Equal(3, monitors.Refreshes);
        }
    }

    [Fact]
    public void Gives_up_after_the_last_try_when_the_monitors_never_answer()
    {
        var monitors = new FakeMonitors(readyOn: int.MaxValue);
        var (watches, log) = Make(monitors);
        using (watches)
        {
            watches.Signal(HardwareSignal.Resume, "fake resume");
            Assert.True(WaitFor(log, "giving up"));
            Thread.Sleep(FastSchedule[^1] * 2);
            Assert.Equal(FastSchedule.Length, monitors.Refreshes);
        }
    }

    [Fact]
    public void A_refresh_made_meanwhile_ends_the_schedule_without_another_refresh()
    {
        var fixedByHand = false;
        var refreshes = 0;
        var log = new ConcurrentQueue<string>();
        using var watches = new HardwareWatches(FastSchedule, log.Enqueue);
        watches.Register(new HardwareWatch("displays.hardware", "Monitors", HardwareSignal.Session,
            () => Interlocked.Increment(ref refreshes), () => !Volatile.Read(ref fixedByHand)));
        watches.Signal(HardwareSignal.Session);
        Assert.True(WaitFor(log, "try 1 of 4 still looks stale"));
        Volatile.Write(ref fixedByHand, true); // the person pressed Refresh
        Assert.True(WaitFor(log, "looks right before try 2"));
        Assert.Equal(1, Volatile.Read(ref refreshes));
    }

    [Fact]
    public void The_retry_uses_the_stricter_check_when_the_watch_has_one()
    {
        var refreshes = 0;
        var log = new ConcurrentQueue<string>();
        using var watches = new HardwareWatches(FastSchedule, log.Enqueue);
        watches.Register(new HardwareWatch("displays.hardware", "Monitors", HardwareSignal.Session,
            () => Interlocked.Increment(ref refreshes), LooksStale: () => false, LooksUnsettled: () => Volatile.Read(ref refreshes) < 2,
            Describe: () => "fake monitors"));
        watches.Signal(HardwareSignal.Session, "fake unlock");
        Assert.True(WaitFor(log, "try 2 of 4 found it"));
        Assert.Contains(log, l => l.StartsWith("Monitors (after Session): try 1 of 4 still looks stale") && l.EndsWith("Found: fake monitors."));
        Thread.Sleep(FastSchedule[^1] * 2);
        Assert.Equal(2, Volatile.Read(ref refreshes));
    }

    [Fact]
    public void A_signal_the_watch_does_not_list_starts_nothing()
    {
        var monitors = new FakeMonitors(readyOn: 1);
        var (watches, log) = Make(monitors);
        using (watches)
        {
            watches.Signal(HardwareSignal.Audio, "fake audio");
            Thread.Sleep(FastSchedule[^1] * 2);
            Assert.Equal(0, monitors.Refreshes);
            Assert.Contains(log, l => l.Contains("Signal Audio (fake audio): no watch"));
        }
    }

    [Fact]
    public void The_same_kind_told_twice_within_the_window_passes_once()
    {
        var filter = new HardwareSignalFilter();
        Assert.True(filter.Pass(HardwareSignal.Session, 10_000));
        Assert.False(filter.Pass(HardwareSignal.Session, 10_500)); // SystemEvents telling the same console connect
        Assert.True(filter.Pass(HardwareSignal.Display, 10_500)); // another kind is news
        Assert.True(filter.Pass(HardwareSignal.Session, 12_000));
    }

    [Fact]
    public void Monitors_look_stale_when_fewer_answer_than_before_with_the_same_displays()
    {
        var expected = new MonitorExpectation();
        string[] two = [@"\\?\DISPLAY#A", @"\\?\DISPLAY#B"];
        Assert.False(expected.LooksStale(two, 2)); // learned: these two answer
        Assert.True(expected.LooksStale(two, 1)); // one still not answering after Remote Desktop
        Assert.True(expected.LooksStale(two, 0));
        Assert.False(expected.LooksStale(two, 2));
        Assert.False(expected.LooksStale([@"\\?\DISPLAY#TV"], 1));
        Assert.False(expected.LooksStale([], 0)); // a laptop panel alone
        Assert.True(expected.LooksStale([@"\\?\DISPLAY#NEW"], 0)); // a desktop monitor and none answering
    }

    [Fact]
    public void A_monitor_answering_fewer_codes_than_before_looks_stale()
    {
        var expected = new MonitorExpectation();
        Assert.False(expected.AnswersLess(@"\\?\DISPLAY#A", 19));
        Assert.True(expected.AnswersLess(@"\\?\DISPLAY#A", 15)); // read while it was still waking
        Assert.True(expected.AnswersLess(@"\\?\DISPLAY#A", 0));
        Assert.False(expected.AnswersLess(@"\\?\DISPLAY#A", 19));
        Assert.False(expected.AnswersLess(@"\\?\DISPLAY#B", 0)); // never answered: left to the stricter check after an announcement
    }
}
