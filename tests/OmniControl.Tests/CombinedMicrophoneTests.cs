using System.Text.Json;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.Core.Engine;
using Xunit.Abstractions;

namespace OmniControl.Tests;

/// <summary>Combined microphones on the owner's PC (one VB-CABLE pair): the planner in both modes, cable pairs, member lists, stored levels.</summary>
public class CombinedMicrophoneTests(ITestOutputHelper output)
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static HashSet<string> Set(params string[] keys) => keys.ToHashSet();

    private static (MixerSettings Settings, CombinedDevice Mic) WithCombinedMic(SoundMode mode, string remembered = "")
    {
        var s = OwnerPc.Settings();
        s.SoundMode = mode.Id;
        var mic = new CombinedDevice { Name = "Both mics", Kind = CombinedKind.Microphones, Members = { OwnerPc.MicArray, OwnerPc.StereoMix }, Cable = remembered };
        s.Combined.Add(mic);
        return (s, mic);
    }

    private RoutePlan Plan(MixerSettings s, SoundMode mode, IReadOnlySet<string> playing, IReadOnlySet<string>? sounding = null,
        IReadOnlySet<string>? failed = null, IReadOnlyList<AudioDevice>? recording = null)
    {
        var plan = RoutePlanner.Make(new PlanInput(mode, OwnerPc.Support, true, s, playing, sounding ?? None, None,
            OwnerPc.Playback, recording ?? OwnerPc.Recording, failed ?? None));
        output.WriteLine(PlanPrinter.Print(plan));
        output.WriteLine("--");
        return plan;
    }

    [Fact]
    public void Fast_CombinedMicrophone_RunsIntoCableInputWithBothMics_ProgramsUnchanged()
    {
        var (s, mic) = WithCombinedMic(SoundModes.Fast);
        var plan = Plan(s, SoundModes.Fast, Set(OwnerPc.Helium, OwnerPc.Spotify));

        var run = plan.Runs[mic.Key];
        Assert.Equal(RunKind.Mic, run.Kind);
        Assert.Equal(OwnerPc.CableInput, run.Cable);
        Assert.Equal(new[] { OwnerPc.MicArray, OwnerPc.StereoMix }, run.Sources);
        Assert.Equal(OwnerPc.CableInput, plan.NewCables[mic.Key]);
        Assert.All(plan.Runs.Values.Where(r => r.Key != mic.Key), r => Assert.Null(r.Cable)); // nothing else uses a cable
        Assert.Contains(OwnerPc.Cable16, plan.CablesInUse); // the pair's other input is held: it would reach CABLE Output

        var without = OwnerPc.Settings();
        without.SoundMode = SoundModes.Fast.Id;
        var before = Plan(without, SoundModes.Fast, Set(OwnerPc.Helium, OwnerPc.Spotify));
        foreach (var key in new[] { OwnerPc.Helium, OwnerPc.Spotify })
            Assert.True(plan.Runs[key].SameAs(before.Runs[key]));
        Assert.Equal(before.ToApo, plan.ToApo);
    }

    [Fact]
    public void Full_CombinedMicrophone_WaitsForAnotherCable_TheBusKeepsCableInput()
    {
        var (s, mic) = WithCombinedMic(SoundModes.Full);
        var plan = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify));

        var blocked = plan.Blocked[mic.Key];
        Assert.Equal(EngineState.Unavailable, blocked.State);
        Assert.Equal(SoundModes.NeedsAnotherCable, blocked.Text);
        Assert.Equal(SoundModes.BusHoldsCable("CABLE"), blocked.Detail);
        Assert.Equal(OwnerPc.CableInput, plan.Runs[MixerService.MasterKey].Cable);
        // The bus holds the whole pair: "CABLE In 16ch" could be neither played into nor recorded while "CABLE Input" was in use (measured).
        // Spotify (Automatic) is recorded instead and plays into the bus; it takes no cable.
        Assert.Equal(RunKind.Capture, plan.Runs[OwnerPc.Spotify].Kind);
        Assert.Null(plan.Runs[OwnerPc.Spotify].Cable);
        Assert.False(plan.Runs.ContainsKey(mic.Key));
    }

    [Fact]
    public void Full_BusGivenUp_MicrophoneHoldsThePair_NothingElseUsesCable16()
    {
        var (s, mic) = WithCombinedMic(SoundModes.Full);
        var plan = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify), failed: Set(MixerService.MasterKey));

        Assert.Equal(OwnerPc.CableInput, plan.Runs[mic.Key].Cable);
        Assert.Contains(OwnerPc.Cable16, plan.CablesInUse);
        Assert.DoesNotContain(plan.Runs.Values, r => r.Cable == OwnerPc.Cable16);
        // Spotify is recorded and plays on the real speakers by id: never "CABLE In 16ch", which would leak into the microphone.
        Assert.Equal(RunKind.Capture, plan.Runs[OwnerPc.Spotify].Kind);
        Assert.Equal(new[] { OwnerPc.Speaker }, plan.Runs[OwnerPc.Spotify].Outputs);
    }

    [Fact]
    public void Full_BusGivenUpThenRecovered_TheBusGetsItsPairBack()
    {
        var (s, mic) = WithCombinedMic(SoundModes.Full);
        var given = Plan(s, SoundModes.Full, None, failed: Set(MixerService.MasterKey));
        mic.Cable = given.NewCables[mic.Key]; // what the engine remembers
        Assert.Equal(OwnerPc.CableInput, mic.Cable);

        var recovered = Plan(s, SoundModes.Full, None);
        Assert.Equal(OwnerPc.CableInput, recovered.Runs[MixerService.MasterKey].Cable);
        Assert.Equal(SoundModes.NeedsAnotherCable, recovered.Blocked[mic.Key].Text);
        Assert.NotEqual(OwnerPc.Cable16, recovered.Runs[MixerService.MasterKey].Cable);
    }

    [Fact]
    public void MemberUnplugged_RunKeepsTheOther_BothUnplugged_SaysSo()
    {
        var (s, mic) = WithCombinedMic(SoundModes.Fast);
        var one = Plan(s, SoundModes.Fast, None, recording: OwnerPc.Recording.Where(d => d.Id != OwnerPc.StereoMix).ToList());
        Assert.Equal(new[] { OwnerPc.MicArray }, one.Runs[mic.Key].Sources);

        var none = Plan(s, SoundModes.Fast, None, recording: OwnerPc.Recording.Where(d => d.Id == OwnerPc.CableOutput).ToList());
        Assert.False(none.Runs.ContainsKey(mic.Key));
        Assert.Equal(SoundModes.NoMicrophonePluggedIn, none.Blocked[mic.Key].Text);
    }

    [Fact]
    public void Members_NeverACableOrItself()
    {
        var candidates = CombinedDevice.Candidates(OwnerPc.Recording).Select(d => d.Id).ToList();
        Assert.Equal(new[] { OwnerPc.StereoMix, OwnerPc.MicArray }, candidates); // CABLE Output (its own recording side) is never offered

        var (s, mic) = WithCombinedMic(SoundModes.Fast);
        mic.Members.Add(OwnerPc.CableOutput); // a stored member that is a cable is left out of the run
        var plan = Plan(s, SoundModes.Fast, None);
        Assert.DoesNotContain(OwnerPc.CableOutput, plan.Runs[mic.Key].Sources);
    }

    [Fact]
    public void Cables_OwnerPc_OnePairWithTwoInputs()
    {
        var pairs = VirtualCables.Pairs(OwnerPc.Support.Cables);
        var pair = Assert.Single(pairs);
        Assert.Equal(OwnerPc.CableOutput, pair.RecordingId);
        Assert.Equal(new[] { OwnerPc.CableInput, OwnerPc.Cable16 }, pair.PlaybackIds);
    }

    [Fact]
    public void OldMixerJson_CombinedMicrophoneWithoutLevels_Loads_LevelsAre100()
    {
        const string json = """
        {"SoundMode":"fast","Combined":[{"Id":"abc","Name":"Desk mics","Kind":1,"Members":["m1","m2"],"Cable":"","FxOn":false,"Fx":[],"Volume":80,"Muted":false}]}
        """;
        var s = JsonSerializer.Deserialize<MixerSettings>(json)!;
        var c = Assert.Single(s.Combined);
        Assert.Equal(CombinedKind.Microphones, c.Kind);
        Assert.Equal(100, c.LevelOf("m1"));
        Assert.False(c.IsMuted("m2"));
        Assert.Equal(1f, c.GainOf("m2"));
        Assert.Equal(80, c.Volume);
    }

    [Fact]
    public void Full_RemovingTheCombinedMicrophone_FreesNothingWhileTheBusHoldsThePair_OtherRowsKeepTheirTrueStatus()
    {
        var (s, mic) = WithCombinedMic(SoundModes.Full);
        var second = new CombinedDevice { Name = "Other mics", Kind = CombinedKind.Microphones, Members = { OwnerPc.MicArray } };
        s.Combined.Add(second);
        var before = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify));
        Assert.Equal(SoundModes.NeedsAnotherCable, before.Blocked[mic.Key].Text);
        Assert.Equal(SoundModes.NeedsAnotherCable, before.Blocked[second.Key].Text);

        s.Combined.Remove(mic); // MixerService.RemoveCombined
        var after = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), sounding: Set(OwnerPc.Spotify));
        Assert.DoesNotContain(mic.Key, after.Blocked.Keys); // nothing of the removed one is left in the plan
        Assert.Equal(OwnerPc.CableInput, after.Runs[MixerService.MasterKey].Cable); // the bus, not the microphone, held the only pair
        Assert.Equal(SoundModes.NeedsAnotherCable, after.Blocked[second.Key].Text); // and still does: that answer is true
    }

    [Fact]
    public void Full_BusGivenUpMicrophoneHoldsPair_RemovingIt_GivesThePairBackToTheBus_AndTheNextMicrophoneWaits()
    {
        var (s, mic) = WithCombinedMic(SoundModes.Full);
        var second = new CombinedDevice { Name = "Other mics", Kind = CombinedKind.Microphones, Members = { OwnerPc.MicArray } };
        s.Combined.Add(second);
        var given = Plan(s, SoundModes.Full, None, failed: Set(MixerService.MasterKey));
        Assert.Equal(OwnerPc.CableInput, given.Runs[mic.Key].Cable);
        Assert.Equal(SoundModes.NeedsAnotherCable, given.Blocked[second.Key].Text);
        mic.Cable = given.NewCables[mic.Key];

        s.Combined.Remove(mic); // the engine also forgets its failures on a routing change
        var after = Plan(s, SoundModes.Full, None);
        Assert.Equal(OwnerPc.CableInput, after.Runs[MixerService.MasterKey].Cable);
        Assert.False(after.Runs.ContainsKey(second.Key));
    }

    [Fact]
    public void RememberedCableOfABlockedCombinedDevice_IsNotAHold()
    {
        // A combined speaker in Fast mode runs nothing but keeps the cable it once had: that cable must not count as taken.
        var (s, _) = WithCombinedMic(SoundModes.Fast);
        var speakers = new CombinedDevice { Name = "Both speakers", Kind = CombinedKind.Speakers, Members = { OwnerPc.Speaker }, Cable = OwnerPc.CableInput };
        s.Combined.Clear();
        s.Combined.Add(speakers);
        var plan = Plan(s, SoundModes.Fast, None);
        Assert.Empty(plan.CablesInUse);
        Assert.Equal(SoundModes.CombinedNeedsFull, plan.Blocked[speakers.Key].Text);
    }
}
