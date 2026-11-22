using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Tests;

/// A mouse button with an action "When the button is held": a short press runs the pressed actions, a long one the held ones, and a
/// button without one is left as it always was.
public class HoldPressTests
{
    private static MouseAction Action(string trigger, int holdFor = 400) => MouseAction.From("keys:Ctrl+C", new Dictionary<string, string>
    {
        [MouseOptions.Trigger.Key] = trigger,
        [MouseOptions.HoldFor.Key] = holdFor.ToString(),
    });

    [Fact]
    public void NoHeldAction_NoSplit() =>
        Assert.Null(HoldSplit.Of(Action(MouseOptions.OnPress), new[] { Action(MouseOptions.OnPress) }));

    [Fact]
    public void HeldExtra_DefersPressedFirst()
    {
        var held = Action(MouseOptions.OnHold, 600);
        var split = HoldSplit.Of(Action(MouseOptions.OnPress), new[] { held })!;
        Assert.True(split.DefersFirst);
        Assert.Null(split.HeldFirst);
        Assert.Equal(new[] { held }, split.HeldExtras);
        Assert.Empty(split.ShortExtras);
        Assert.Equal(TimeSpan.FromMilliseconds(600), split.HoldFor);
    }

    [Fact]
    public void ReleasedFirst_IsNotDeferred() =>
        Assert.False(HoldSplit.Of(Action(MouseOptions.OnRelease), new[] { Action(MouseOptions.OnHold) })!.DefersFirst);

    [Fact]
    public void ShortPress_RunsShortOnly()
    {
        var clock = new FakeClock();
        using var watch = new HoldWatch(clock);
        var (shorts, helds) = (0, 0);
        watch.Start(MouseButton.Forward, TimeSpan.FromMilliseconds(400), () => shorts++, () => helds++);
        clock.Advance(TimeSpan.FromMilliseconds(399));
        Assert.True(watch.Release(MouseButton.Forward));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal((1, 0), (shorts, helds));
    }

    [Fact]
    public void Hold_RunsHeldOnce_AndReleaseDoesNothing()
    {
        var clock = new FakeClock();
        using var watch = new HoldWatch(clock);
        var (shorts, helds) = (0, 0);
        watch.Start(MouseButton.Forward, TimeSpan.FromMilliseconds(400), () => shorts++, () => helds++);
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.False(watch.Release(MouseButton.Forward));
        Assert.Equal((0, 1), (shorts, helds));
    }

    [Fact]
    public void HoldFor_ShowsOnlyWhenHeld()
    {
        string? Trigger(string value, string key) => key == MouseOptions.Trigger.Key ? value : null;
        Assert.True(MouseOptions.HoldFor.IsShown(k => Trigger(MouseOptions.OnHold, k)));
        Assert.False(MouseOptions.HoldFor.IsShown(k => Trigger(MouseOptions.OnPress, k)));
        Assert.Contains(MouseOptions.OnHold, MouseOptions.Trigger.Choices!);
        Assert.Contains(MouseOptions.OnHold, MouseOptions.TriggerWithHold.Choices!);
    }

    [Fact]
    public void LaterAction_RunsPressedOrHeld()
    {
        var later = MouseOptions.OneShot(MouseOptions.Common)!;
        Assert.Equal(new[] { MouseOptions.OnPress, MouseOptions.OnHold }, later.Single(o => o.Key == MouseOptions.Trigger.Key).Choices);
        Assert.Contains(later, o => o.Key == MouseOptions.HoldFor.Key);
        Assert.DoesNotContain(later, o => o.Key == MouseOptions.RepeatDelay.Key);
    }

    /// A clock whose time moves only when told; its timers fire as it passes their time.
    private sealed class FakeClock : TimeProvider
    {
        private readonly List<FakeTimer> _timers = new();
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new FakeTimer(this, () => callback(state));
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
            foreach (var timer in _timers.ToList()) timer.FireIfDue(_now);
        }

        private sealed class FakeTimer(FakeClock clock, Action fire) : ITimer
        {
            private DateTimeOffset? _due;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                return true;
            }

            public void FireIfDue(DateTimeOffset now)
            {
                if (_due is not { } due || due > now) return;
                _due = null;
                fire();
            }

            public void Dispose() => _due = null;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
