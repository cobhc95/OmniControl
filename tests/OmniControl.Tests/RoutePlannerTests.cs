using System.Text.Json;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using Xunit.Abstractions;

namespace OmniControl.Tests;

/// <summary>The route planner on the owner's PC and settings (Full control and Fast), and across the events a session goes through.</summary>
public class RoutePlannerTests(ITestOutputHelper output)
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static HashSet<string> Set(params string[] keys) => keys.ToHashSet();

    private RoutePlan Plan(MixerSettings s, SoundMode mode, IReadOnlySet<string> playing, IReadOnlySet<string>? sounding = null,
        IReadOnlySet<string>? holding = null, IReadOnlySet<string>? failed = null, bool twoPairs = false,
        IReadOnlySet<string>? capturing = null, bool captureWorks = true)
    {
        var plan = RoutePlanner.Make(new PlanInput(mode, twoPairs ? OwnerPc.SupportTwoPairs : OwnerPc.Support, captureWorks, s, playing,
            sounding ?? None, holding ?? None, twoPairs ? OwnerPc.PlaybackTwoPairs : OwnerPc.Playback,
            twoPairs ? OwnerPc.RecordingTwoPairs : OwnerPc.Recording, failed ?? None, capturing));
        output.WriteLine(PlanPrinter.Print(plan));
        output.WriteLine("--");
        NoLoops(plan);
        return plan;
    }

    /// <summary>No run plays into the cable it records, and no program run records the master bus's cable.</summary>
    private static void NoLoops(RoutePlan plan)
    {
        foreach (var run in plan.Runs.Values)
        {
            // A microphone's run plays INTO its cable on purpose (it records microphones); a Fast-mode recording uses no cable.
            if (run.Kind == RunKind.Mic || run.Cable is null) continue;
            Assert.DoesNotContain(run.Cable, run.Outputs);
            if (run.Kind == RunKind.Cable) Assert.NotEqual(plan.Bus?.Cable, run.Cable);
            Assert.NotEqual(OwnerPc.Cable16, run.Cable); // never usable while its pair's "CABLE Input" is in use (measured)
        }
    }

    /// <summary>
    /// The owner's PC as it is (one VB-CABLE): the bus holds the whole pair (measured live while the bus ran on "CABLE Input": "CABLE
    /// In 16ch" refused both recording and playing, 0x8889000A). Spotify on Automatic is recorded instead and plays into the bus.
    /// </summary>
    [Fact]
    public void Full_OneVbCable_Automatic_SpotifyIsRecordedIntoTheBus()
    {
        var plan = Plan(OwnerPc.Settings(), SoundModes.Full, Set(OwnerPc.Helium, OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify));
        Assert.Equal(OwnerPc.CableInput, plan.Runs[MixerService.MasterKey].Cable);
        Assert.Contains(OwnerPc.Cable16, plan.CablesInUse);
        var spotify = plan.Runs[OwnerPc.Spotify];
        Assert.Equal(RunKind.Capture, spotify.Kind);
        Assert.Null(spotify.Cable);
        Assert.Equal(new[] { OwnerPc.CableInput }, spotify.Outputs); // its effects first, then the master's in the bus
        // Silent but open, no pair free: armed by recording now (no cable taken), so its effects apply from its first sound.
        Assert.Equal(RunKind.Capture, plan.Runs[OwnerPc.Helium].Kind);
        Assert.False(plan.Blocked.ContainsKey(OwnerPc.Helium));
    }

    /// <summary>The owner chose "Virtual cable" for Spotify: with the one pair held by the bus its effects wait for another cable.</summary>
    [Fact]
    public void Full_OneVbCable_RouteCable_SpotifyNeedsAnotherCable()
    {
        var s = OwnerPc.Settings();
        s.Apps.First(a => a.Key == OwnerPc.Spotify).FxRoute = SoundModes.RouteCable.Id;
        var plan = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify));
        Assert.False(plan.Runs.ContainsKey(OwnerPc.Spotify));
        Assert.Equal(EngineState.Unavailable, plan.Blocked[OwnerPc.Spotify].State);
        Assert.Equal($"{SoundModes.NeedsAnotherCable}. Playing without effects.", plan.Blocked[OwnerPc.Spotify].Text);
    }

    /// <summary>"Recording" never takes a cable, even when a whole pair is free.</summary>
    [Fact]
    public void Full_RouteCapture_NeverTakesACable_EvenWithAFreePair()
    {
        var s = OwnerPc.Settings();
        s.Apps.First(a => a.Key == OwnerPc.Spotify).FxRoute = SoundModes.RouteCapture.Id;
        var one = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify));
        Assert.Equal(RunKind.Capture, one.Runs[OwnerPc.Spotify].Kind);
        var two = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify), twoPairs: true);
        Assert.Equal(RunKind.Capture, two.Runs[OwnerPc.Spotify].Kind);
        Assert.DoesNotContain(OwnerPc.CableA, two.CablesInUse);
    }

    /// <summary>Automatic prefers a free pair: with a second pair the program gets its cable, not a recording.</summary>
    [Fact]
    public void Full_TwoPairs_Automatic_PrefersTheCable()
    {
        var plan = Plan(OwnerPc.Settings(), SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify), twoPairs: true);
        Assert.Equal(RunKind.Cable, plan.Runs[OwnerPc.Spotify].Kind);
        Assert.Equal(OwnerPc.CableA, plan.Runs[OwnerPc.Spotify].Cable);
    }

    /// <summary>A program already recording on Automatic keeps recording when a pair frees up (no move while it plays or pauses).</summary>
    [Fact]
    public void Full_Automatic_RecordingKeepsRecording_WhenAPairFrees()
    {
        var s = OwnerPc.Settings();
        var both = Plan(s, SoundModes.Full, Set(OwnerPc.Helium, OwnerPc.Spotify), sounding: Set(OwnerPc.Helium, OwnerPc.Spotify), twoPairs: true);
        Assert.Equal(RunKind.Cable, both.Runs[OwnerPc.Helium].Kind);
        Assert.Equal(RunKind.Capture, both.Runs[OwnerPc.Spotify].Kind);

        output.WriteLine("Helium closes; Spotify keeps recording:");
        var after = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify), capturing: Set(OwnerPc.Spotify), twoPairs: true);
        Assert.True(after.Runs[OwnerPc.Spotify].SameAs(both.Runs[OwnerPc.Spotify]));
        Assert.DoesNotContain(OwnerPc.CableA, after.CablesInUse);

        output.WriteLine("Spotify's run ended (it closed and opens again): Automatic takes the free pair:");
        var again = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify), twoPairs: true);
        Assert.Equal(RunKind.Cable, again.Runs[OwnerPc.Spotify].Kind);
    }

    /// <summary>A recorded program across a session: plays, pauses (kept), closes (waits), plays again (same run); never a loop.</summary>
    [Fact]
    public void Full_OneVbCable_RecordedProgram_StopsAndRestarts()
    {
        var s = OwnerPc.Settings();
        s.Apps.First(a => a.Key == OwnerPc.Helium).FxOn = false; // Helium without effects follows the master
        output.WriteLine("1. Spotify plays:");
        var p1 = Plan(s, SoundModes.Full, Set(OwnerPc.Helium, OwnerPc.Spotify), sounding: Set(OwnerPc.Helium, OwnerPc.Spotify));
        Assert.Equal(RunKind.Capture, p1.Runs[OwnerPc.Spotify].Kind);
        Assert.Contains(OwnerPc.Helium, p1.BusRoutes);
        Assert.False(p1.Runs.ContainsKey(OwnerPc.Helium));

        output.WriteLine("2. Spotify paused, its recording is kept:");
        var p2 = Plan(s, SoundModes.Full, Set(OwnerPc.Helium, OwnerPc.Spotify), capturing: Set(OwnerPc.Spotify));
        Assert.True(p2.Runs[OwnerPc.Spotify].SameAs(p1.Runs[OwnerPc.Spotify]));

        output.WriteLine("3. Spotify closed:");
        var p3 = Plan(s, SoundModes.Full, Set(OwnerPc.Helium));
        Assert.False(p3.Runs.ContainsKey(OwnerPc.Spotify));
        Assert.Equal(EngineState.Waiting, p3.Blocked[OwnerPc.Spotify].State);

        output.WriteLine("4. Spotify opened and plays again:");
        var p4 = Plan(s, SoundModes.Full, Set(OwnerPc.Helium, OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify));
        Assert.True(p4.Runs[OwnerPc.Spotify].SameAs(p1.Runs[OwnerPc.Spotify]));
        Assert.True(p4.Runs[MixerService.MasterKey].SameAs(p1.Runs[MixerService.MasterKey])); // the bus never restarts for a program
    }

    /// <summary>
    /// The bus given up: its pair is free again, so Automatic takes it (the preferred path); a program set to Recording plays on the real
    /// speakers by id, never into the cable nobody plays.
    /// </summary>
    [Fact]
    public void Full_OneVbCable_BusGivenUp_RecordedProgramPlaysOnTheSpeaker()
    {
        var s = OwnerPc.Settings();
        var automatic = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify), failed: Set(MixerService.MasterKey));
        Assert.Null(automatic.Bus);
        Assert.Equal(RunKind.Cable, automatic.Runs[OwnerPc.Spotify].Kind);
        Assert.Equal(new[] { OwnerPc.Speaker }, automatic.Runs[OwnerPc.Spotify].Outputs);

        s.Apps.First(a => a.Key == OwnerPc.Spotify).FxRoute = SoundModes.RouteCapture.Id;
        var recording = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify), failed: Set(MixerService.MasterKey));
        Assert.Equal(RunKind.Capture, recording.Runs[OwnerPc.Spotify].Kind);
        Assert.Equal(new[] { OwnerPc.Speaker }, recording.Runs[OwnerPc.Spotify].Outputs);
    }

    /// <summary>Where the Test found no way to record a silenced program, Automatic says why; nothing runs.</summary>
    [Fact]
    public void Full_RecordingImpossible_RowSaysWhy()
    {
        var plan = Plan(OwnerPc.Settings(), SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify), captureWorks: false);
        Assert.False(plan.Runs.ContainsKey(OwnerPc.Spotify));
        Assert.Equal(EngineState.Unavailable, plan.Blocked[OwnerPc.Spotify].State);
        Assert.StartsWith("Recording stops", plan.Blocked[OwnerPc.Spotify].Text);
    }

    /// <summary>Files written before the choice existed (the owner's, no "FxRoute") load as Automatic.</summary>
    [Fact]
    public void OldSettings_LoadAsAutomatic()
    {
        Assert.All(OwnerPc.Settings().Apps, a => Assert.Same(SoundModes.RouteAutomatic, SoundModes.RouteOf(a.FxRoute)));
        Assert.Same(SoundModes.RouteAutomatic, SoundModes.RouteOf("something-unknown"));
    }

    [Fact]
    public void Cables_TwoInputsOfOneVbCable_AreFoundInNameOrder()
    {
        var cables = OwnerPc.Support.Cables;
        Assert.Equal(2, cables.Count);
        Assert.Equal(OwnerPc.CableInput, cables[0].PlaybackId);
        Assert.Equal(OwnerPc.CableOutput, cables[0].RecordingId);
        Assert.Equal(OwnerPc.Cable16, cables[1].PlaybackId);
        Assert.Null(cables[1].RecordingId); // "CABLE In 16ch" has no recording side of its own
    }

    [Fact]
    public void Full_SpotifyPlays_HeliumOnlyHoldsASilentStream_SpotifyGetsTheSpareCable()
    {
        var plan = Plan(OwnerPc.Settings(), SoundModes.Full, Set(OwnerPc.Helium, OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify), twoPairs: true);

        var bus = plan.Runs[MixerService.MasterKey];
        Assert.Equal(RunKind.Bus, bus.Kind);
        Assert.Equal(OwnerPc.CableInput, bus.Cable);
        Assert.Equal(new[] { OwnerPc.Speaker }, bus.Outputs);

        var spotify = plan.Runs[OwnerPc.Spotify];
        Assert.Equal(RunKind.Cable, spotify.Kind);
        Assert.Equal(OwnerPc.CableA, spotify.Cable);
        Assert.Equal(new[] { OwnerPc.CableInput }, spotify.Outputs); // its effects first, then the master's in the bus

        // Silent: never moved to a cable; with no pair left it is armed by recording instead (its effects apply from its first sound).
        Assert.Equal(RunKind.Capture, plan.Runs[OwnerPc.Helium].Kind);
        Assert.Null(plan.Runs[OwnerPc.Helium].Cable);
    }

    [Fact]
    public void Full_ProgramWithOnlyASilentStream_IsNotMovedEvenWithAFreeCable()
    {
        var plan = Plan(OwnerPc.Settings(), SoundModes.Full, Set(OwnerPc.Helium), twoPairs: true);
        Assert.False(plan.Runs.ContainsKey(OwnerPc.Helium));
        Assert.Equal(EngineState.Waiting, plan.Blocked[OwnerPc.Helium].State);
        Assert.DoesNotContain(OwnerPc.CableA, plan.CablesInUse); // only the bus's pair is in use
    }

    [Fact]
    public void Full_BothHeard_TheSecondIsRecorded()
    {
        var plan = Plan(OwnerPc.Settings(), SoundModes.Full, Set(OwnerPc.Helium, OwnerPc.Spotify), sounding: Set(OwnerPc.Helium, OwnerPc.Spotify), twoPairs: true);
        Assert.Equal(OwnerPc.CableA, plan.Runs[OwnerPc.Helium].Cable); // settings order among programs that are heard
        Assert.Equal(RunKind.Capture, plan.Runs[OwnerPc.Spotify].Kind); // no pair left: Automatic records it
    }

    [Fact]
    public void Full_HeliumRunFailed_ItsCableGoesToSpotify_AndHeliumIsNotMovedAgain()
    {
        var plan = Plan(OwnerPc.Settings(), SoundModes.Full, Set(OwnerPc.Helium, OwnerPc.Spotify),
            sounding: Set(OwnerPc.Helium, OwnerPc.Spotify), failed: Set(OwnerPc.Helium), twoPairs: true);
        Assert.Equal(OwnerPc.CableA, plan.Runs[OwnerPc.Spotify].Cable);
        Assert.False(plan.Runs.ContainsKey(OwnerPc.Helium));
        Assert.DoesNotContain(OwnerPc.Helium, plan.BusRoutes);
        Assert.False(plan.Blocked.ContainsKey(OwnerPc.Helium)); // the engine shows its failure
    }

    [Fact]
    public void Full_BothPlay_TheProgramHoldingTheCableKeepsIt()
    {
        var plan = Plan(OwnerPc.Settings(), SoundModes.Full, Set(OwnerPc.Helium, OwnerPc.Spotify),
            sounding: Set(OwnerPc.Helium, OwnerPc.Spotify), holding: Set(OwnerPc.Spotify), twoPairs: true);
        Assert.Equal(OwnerPc.CableA, plan.Runs[OwnerPc.Spotify].Cable);
        Assert.Equal(RunKind.Capture, plan.Runs[OwnerPc.Helium].Kind);
    }

    [Fact]
    public void Full_SessionOfEvents_ProgramEndsUpProcessedIntoTheBus_NeverALoop()
    {
        var s = OwnerPc.Settings();
        s.Apps.First(a => a.Key == OwnerPc.Helium).FxOn = false; // Helium follows the master

        output.WriteLine("1. Spotify closed, Helium open:");
        var p1 = Plan(s, SoundModes.Full, Set(OwnerPc.Helium), twoPairs: true);
        Assert.Equal(EngineState.Waiting, p1.Blocked[OwnerPc.Spotify].State);
        Assert.Contains(OwnerPc.Helium, p1.BusRoutes);

        output.WriteLine("2. Spotify starts playing:");
        var p2 = Plan(s, SoundModes.Full, Set(OwnerPc.Helium, OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify), twoPairs: true);
        Assert.Equal(new[] { OwnerPc.CableInput }, p2.Runs[OwnerPc.Spotify].Outputs);
        Assert.Equal(OwnerPc.CableA, p2.Runs[OwnerPc.Spotify].Cable);

        output.WriteLine("3. Spotify paused (stream still open), its run is held:");
        var p3 = Plan(s, SoundModes.Full, Set(OwnerPc.Helium, OwnerPc.Spotify), holding: Set(OwnerPc.Spotify), twoPairs: true);
        Assert.True(p3.Runs[OwnerPc.Spotify].SameAs(p2.Runs[OwnerPc.Spotify])); // no restart on pause

        output.WriteLine("4. The master bus is given up (it heard nothing):");
        var p4 = Plan(s, SoundModes.Full, Set(OwnerPc.Helium, OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify),
            holding: Set(OwnerPc.Spotify), failed: Set(MixerService.MasterKey), twoPairs: true);
        Assert.False(p4.Runs.ContainsKey(MixerService.MasterKey));
        Assert.Null(p4.Bus);
        Assert.Empty(p4.BusRoutes);
        var spotify = p4.Runs[OwnerPc.Spotify];
        Assert.Equal(new[] { OwnerPc.Speaker }, spotify.Outputs); // the real speakers by name, never "the default" that may be a cable

        output.WriteLine("5. Spotify closes:");
        var p5 = Plan(s, SoundModes.Full, Set(OwnerPc.Helium), twoPairs: true);
        Assert.False(p5.Runs.ContainsKey(OwnerPc.Spotify));
        Assert.True(p5.Runs[MixerService.MasterKey].SameAs(p2.Runs[MixerService.MasterKey])); // the bus never restarts for a program
    }

    [Fact]
    public void Full_EffectsSwitchedOff_SpotifyFollowsTheBus()
    {
        var s = OwnerPc.Settings();
        s.Apps.First(a => a.Key == OwnerPc.Spotify).FxOn = false;
        var plan = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify));
        Assert.False(plan.Runs.ContainsKey(OwnerPc.Spotify));
        Assert.Contains(OwnerPc.Spotify, plan.BusRoutes);
    }

    [Fact]
    public void Fast_Unchanged_ProgramsRecordedAndMasterInEqualizerApo()
    {
        var s = OwnerPc.Settings();
        s.SoundMode = SoundModes.Fast.Id;
        var plan = Plan(s, SoundModes.Fast, Set(OwnerPc.Helium, OwnerPc.Spotify), failed: Set(OwnerPc.Helium));
        Assert.Null(plan.Bus);
        Assert.Contains(MixerService.MasterKey, plan.ToApo);
        Assert.Equal(RunKind.Capture, plan.Runs[OwnerPc.Spotify].Kind);
        Assert.Equal(new string?[] { null }, plan.Runs[OwnerPc.Spotify].Outputs);
        Assert.Equal(RunKind.Capture, plan.Runs[OwnerPc.Helium].Kind); // Fast keeps a failed run's spec: the engine shows the failure
        Assert.Empty(plan.CablesInUse);
    }

    // ---- armed ahead of the sound ----

    /// <summary>
    /// The owner's case: Spotify open and paused (silent session) is armed by recording before it plays; when it plays (and the engine
    /// reports its run as recording) the plan is the same run, so nothing restarts and nothing is left to start.
    /// </summary>
    [Fact]
    public void Full_OneVbCable_SilentOpenProgram_IsArmed_AndPlayingKeepsTheSameRun()
    {
        var s = OwnerPc.Settings();
        s.Apps.First(a => a.Key == OwnerPc.Helium).FxOn = false;
        output.WriteLine("1. Spotify open, paused:");
        var paused = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify));
        Assert.Equal(RunKind.Capture, paused.Runs[OwnerPc.Spotify].Kind);
        Assert.Equal(new[] { OwnerPc.CableInput }, paused.Runs[OwnerPc.Spotify].Outputs);

        output.WriteLine("2. Spotify plays:");
        var plays = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify), capturing: Set(OwnerPc.Spotify));
        Assert.True(plays.Runs[OwnerPc.Spotify].SameAs(paused.Runs[OwnerPc.Spotify]));
        Assert.True(plays.Runs[MixerService.MasterKey].SameAs(paused.Runs[MixerService.MasterKey]));
    }

    /// <summary>A program set to Virtual cable is never armed while silent (a silent session must not take a cable).</summary>
    [Fact]
    public void Full_SilentProgramOnVirtualCable_Waits()
    {
        var s = OwnerPc.Settings();
        s.Apps.First(a => a.Key == OwnerPc.Spotify).FxRoute = SoundModes.RouteCable.Id;
        var plan = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), twoPairs: true);
        Assert.False(plan.Runs.ContainsKey(OwnerPc.Spotify));
        Assert.Equal(EngineState.Waiting, plan.Blocked[OwnerPc.Spotify].State);
        Assert.DoesNotContain(OwnerPc.CableA, plan.CablesInUse);
    }

    /// <summary>A program set to Recording is armed while silent even with a free pair (it never uses one).</summary>
    [Fact]
    public void Full_SilentProgramOnRecording_IsArmed_EvenWithAFreePair()
    {
        var s = OwnerPc.Settings();
        s.Apps.First(a => a.Key == OwnerPc.Spotify).FxRoute = SoundModes.RouteCapture.Id;
        var plan = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), twoPairs: true);
        Assert.Equal(RunKind.Capture, plan.Runs[OwnerPc.Spotify].Kind);
        Assert.DoesNotContain(OwnerPc.CableA, plan.CablesInUse);
    }

    /// <summary>
    /// A replan caused by another program (it opens, starts, stops or closes) never changes a recorded program's run or the bus, so
    /// neither restarts (EffectsEngine keeps a run whose spec is the same).
    /// </summary>
    [Fact]
    public void Full_OtherProgramsChanging_NeverChangeARecordedRunOrTheBus()
    {
        var s = OwnerPc.Settings();
        var capturing = Set(OwnerPc.Spotify, OwnerPc.Helium);
        var first = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify), capturing: Set(OwnerPc.Spotify));
        var states = new[]
        {
            Plan(s, SoundModes.Full, Set(OwnerPc.Spotify, OwnerPc.Helium), sounding: Set(OwnerPc.Spotify), capturing: Set(OwnerPc.Spotify)), // Helium opens
            Plan(s, SoundModes.Full, Set(OwnerPc.Spotify, OwnerPc.Helium), sounding: Set(OwnerPc.Spotify, OwnerPc.Helium), capturing: capturing), // Helium plays
            Plan(s, SoundModes.Full, Set(OwnerPc.Spotify, OwnerPc.Helium), sounding: Set(OwnerPc.Helium), capturing: capturing), // Spotify pauses
            Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), capturing: capturing), // Helium closes
        };
        foreach (var plan in states)
        {
            Assert.True(plan.Runs[OwnerPc.Spotify].SameAs(first.Runs[OwnerPc.Spotify]));
            Assert.True(plan.Runs[MixerService.MasterKey].SameAs(first.Runs[MixerService.MasterKey]));
        }
    }

    /// <summary>
    /// Restore paths: the run ends (the engine then puts the original volume back) when the effects are switched off, when the last
    /// enabled effect is disabled, and when the Test found no way to record; in Fast mode also when the program closes. In Full control a
    /// closed program keeps its run (and its silence) so it is armed again the moment it reopens.
    /// </summary>
    [Fact]
    public void ArmedRun_EndsWhenItShould_AndIsKeptWhileClosedInFullControl()
    {
        var off = OwnerPc.Settings();
        off.Apps.First(a => a.Key == OwnerPc.Spotify).FxOn = false;
        Assert.False(Plan(off, SoundModes.Full, Set(OwnerPc.Spotify), capturing: Set(OwnerPc.Spotify)).Runs.ContainsKey(OwnerPc.Spotify));

        var disabled = OwnerPc.Settings();
        disabled.Apps.First(a => a.Key == OwnerPc.Spotify).Fx.ForEach(f => f.Enabled = false);
        Assert.False(Plan(disabled, SoundModes.Full, Set(OwnerPc.Spotify), capturing: Set(OwnerPc.Spotify)).Runs.ContainsKey(OwnerPc.Spotify));

        var untestable = Plan(OwnerPc.Settings(), SoundModes.Full, Set(OwnerPc.Spotify), captureWorks: false);
        Assert.False(untestable.Runs.ContainsKey(OwnerPc.Spotify));

        var fast = OwnerPc.Settings();
        fast.SoundMode = SoundModes.Fast.Id;
        Assert.Equal(RunKind.Capture, Plan(fast, SoundModes.Fast, Set(OwnerPc.Spotify)).Runs[OwnerPc.Spotify].Kind); // open, silent: armed
        Assert.False(Plan(fast, SoundModes.Fast, None, capturing: Set(OwnerPc.Spotify)).Runs.ContainsKey(OwnerPc.Spotify)); // closed

        var closed = Plan(OwnerPc.Settings(), SoundModes.Full, None, capturing: Set(OwnerPc.Spotify));
        Assert.Equal(RunKind.Capture, closed.Runs[OwnerPc.Spotify].Kind);
    }

    /// <summary>
    /// The owner's real mixer.json, read only, when this PC has one (other PCs skip the check): the plan for the programs it holds,
    /// with Spotify heard and every other program holding a silent stream.
    /// </summary>
    [Fact]
    public void OwnersRealSettings_WhenPresent_PlanHasNoLoopAndNeverUsesCable16()
    {
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniControl", "mixer.json");
        if (!File.Exists(file)) return;
        var s = JsonSerializer.Deserialize<MixerSettings>(File.ReadAllText(file))!;
        if (SoundModes.Find(s.SoundMode) != SoundModes.Full || s.Apps.FirstOrDefault(a => a.Name == "Spotify") is not { } spotify) return;
        var all = s.Apps.Select(a => a.Key).ToHashSet();
        var plan = Plan(s, SoundModes.Full, all, sounding: Set(spotify.Key));
        output.WriteLine($"(Spotify = {spotify.Key})");
        // One VB-CABLE: the bus holds the pair and nothing ever uses "CABLE In 16ch"; Spotify's effects are recorded (Automatic or
        // Recording) or wait for a second cable (Virtual cable).
        if (!spotify.FxOn || !spotify.Fx.Any(f => f.Enabled)) return;
        if (SoundModes.RouteOf(spotify.FxRoute) == SoundModes.RouteCable) Assert.Equal(EngineState.Unavailable, plan.Blocked[spotify.Key].State);
        else Assert.Equal(RunKind.Capture, plan.Runs[spotify.Key].Kind);
    }
}
