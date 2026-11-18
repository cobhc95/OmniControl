using System.Collections.Concurrent;
using OmniControl.App.Hotkeys;
using OmniControl.App.Rules;
using OmniControl.App.Services;
using OmniControl.Core.Engine;
using Xunit;

namespace OmniControl.Tests;

/// <summary>
/// The power journal and the wake decision, replayed with fake signals and a fake clock (nothing of Windows is touched, the PC never sleeps),
/// and the readiness wait and retries of a step that needs a device, with a fake device and no real delays.
/// </summary>
public class PowerJournalTests
{
    private static readonly long Confirm = (long)Timing.WakeConfirmWindow.TotalMilliseconds;

    private static PowerSignal S(PowerEventKind kind, PowerChannel channel) => new(kind, channel, "test");

    private static WakeDetector Detector() =>
        new(Confirm, (long)Timing.WakeQuiet.TotalMilliseconds, (long)Timing.StandbyDisplayOffMin.TotalMilliseconds);

    // ---- The wake decision ----

    [Fact]
    public void Suspend_then_resume_from_both_listeners_is_one_confirmed_wake()
    {
        var d = Detector();
        Assert.Null(d.Observe(S(PowerEventKind.Suspend, PowerChannel.Window), 0));
        Assert.Null(d.Observe(S(PowerEventKind.Suspend, PowerChannel.SystemEvents), 10));
        Assert.Equal(WakeVerdict.Pending, d.Observe(S(PowerEventKind.Resume, PowerChannel.Window), 600_000)!.Verdict);
        Assert.Equal(WakeVerdict.Confirmed, d.Observe(S(PowerEventKind.Resume, PowerChannel.SystemEvents), 600_050)!.Verdict);
        Assert.Equal(WakeVerdict.Duplicate, d.Observe(S(PowerEventKind.ClockGap, PowerChannel.Clock), 602_000)!.Verdict);
        Assert.Null(d.Expire(600_000 + Confirm + 1));
    }

    [Fact]
    public void Resume_without_suspend_is_confirmed_by_the_clock_gap()
    {
        var d = Detector();
        Assert.Equal(WakeVerdict.Pending, d.Observe(S(PowerEventKind.Resume, PowerChannel.Window), 1_000)!.Verdict);
        var decision = d.Observe(S(PowerEventKind.ClockGap, PowerChannel.Clock), 3_000)!;
        Assert.Equal(WakeVerdict.Confirmed, decision.Verdict);
        Assert.Equal(new[] { PowerChannel.Window, PowerChannel.Clock }, decision.Channels);
    }

    [Fact]
    public void A_lone_resume_without_suspend_or_second_channel_is_rejected()
    {
        var d = Detector();
        d.Observe(S(PowerEventKind.Resume, PowerChannel.SystemEvents), 1_000);
        Assert.Null(d.Expire(1_000 + Confirm - 1));
        Assert.Equal(WakeVerdict.Rejected, d.Expire(1_000 + Confirm)!.Verdict);
    }

    [Fact]
    public void A_lone_resume_after_its_suspend_is_confirmed_when_the_window_ends()
    {
        var d = Detector();
        d.Observe(S(PowerEventKind.Suspend, PowerChannel.Window), 0);
        d.Observe(S(PowerEventKind.Resume, PowerChannel.Window), 5_000);
        Assert.Equal(WakeVerdict.Confirmed, d.Expire(5_000 + Confirm)!.Verdict);
    }

    [Fact]
    public void A_duplicate_resume_fires_once()
    {
        var d = Detector();
        d.Observe(S(PowerEventKind.Suspend, PowerChannel.Window), 0);
        var verdicts = new[]
        {
            d.Observe(S(PowerEventKind.Resume, PowerChannel.Window), 100_000),       // PBT_APMRESUMEAUTOMATIC
            d.Observe(S(PowerEventKind.Resume, PowerChannel.Window), 101_000),       // PBT_APMRESUMESUSPEND: the same channel again
            d.Observe(S(PowerEventKind.Resume, PowerChannel.SystemEvents), 101_100),
            d.Observe(S(PowerEventKind.Resume, PowerChannel.SystemEvents), 101_200),
            d.Observe(S(PowerEventKind.ClockGap, PowerChannel.Clock), 102_000),
        }.Select(x => x!.Verdict).ToList();
        Assert.Equal(1, verdicts.Count(v => v == WakeVerdict.Confirmed));
        Assert.Equal(WakeVerdict.Confirmed, verdicts[2]);
        Assert.All(verdicts.Skip(3), v => Assert.Equal(WakeVerdict.Duplicate, v));
    }

    [Fact]
    public void Modern_standby_display_off_and_on_with_a_clock_gap_is_a_wake()
    {
        var d = Detector();
        d.Observe(S(PowerEventKind.DisplayOff, PowerChannel.Display), 0);
        Assert.Equal(WakeVerdict.Pending, d.Observe(S(PowerEventKind.DisplayOn, PowerChannel.Display), 600_000)!.Verdict);
        Assert.Equal(WakeVerdict.Confirmed, d.Observe(S(PowerEventKind.ClockGap, PowerChannel.Clock), 601_000)!.Verdict);
    }

    [Fact]
    public void A_screen_time_out_without_a_clock_gap_is_not_a_wake()
    {
        var d = Detector();
        d.Observe(S(PowerEventKind.DisplayOff, PowerChannel.Display), 0);
        d.Observe(S(PowerEventKind.DisplayOn, PowerChannel.Display), 600_000);
        Assert.Equal(WakeVerdict.Rejected, d.Expire(600_000 + Confirm)!.Verdict);
    }

    [Fact]
    public void A_short_blank_is_not_evidence()
    {
        var d = Detector();
        d.Observe(S(PowerEventKind.DisplayOff, PowerChannel.Display), 0);
        Assert.Null(d.Observe(S(PowerEventKind.DisplayOn, PowerChannel.Display), 1_000));
    }

    [Fact]
    public void Two_sleeps_in_a_row_are_two_wakes()
    {
        var d = Detector();
        foreach (var start in new[] { 0L, 1_000_000L })
        {
            d.Observe(S(PowerEventKind.Suspend, PowerChannel.Window), start);
            d.Observe(S(PowerEventKind.Resume, PowerChannel.Window), start + 60_000);
            Assert.Equal(WakeVerdict.Confirmed, d.Observe(S(PowerEventKind.Resume, PowerChannel.SystemEvents), start + 60_100)!.Verdict);
        }
    }

    // ---- The clock check ----

    [Fact]
    public void Clock_gap_sees_a_late_sample_and_says_whether_the_PC_slept()
    {
        var gap = new ClockGap(2_000, 20_000);
        Assert.Null(gap.Sample(0, 0));
        Assert.Null(gap.Sample(2_000, 2_000));
        var slept = gap.Sample(602_000, 4_000)!;                     // the sleep clock stood still: slept
        Assert.Equal(PowerEventKind.ClockGap, slept.Kind);
        Assert.Contains("slept", slept.Detail);
        var frozen = gap.Sample(1_202_000, 604_000)!;                // both clocks ran: frozen (Modern Standby)
        Assert.Contains("stopped", frozen.Detail);
        Assert.Null(gap.Sample(1_204_000, 606_000));
    }

    // ---- The journal ----

    private sealed class FakeClock
    {
        public long Ms;
        public DateTime Now => new DateTime(2026, 11, 18, 1, 0, 0).AddMilliseconds(Ms);
    }

    private static (PowerJournal Journal, FakeClock Clock, ConcurrentQueue<string> Log) Journal(PowerJournalData? data = null, int session = 1)
    {
        var clock = new FakeClock();
        var log = new ConcurrentQueue<string>();
        var journal = new PowerJournal(data ?? new PowerJournalData(), _ => { }, log.Enqueue, () => clock.Now, () => clock.Ms,
            () => new DateTime(2026, 11, 18, 0, 0, 0), session);
        return (journal, clock, log);
    }

    [Fact]
    public void Journal_raises_one_wake_and_merges_the_same_event_told_twice()
    {
        var (journal, clock, log) = Journal();
        var woke = 0;
        journal.Woke += () => woke++;
        journal.Record(S(PowerEventKind.Suspend, PowerChannel.Window));
        journal.Record(S(PowerEventKind.Suspend, PowerChannel.SystemEvents));
        clock.Ms = 3_600_000;
        journal.Record(S(PowerEventKind.Resume, PowerChannel.Window));
        journal.Record(S(PowerEventKind.Resume, PowerChannel.SystemEvents));
        clock.Ms += 2_000;
        journal.Record(S(PowerEventKind.ClockGap, PowerChannel.Clock));
        clock.Ms += Confirm;
        journal.Tick();

        Assert.Equal(1, woke);
        var entries = journal.Entries;
        Assert.Equal(new[] { "Suspend", "Resume", "ClockGap" }, entries.Select(e => e.Kind));
        Assert.Equal("Window, SystemEvents", entries[0].Channels);
        Assert.Contains(log, line => line.StartsWith("Wake confirmed"));
        Assert.Contains(log, line => line.StartsWith("Wake duplicate"));
    }

    [Fact]
    public void At_log_in_runs_once_per_sign_in()
    {
        var data = new PowerJournalData();
        Assert.False(Journal(data).Journal.TakeLogin(startedByWindows: false));
        Assert.True(Journal(data).Journal.TakeLogin(startedByWindows: true));
        Assert.False(Journal(data).Journal.TakeLogin(startedByWindows: true));        // OmniControl started again in the same sign-in
        Assert.True(Journal(data, session: 2).Journal.TakeLogin(startedByWindows: true)); // signed out and in again
    }

    // ---- Readiness and retries ----

    private sealed class FakeDevice(int answersOnCheck)
    {
        public int Checks;

        public Task<string?> Probe(CancellationToken _) =>
            Task.FromResult(++Checks >= answersOnCheck ? null : "it did not answer");
    }

    private static Func<TimeSpan, CancellationToken, Task> Recording(List<TimeSpan> waits) => (wait, _) =>
    {
        waits.Add(wait);
        return Task.CompletedTask;
    };

    [Fact]
    public async Task Readiness_wait_ends_when_the_device_answers_on_the_third_check()
    {
        var device = new FakeDevice(answersOnCheck: 3);
        var waits = new List<TimeSpan>();
        var result = await ReadyWait.UntilAsync(device.Probe, Timing.ReadyBackoff, Timing.ReadyTimeout, Recording(waits), CancellationToken.None);
        Assert.True(result.Ready);
        Assert.Equal(3, result.Checks);
        Assert.Equal(new[] { Timing.ReadyBackoff[0], Timing.ReadyBackoff[1] }, waits);
    }

    [Fact]
    public async Task Readiness_wait_gives_up_at_its_time_limit()
    {
        var device = new FakeDevice(answersOnCheck: int.MaxValue);
        var waits = new List<TimeSpan>();
        var result = await ReadyWait.UntilAsync(device.Probe, Timing.ReadyBackoff, Timing.ReadyTimeout, Recording(waits), CancellationToken.None);
        Assert.False(result.Ready);
        Assert.Equal("it did not answer", result.Problem);
        Assert.True(waits.Aggregate(TimeSpan.Zero, (a, b) => a + b) <= Timing.ReadyTimeout);
        Assert.Equal(waits.Count + 1, result.Checks);
    }

    [Fact]
    public async Task A_failed_step_is_tried_again_until_it_works()
    {
        var waits = new List<TimeSpan>();
        var (outcome, tries) = await ReadyWait.RetryAsync(n => Task.FromResult(n < 2 ? new StepOutcome("not reachable") : StepOutcome.Done),
            Timing.StepRetryBackoff, Timing.StepTriesMax, Recording(waits), CancellationToken.None);
        Assert.Null(outcome.Problem);
        Assert.Equal(2, tries);
        Assert.Single(waits);
    }

    [Fact]
    public async Task A_step_stopped_by_the_user_is_not_tried_again()
    {
        var (outcome, tries) = await ReadyWait.RetryAsync(_ => Task.FromResult(new StepOutcome("Stopped", MayRetry: false)),
            Timing.StepRetryBackoff, Timing.StepTriesMax, Recording(new List<TimeSpan>()), CancellationToken.None);
        Assert.Equal("Stopped", outcome.Problem);
        Assert.Equal(1, tries);
    }
}
