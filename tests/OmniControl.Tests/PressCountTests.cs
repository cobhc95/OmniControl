using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Tests;

/// A mouse button with an action "When the button is pressed twice / three times": presses are counted within the allowed pause and the
/// right count's actions run; a button without such an action is left as it always was.
public class PressCountTests
{
    private static readonly TimeSpan Within = TimeSpan.FromMilliseconds(300);

    private static MouseAction Action(string trigger, int within = 300, int holdFor = 400) => MouseAction.From("keys:Ctrl+C", new Dictionary<string, string>
    {
        [MouseOptions.Trigger.Key] = trigger,
        [MouseOptions.PressesWithin.Key] = within.ToString(),
        [MouseOptions.HoldFor.Key] = holdFor.ToString(),
    });

    private static void Press(PressCounter counter, FakeClock clock, List<int> reported, int most = 3, int heldMs = 50)
    {
        counter.Down(MouseButton.Forward);
        clock.Advance(TimeSpan.FromMilliseconds(heldMs));
        counter.Up(MouseButton.Forward, most, Within, reported.Add);
    }

    [Fact]
    public void SinglePress_ReportsOneAfterThePause()
    {
        var clock = new FakeClock();
        using var counter = new PressCounter(clock);
        var reported = new List<int>();
        Press(counter, clock, reported);
        clock.Advance(TimeSpan.FromMilliseconds(299));
        Assert.Empty(reported);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(new[] { 1 }, reported);
    }

    [Fact]
    public void TwoPressesInTime_ReportTwoOnce()
    {
        var clock = new FakeClock();
        using var counter = new PressCounter(clock);
        var reported = new List<int>();
        Press(counter, clock, reported);
        clock.Advance(TimeSpan.FromMilliseconds(250));
        Press(counter, clock, reported);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { 2 }, reported);
    }

    [Fact]
    public void SecondPressTooLate_IsANewSeries()
    {
        var clock = new FakeClock();
        using var counter = new PressCounter(clock);
        var reported = new List<int>();
        Press(counter, clock, reported);
        clock.Advance(TimeSpan.FromMilliseconds(301));
        Press(counter, clock, reported);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { 1, 1 }, reported);
    }

    [Fact]
    public void ThirdPress_ReportsAtOnceAtTheMost()
    {
        var clock = new FakeClock();
        using var counter = new PressCounter(clock);
        var reported = new List<int>();
        for (var i = 0; i < 3; i++) Press(counter, clock, reported);
        Assert.Equal(new[] { 3 }, reported);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { 3 }, reported);
    }

    [Fact]
    public void OnlyTwiceConfigured_ReportsAtOnceAtTwo()
    {
        var clock = new FakeClock();
        using var counter = new PressCounter(clock);
        var reported = new List<int>();
        Press(counter, clock, reported, most: 2);
        Press(counter, clock, reported, most: 2);
        Assert.Equal(new[] { 2 }, reported);
    }

    [Fact]
    public void SecondPressSlowButStartedInTime_IsStillCounted()
    {
        var clock = new FakeClock();
        using var counter = new PressCounter(clock);
        var reported = new List<int>();
        Press(counter, clock, reported);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Press(counter, clock, reported, heldMs: 350);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { 2 }, reported);
    }

    [Fact]
    public void HeldAfterAPress_FlushesTheEarlierCount()
    {
        var clock = new FakeClock();
        using var counter = new PressCounter(clock);
        var reported = new List<int>();
        Press(counter, clock, reported);
        counter.Down(MouseButton.Forward);
        counter.Flush(MouseButton.Forward);
        Assert.Equal(new[] { 1 }, reported);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { 1 }, reported);
    }

    [Fact]
    public void CancelAll_DropsWhatWaits()
    {
        var clock = new FakeClock();
        using var counter = new PressCounter(clock);
        var reported = new List<int>();
        Press(counter, clock, reported);
        counter.CancelAll();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(reported);
    }

    [Fact]
    public void NoCountedAction_NoSplit() =>
        Assert.Null(HoldSplit.Of(Action(MouseOptions.OnPress), new[] { Action(MouseOptions.OnRelease) }));

    [Fact]
    public void Twice_WaitsAndSteps()
    {
        var twice = Action(MouseOptions.OnTwice, 500);
        var split = HoldSplit.Of(Action(MouseOptions.OnPress), new[] { twice })!;
        Assert.True(split.IsCounted);
        Assert.False(split.HasHeld);
        Assert.Equal(2, split.MaxCount);
        Assert.Equal(TimeSpan.FromMilliseconds(500), split.Within);
        Assert.NotNull(split.StepFor(1).First);
        Assert.Equal(new[] { twice }, split.StepFor(2).Extras);
        Assert.Equal(new[] { twice }, split.StepFor(3).Extras);
    }

    [Fact]
    public void FirstActionOnThree_RunsOnlyOnThree()
    {
        var first = Action(MouseOptions.OnThrice);
        var split = HoldSplit.Of(first, Array.Empty<MouseAction>())!;
        Assert.Equal(3, split.MaxCount);
        Assert.True(split.StepFor(1).IsEmpty);
        Assert.True(split.StepFor(2).IsEmpty);
        Assert.Same(first, split.StepFor(3).First);
    }

    [Fact]
    public void WithHeld_BothKept()
    {
        var split = HoldSplit.Of(Action(MouseOptions.OnTwice), new[] { Action(MouseOptions.OnHold, holdFor: 600) })!;
        Assert.True(split.HasHeld);
        Assert.True(split.IsCounted);
        Assert.Equal(TimeSpan.FromMilliseconds(600), split.HoldFor);
    }

    [Fact]
    public void ReleasedFirst_RunsAtOnce_CountedExtraWaits()
    {
        var split = HoldSplit.Of(Action(MouseOptions.OnRelease), new[] { Action(MouseOptions.OnTwice) })!;
        Assert.False(split.DefersFirst);
        Assert.True(split.StepFor(1).IsEmpty);
        Assert.True(split.IsCounted);
    }

    [Fact]
    public void PressesWithin_ShowsOnlyForCounts()
    {
        string? Trigger(string value, string key) => key == MouseOptions.Trigger.Key ? value : null;
        Assert.True(MouseOptions.PressesWithin.IsShown(k => Trigger(MouseOptions.OnTwice, k)));
        Assert.True(MouseOptions.PressesWithin.IsShown(k => Trigger(MouseOptions.OnThrice, k)));
        Assert.False(MouseOptions.PressesWithin.IsShown(k => Trigger(MouseOptions.OnHold, k)));
        Assert.Equal(new[] { MouseOptions.OnPress, MouseOptions.OnRelease, MouseOptions.OnHold, MouseOptions.OnTwice, MouseOptions.OnThrice, MouseOptions.Repeatedly },
            MouseOptions.Trigger.Choices);
    }
}
