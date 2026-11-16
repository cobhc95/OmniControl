using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Services;

namespace OmniControl.Tests;

/// <summary>
/// The short words a Mixer row, the Wiring tab and the footer use for the effects engine (<see cref="EffectsRules.Brief"/>), the steady
/// delay figure (<see cref="StableDelay"/>), the Method menu's enabled and disabled entries (<see cref="EffectsRules.OfferOf"/>), the
/// footer's two sizes of message and the planner's answer for a program set to Virtual cable.
/// </summary>
public class StatusWordsTests
{
    // ---- the steady delay: five-ms steps, held until the same new figure is read several times in a row ----

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 5)]
    [InlineData(67, 65)]
    [InlineData(68, 70)]
    [InlineData(70, 70)]
    public void Round_GoesToTheNearestStep(int ms, int expected) => Assert.Equal(expected, StableDelay.Round(ms));

    [Fact]
    public void FirstMeasurement_IsShownAtOnce_AndNothingIsShownBefore()
    {
        var delay = new StableDelay(5, 3);
        Assert.Null(delay.Shown);
        Assert.Null(delay.Feed(0));
        Assert.Equal(70, delay.Feed(69));
    }

    [Fact]
    public void PendingMeasurement_KeepsTheLastGoodFigure()
    {
        var delay = new StableDelay(5, 3);
        delay.Feed(69);
        Assert.Equal(70, delay.Feed(0));
        Assert.Equal(70, delay.Feed(-1));
    }

    [Fact]
    public void ReadingsThatFlipBetweenTwoFigures_NeverChangeIt()
    {
        var delay = new StableDelay(5, 3);
        delay.Feed(70);
        foreach (var reading in new[] { 76, 70, 76, 70, 76, 77, 70, 76 }) Assert.Equal(70, delay.Feed(reading));
    }

    [Fact]
    public void NewFigure_ReplacesTheOld_OnlyAfterTheSameValueWasReadSeveralTimesInARow()
    {
        var delay = new StableDelay(5, 3);
        delay.Feed(70);
        Assert.Equal(70, delay.Feed(80));
        Assert.Equal(70, delay.Feed(81)); // 80 again: two in a row
        Assert.Equal(80, delay.Feed(79)); // third in a row
        Assert.Equal(80, delay.Feed(80));
    }

    [Fact]
    public void WithinOneStep_NoChange()
    {
        var delay = new StableDelay(5, 2);
        delay.Feed(70);
        foreach (var reading in new[] { 68, 72, 71, 69 }) Assert.Equal(70, delay.Feed(reading));
    }

    // ---- the short status of a row ----

    [Theory]
    [InlineData(StatusKind.Running, RunMethod.Recording, 70, "Recording · 70 ms")]
    [InlineData(StatusKind.Running, RunMethod.Cable, 10, "Cable · 10 ms")]
    [InlineData(StatusKind.Running, RunMethod.Bus, 40, "Bus · 40 ms")]
    [InlineData(StatusKind.Running, RunMethod.Apo, null, "APO")]
    [InlineData(StatusKind.Running, RunMethod.Recording, null, "Recording")]
    [InlineData(StatusKind.Ready, RunMethod.Recording, null, "Recording ready")]
    [InlineData(StatusKind.WaitingForSound, RunMethod.None, null, "Waiting for sound")]
    [InlineData(StatusKind.StartsWhenPlays, RunMethod.None, null, "Starts when it plays")]
    [InlineData(StatusKind.NoFreeCable, RunMethod.None, null, "No free cable")]
    [InlineData(StatusKind.NeedsCable, RunMethod.None, null, "Needs VB-CABLE")]
    [InlineData(StatusKind.NeedsAnotherCable, RunMethod.None, null, "Needs another cable")]
    [InlineData(StatusKind.WentSilent, RunMethod.Recording, null, "Recording went silent")]
    [InlineData(StatusKind.CouldNotMove, RunMethod.Cable, null, "Windows would not move it")]
    [InlineData(StatusKind.Stopped, RunMethod.None, null, "Engine stopped")]
    public void Brief_IsShortAndNamesTheRealMethod(StatusKind kind, RunMethod method, int? delay, string expected)
    {
        Assert.Equal(expected, EffectsRules.Brief(kind, method, delay));
    }

    [Fact]
    public void EveryBrief_IsOneShortLine_WithNoAboutNoDelayWordAndNoSentencePunctuation()
    {
        foreach (var kind in Enum.GetValues<StatusKind>())
            foreach (var method in Enum.GetValues<RunMethod>())
            {
                var text = EffectsRules.Brief(kind, method, 70);
                Assert.DoesNotContain('\n', text);
                Assert.DoesNotContain("about", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("delay", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".", text);
                Assert.True(text.Length <= 28, text);
            }
    }

    [Fact]
    public void EngineStatus_BriefFollowsItsKindAndMethod_AndFallsBackOnTheState()
    {
        var running = new EngineStatus(EngineState.Running, "Effects running by recording · about 70 ms delay", null, StatusKind.Running, RunMethod.Recording, 70);
        Assert.Equal("Recording · 70 ms", running.Brief);
        Assert.Equal("Not available", new EngineStatus(EngineState.Unavailable, "Some long reason.").Brief);
        Assert.Equal("Engine stopped", new EngineStatus(EngineState.Failed, "Effects stopped: x.").Brief);
        Assert.Equal(string.Empty, EngineStatus.Off.Brief);
    }

    [Fact]
    public void TheTooltip_HoldsTheFullLine_AndWhatToDoNext()
    {
        var status = new EngineStatus(EngineState.Unavailable, $"{EffectsRules.NeedsAnotherCable}. Playing without effects.", null, StatusKind.NoFreeCable);
        var tip = status.TipFor(EffectsRules.MethodFor("app"));
        Assert.StartsWith(status.Text, tip);
        Assert.Contains("Playing without effects", tip);
        Assert.Contains("Use Recording", tip);
    }

    // ---- the test result: one short line, the long explanation in the tooltip ----

    [Fact]
    public void TestPassed_IsOneShortLine_WithTheFullSentenceBehindIt()
    {
        var outcome = EffectsRules.TestPassed(70, SilenceMethod.Quiet, 1, string.Empty);
        Assert.Equal("Test passed · 70 ms", outcome.Brief);
        Assert.StartsWith("It works on this PC", outcome.Full);
        Assert.Contains("Effects that ran: 1", outcome.Full);
    }

    [Fact]
    public void TestFailed_IsOneShortLine()
    {
        Assert.Equal("Test failed · went silent", EffectsRules.TestWentSilent("Spotify").Brief);
        foreach (var outcome in new[]
                 {
                     EffectsRules.TestNotListed(), EffectsRules.TestNotPlaying("Spotify"), EffectsRules.TestCannotRecord("Spotify", "no"),
                     EffectsRules.TestHeardNothing("Spotify"), EffectsRules.TestRunning(), EffectsRules.TestAlreadyRunning(),
                 })
        {
            Assert.True(outcome.Brief.Length <= 30, outcome.Brief);
            Assert.DoesNotContain('\n', outcome.Brief);
            Assert.NotEqual(outcome.Brief, outcome.Full);
        }
    }

    [Fact]
    public void FooterMessage_ShowsTheShortLine_AndKeepsTheFullTextForTheTooltip()
    {
        var message = StatusMessage.Compose("Test passed · 70 ms", "It works on this PC. Extra delay about 70 ms.");
        Assert.Equal("Test passed · 70 ms", StatusMessage.BriefOf(message));
        Assert.Equal("It works on this PC. Extra delay about 70 ms.", StatusMessage.FullOf(message));
        Assert.Equal("Added Spotify", StatusMessage.BriefOf("Added Spotify"));
        Assert.Equal("Added Spotify", StatusMessage.FullOf("Added Spotify"));
    }

    // ---- the Method menu: each entry live, and disabled when it cannot work ----

    private static RouteFacts Facts(int pairs, bool free, bool bus, string? recordingWhy = null, RunMethod current = RunMethod.None) =>
        new(new CableOffer(pairs, free, bus), recordingWhy, current);

    [Fact]
    public void VirtualCable_IsDisabled_WhenNoCablePairIsInstalled()
    {
        var offer = EffectsRules.OfferOf(EffectsRules.RouteCable, Facts(0, false, false));
        Assert.False(offer.Available);
        Assert.Equal("Not available: no cable pair installed", offer.Description);
        Assert.Contains("no cable pair installed", offer.Tip);
    }

    [Fact]
    public void VirtualCable_IsDisabled_WhenTheMasterBusHoldsTheOnlyPair()
    {
        var offer = EffectsRules.OfferOf(EffectsRules.RouteCable, Facts(1, false, true));
        Assert.False(offer.Available);
        Assert.Equal("Not available: the master bus uses the cable", offer.Description);
    }

    [Fact]
    public void VirtualCable_IsDisabled_WhenEveryPairIsInUse_AndEnabledWhenOneIsFree()
    {
        Assert.Equal("Not available: every cable pair is in use", EffectsRules.OfferOf(EffectsRules.RouteCable, Facts(2, false, true)).Description);
        var free = EffectsRules.OfferOf(EffectsRules.RouteCable, Facts(2, true, true));
        Assert.True(free.Available);
        Assert.Equal(EffectsRules.RouteCable.Description, free.Description);
    }

    [Fact]
    public void Recording_IsDisabledOnlyWhenItIsImpossibleOnThisPc()
    {
        Assert.True(EffectsRules.OfferOf(EffectsRules.RouteCapture, Facts(0, false, false)).Available);
        var offer = EffectsRules.OfferOf(EffectsRules.RouteCapture, Facts(1, true, false, "Needs Windows 10 version 2004 or later"));
        Assert.False(offer.Available);
        Assert.Equal("Not available: needs Windows 10 version 2004 or later", offer.Description);
    }

    [Fact]
    public void Automatic_IsAlwaysAvailable_AndSaysWhatItUsesNow()
    {
        Assert.Equal("Currently: recording", EffectsRules.OfferOf(EffectsRules.RouteAutomatic, Facts(1, false, true, current: RunMethod.Recording)).Description);
        Assert.Equal("Currently: cable", EffectsRules.OfferOf(EffectsRules.RouteAutomatic, Facts(2, true, true, current: RunMethod.Cable)).Description);
        var idle = EffectsRules.OfferOf(EffectsRules.RouteAutomatic, Facts(0, false, false));
        Assert.True(idle.Available);
        Assert.Equal(EffectsRules.RouteAutomatic.Description, idle.Description);
    }

    [Fact]
    public void ADisabledCard_CannotBePicked()
    {
        EffectsRoute? picked = null;
        var card = new OmniControl.App.Pages.Volume.RouteCard(EffectsRules.RouteCable, r => picked = r);
        card.Sync(false, EffectsRules.OfferOf(EffectsRules.RouteCable, Facts(1, false, true)));
        card.PickCommand.Execute(null);
        Assert.Null(picked);
        card.Sync(false, EffectsRules.OfferOf(EffectsRules.RouteCable, Facts(2, true, false)));
        card.PickCommand.Execute(null);
        Assert.Equal(EffectsRules.RouteCable, picked);
    }

    [Fact]
    public void UseRecording_SetsTheMethodToRecording_InOneClick()
    {
        EffectsRoute? picked = null;
        var choice = OmniControl.App.Pages.Volume.RouteChoice.ForProgram(r => picked = r, () => { });
        choice.SyncProgram(EffectsRules.RouteCable, Facts(1, false, true));
        Assert.True(choice.RecordingAvailable);
        choice.UseRecording!.Command.Execute(null);
        Assert.Equal(EffectsRules.RouteCapture, picked);
    }

    // ---- the planner: a program set to Virtual cable with no free cable is said so at once, silent or not ----

    [Fact]
    public void Planner_ProgramOnVirtualCable_WithNoFreePair_SaysNoFreeCable_EvenWhileSilent()
    {
        var s = OwnerPc.Settings();
        s.Apps.First(a => a.Key == OwnerPc.Spotify).FxRoute = EffectsRules.RouteCable.Id;
        var playing = new HashSet<string> { OwnerPc.Spotify };
        var none = new HashSet<string>();
        var plan = RoutePlanner.Make(new PlanInput(OwnerPc.Support, true, s, playing, none, none, OwnerPc.Playback,
            OwnerPc.Recording, none));
        var status = plan.Blocked[OwnerPc.Spotify];
        Assert.Equal(EngineState.Unavailable, status.State);
        Assert.Equal(StatusKind.NoFreeCable, status.Kind);
        Assert.Equal("No free cable", status.Brief);
        Assert.Equal(1, plan.CablePairs);
        Assert.False(plan.CableFree);
    }
}
