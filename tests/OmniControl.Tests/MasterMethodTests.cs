using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Apo;
using Xunit.Abstractions;

namespace OmniControl.Tests;

/// <summary>
/// The master's method in Full control (cable bus or Equalizer APO on the speakers) on the owner's PC: the plans of the help table's
/// setups (the free cable for a program, a combined microphone, combined speakers, a program with Equalizer APO on a second cable),
/// what goes to Equalizer APO (through a fake store: nothing is written), the default-device rule, and the Method pop-down's entries.
/// </summary>
public class MasterMethodTests(ITestOutputHelper output)
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static HashSet<string> Set(params string[] keys) => keys.ToHashSet();

    /// <summary>The owner's settings in Full control with the master's method set to <paramref name="method"/>.</summary>
    private static MixerSettings Settings(EffectsRoute method)
    {
        var s = OwnerPc.Settings();
        s.MasterMethod = method.Id;
        return s;
    }

    private RoutePlan Plan(MixerSettings s, SoundMode mode, IReadOnlySet<string> sounding, bool twoPairs = false, ApoState? apo = null,
        bool speakerDefault = true)
    {
        var support = (twoPairs ? OwnerPc.SupportTwoPairs : OwnerPc.Support) with { Apo = apo ?? OwnerPc.ApoOnSpeaker() };
        var playback = twoPairs
            ? speakerDefault ? OwnerPc.PlaybackTwoPairsSpeakerDefault : OwnerPc.PlaybackTwoPairs
            : speakerDefault ? OwnerPc.PlaybackSpeakerDefault : OwnerPc.Playback;
        var plan = RoutePlanner.Make(new PlanInput(mode, support, true, s, sounding, sounding, None, playback,
            twoPairs ? OwnerPc.RecordingTwoPairs : OwnerPc.Recording, None));
        output.WriteLine(PlanPrinter.Print(plan));
        output.WriteLine($"master: {plan.MasterMethod}, setup {SoundModes.SetupNumber(plan.Help)}, to APO: [{string.Join(", ", plan.ToApo)}]");
        output.WriteLine("--");
        NoLoops(plan, playback);
        return plan;
    }

    /// <summary>No run plays into the cable it uses; without the bus no program run takes the cable that is Windows' default; "CABLE In 16ch" is never used.</summary>
    private static void NoLoops(RoutePlan plan, IReadOnlyList<AudioDevice> playback)
    {
        var defaultId = playback.First(d => d.IsDefault).Id;
        foreach (var run in plan.Runs.Values.Where(r => r.Kind != RunKind.Mic && r.Cable is not null))
        {
            Assert.DoesNotContain(run.Cable, run.Outputs);
            Assert.NotEqual(OwnerPc.Cable16, run.Cable);
            if (plan.Bus is null && run.Kind is RunKind.Cable or RunKind.ApoCable) Assert.NotEqual(defaultId, run.Cable);
        }
    }

    // ---- the plans of the setups ----

    /// <summary>Setup 2: the master in Equalizer APO on the speakers frees the cable; the program that plays takes it and plays on the speakers.</summary>
    [Fact]
    public void Setup2_MasterApo_TheCableGoesToTheProgramThatPlays()
    {
        var plan = Plan(Settings(SoundModes.MasterApo), SoundModes.Full, Set(OwnerPc.Spotify, OwnerPc.Helium));
        Assert.Equal(RunMethod.Apo, plan.MasterMethod);
        Assert.Null(plan.Bus);
        Assert.False(plan.Runs.ContainsKey(MixerService.MasterKey));
        Assert.Contains(MixerService.MasterKey, plan.ToApo);
        Assert.Equal(OwnerPc.Speaker, plan.MasterApoDevice);
        Assert.Empty(plan.BusRoutes);
        var cable = plan.Runs.Values.Single(r => r.Kind == RunKind.Cable);
        Assert.Equal(OwnerPc.CableInput, cable.Cable);
        Assert.Equal(new[] { OwnerPc.Speaker }, cable.Outputs); // its replay passes the master's APO chain once, on the speakers
        var other = plan.Runs.Values.Single(r => r.Kind == RunKind.Capture);
        Assert.Equal(new[] { OwnerPc.Speaker }, other.Outputs); // the other program by recording, also on the speakers
        Assert.Equal(2, SoundModes.SetupNumber(plan.Help));
    }

    /// <summary>The moment after the bus let go the cable is still Windows' default: no program run may take it until the speakers are the default again.</summary>
    [Fact]
    public void MasterApo_TheCableStillTheDefault_IsGivenToNoProgram()
    {
        var plan = Plan(Settings(SoundModes.MasterApo), SoundModes.Full, Set(OwnerPc.Spotify), speakerDefault: false);
        Assert.DoesNotContain(plan.Runs.Values, r => r.Kind == RunKind.Cable);
        Assert.Equal(RunKind.Capture, plan.Runs[OwnerPc.Spotify].Kind);
        Assert.Equal(new[] { OwnerPc.Speaker }, plan.Runs[OwnerPc.Spotify].Outputs);
    }

    /// <summary>Setup 3: a combined microphone takes the free cable (apps record "CABLE Output"); the programs are recorded.</summary>
    [Fact]
    public void Setup3_MasterApo_CombinedMicrophoneOnTheFreeCable()
    {
        var s = Settings(SoundModes.MasterApo);
        var mic = new CombinedDevice { Name = "Both mics", Kind = CombinedKind.Microphones, Members = { OwnerPc.MicArray, OwnerPc.StereoMix } };
        s.Combined.Add(mic);
        var plan = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify));
        Assert.Equal(RunKind.Mic, plan.Runs[mic.Key].Kind);
        Assert.Equal(OwnerPc.CableInput, plan.Runs[mic.Key].Cable);
        Assert.Equal(RunKind.Capture, plan.Runs[OwnerPc.Spotify].Kind);
        Assert.Equal(3, SoundModes.SetupNumber(plan.Help));
    }

    /// <summary>Setup 4: combined speakers take the free cable and play it on their members; the master stays on one speaker in Equalizer APO.</summary>
    [Fact]
    public void Setup4_MasterApo_CombinedSpeakerOnTheFreeCable()
    {
        var s = Settings(SoundModes.MasterApo);
        var room = new CombinedDevice { Name = "Room", Kind = CombinedKind.Speakers, Members = { OwnerPc.Speaker } };
        s.Combined.Add(room);
        s.Apps.First(a => a.Key == OwnerPc.Spotify).PlaysOn.Add(room.Key);
        var plan = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify));
        var fan = plan.Runs[room.Key];
        Assert.Equal(RunKind.FanOut, fan.Kind);
        Assert.Equal(OwnerPc.CableInput, fan.Cable);
        Assert.Equal(new[] { OwnerPc.Speaker }, fan.Outputs);
        Assert.Equal(OwnerPc.CableInput, plan.Direct[OwnerPc.Spotify]); // the program that picked it plays into its cable
        Assert.Equal(RunKind.Capture, plan.Runs[OwnerPc.Spotify].Kind); // its effects by recording, replayed into the combined device
        Assert.Contains(MixerService.MasterKey, plan.ToApo);
        Assert.Equal(4, SoundModes.SetupNumber(plan.Help));
    }

    /// <summary>Setup 5: nothing uses the cable (no program with effects plays); the master without effects plays straight on the speakers.</summary>
    [Fact]
    public void Setup5_MasterApo_NothingOnTheCable_NoEffects_IsDirect()
    {
        var s = Settings(SoundModes.MasterApo);
        s.MasterFxOn = false;
        var plan = Plan(s, SoundModes.Full, None);
        Assert.Empty(plan.ToApo); // nothing to write, nothing needs installing
        Assert.Equal(StatusKind.Direct, plan.Blocked[MixerService.MasterKey].Kind);
        Assert.Equal("Direct", plan.Blocked[MixerService.MasterKey].Brief);
        Assert.Equal(5, SoundModes.SetupNumber(plan.Help));
    }

    /// <summary>Setup 1: Automatic with a cable allowed is today's cable bus.</summary>
    [Fact]
    public void Setup1_Automatic_IsTheCableBus()
    {
        var plan = Plan(Settings(SoundModes.MasterAutomatic), SoundModes.Full, Set(OwnerPc.Spotify), speakerDefault: false);
        Assert.Equal(RunMethod.Bus, plan.MasterMethod);
        Assert.Equal(RunKind.Bus, plan.Runs[MixerService.MasterKey].Kind);
        Assert.DoesNotContain(MixerService.MasterKey, plan.ToApo);
        Assert.Equal(1, SoundModes.SetupNumber(plan.Help));
    }

    /// <summary>Setup 6: the bus on the first cable, a combined microphone on the second pair.</summary>
    [Fact]
    public void Setup6_Bus_SecondPairForACombinedMicrophone()
    {
        var s = Settings(SoundModes.MasterBus);
        var mic = new CombinedDevice { Name = "Both mics", Kind = CombinedKind.Microphones, Members = { OwnerPc.MicArray } };
        s.Combined.Add(mic);
        var plan = Plan(s, SoundModes.Full, None, twoPairs: true, speakerDefault: false);
        Assert.Equal(OwnerPc.CableInput, plan.Bus!.Cable);
        Assert.Equal(OwnerPc.CableA, plan.Runs[mic.Key].Cable);
        Assert.Equal(6, SoundModes.SetupNumber(plan.Help));
    }

    /// <summary>Setup 7: a program set to "Equalizer APO on a cable" gets the second pair that has Equalizer APO on it; its chain goes to that cable's Device section.</summary>
    [Fact]
    public void Setup7_ProgramOnACableWithEqualizerApo()
    {
        var s = Settings(SoundModes.MasterApo);
        s.Apps.First(a => a.Key == OwnerPc.Spotify).FxRoute = SoundModes.RouteApoCable.Id;
        var plan = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), twoPairs: true, apo: OwnerPc.ApoOnSpeaker(onCableA: true));
        var run = plan.Runs[OwnerPc.Spotify];
        Assert.Equal(RunKind.ApoCable, run.Kind);
        Assert.Equal(OwnerPc.CableA, run.Cable);
        Assert.Equal(new[] { OwnerPc.CableAOutput }, run.Sources); // Equalizer APO's output arrives on the recording side
        Assert.Equal(new[] { OwnerPc.Speaker }, run.Outputs);
        Assert.Contains(OwnerPc.Spotify, plan.ToApo);
        Assert.Equal(7, SoundModes.SetupNumber(plan.Help));
    }

    /// <summary>"Equalizer APO on a cable" is honest when it can't run: one pair = needs a second pair; no Equalizer APO on a cable = says so.</summary>
    [Fact]
    public void ApoCable_SaysWhatIsMissing()
    {
        var s = Settings(SoundModes.MasterApo);
        s.Apps.First(a => a.Key == OwnerPc.Spotify).FxRoute = SoundModes.RouteApoCable.Id;
        var one = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify));
        Assert.Equal("Needs a second cable pair. Playing without effects.", one.Blocked[OwnerPc.Spotify].Text);
        var two = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), twoPairs: true);
        Assert.Equal("Equalizer APO is not on a cable. Playing without effects.", two.Blocked[OwnerPc.Spotify].Text);
        Assert.False(SoundModes.OfferOf(SoundModes.RouteApoCable, new RouteFacts(CableOffer.None, null, RunMethod.None, two.ApoCableWhy)).Available);
    }

    /// <summary>Fast mode: setup 8, the master in Equalizer APO on Windows' default device (no device named by the plan).</summary>
    [Fact]
    public void Setup8_Fast_MasterInApoOnTheDefault()
    {
        var plan = Plan(OwnerPc.Settings(), SoundModes.Fast, None);
        Assert.Equal(RunMethod.Apo, plan.MasterMethod);
        Assert.Contains(MixerService.MasterKey, plan.ToApo);
        Assert.Null(plan.MasterApoDevice);
        Assert.Equal(8, SoundModes.SetupNumber(plan.Help));
    }

    // ---- Automatic and the Method pop-down ----

    [Fact]
    public void Automatic_WithNoCable_UsesEqualizerApo_WhenItRunsOnTheSpeakers_ElseTheBusSaysWhatIsMissing()
    {
        var facts = new MasterFacts(true, 0, StatusKind.NeedsCable, null, false, true);
        Assert.Equal(RunMethod.Apo, SoundModes.MasterMethodOf(SoundModes.MasterAutomatic, facts));
        Assert.Equal(RunMethod.Bus, SoundModes.MasterMethodOf(SoundModes.MasterAutomatic, facts with { ApoWhy = "Equalizer APO is not on this device" }));
        Assert.Equal(RunMethod.Apo, SoundModes.MasterMethodOf(SoundModes.MasterAutomatic, facts with { ApoWhy = "Equalizer APO is not on this device", HasEffects = false }));
        Assert.Equal(RunMethod.Bus, SoundModes.MasterMethodOf(SoundModes.MasterAutomatic, facts with { CablePairs = 1 }));
        Assert.Equal(RunMethod.Bus, SoundModes.MasterMethodOf(SoundModes.MasterAutomatic, facts with { SeveralOutputs = true }));
        Assert.Equal(RunMethod.Apo, SoundModes.MasterMethodOf(SoundModes.MasterBus, facts with { Full = false })); // Fast: always APO
    }

    [Fact]
    public void MethodPopDown_GreysWhatCantRun_WithTheReason()
    {
        var facts = new MasterFacts(true, 0, StatusKind.NeedsCable, "Equalizer APO is not on this device", false, true);
        var bus = SoundModes.MasterOfferOf(SoundModes.MasterBus, facts, RunMethod.Bus);
        Assert.False(bus.Available);
        Assert.Equal("Not available: needs VB-CABLE", bus.Description);
        var apo = SoundModes.MasterOfferOf(SoundModes.MasterApo, facts, RunMethod.Bus);
        Assert.False(apo.Available);
        Assert.Equal("Not available: Equalizer APO is not on this device", apo.Description);
        Assert.True(SoundModes.MasterOfferOf(SoundModes.MasterApo, facts with { HasEffects = false }, RunMethod.Bus).Available); // nothing needs installing
        Assert.False(SoundModes.MasterOfferOf(SoundModes.MasterApo, facts with { ApoWhy = null, SeveralOutputs = true }, RunMethod.Bus).Available);
        Assert.Equal("Currently: Equalizer APO", SoundModes.MasterOfferOf(SoundModes.MasterAutomatic, facts, RunMethod.Apo).Description);
        Assert.Equal("Currently: cable bus", SoundModes.MasterOfferOf(SoundModes.MasterAutomatic, facts, RunMethod.Bus).Description);
    }

    /// <summary>The planner's master facts on the owner's PC: Equalizer APO is checked on the real speakers, not on the cable that is the default while the bus runs.</summary>
    [Fact]
    public void MasterFacts_CheckTheRealSpeakers_NotTheBusCable()
    {
        var plan = Plan(Settings(SoundModes.MasterBus), SoundModes.Full, None, speakerDefault: false, apo: OwnerPc.ApoOnSpeaker(defaultId: OwnerPc.CableInput));
        Assert.Null(plan.Master.ApoWhy);
        var none = Plan(Settings(SoundModes.MasterBus), SoundModes.Full, None, speakerDefault: false, apo: ApoState.None);
        Assert.Equal("Equalizer APO is not installed", none.Master.ApoWhy);
    }

    // ---- what goes to Equalizer APO (a fake store: nothing is written) ----

    private sealed class FakeStore : IApoConfigStore
    {
        public Dictionary<string, string> Files { get; } = new();
        public List<string> Removed { get; } = new();

        public string? Apply(string folder, string text)
        {
            Files[folder] = text;
            return null;
        }

        public string? Remove(string folder)
        {
            Removed.Add(folder);
            Files.Remove(folder);
            return null;
        }
    }

    private static IReadOnlyDictionary<string, EngineStatus> Send(ApoChains chains, RoutePlan plan, MixerSettings s, ApoState apo) =>
        chains.Apply(ApoChains.RowsFor(plan, apo, key => key is null ? s.MasterFx : s.Apps.First(a => a.Key == key).Fx, key => key), apo);

    private static int Count(string text, string part) => text.Split(part).Length - 1;

    [Fact]
    public void ApoWriter_MasterSectionOnTheSpeakers_Once_ThenRemovedWhenTheBusTakesOver()
    {
        var apo = OwnerPc.ApoOnSpeaker(onCableA: true);
        var store = new FakeStore();
        var chains = new ApoChains(_ => { }, store);
        var s = Settings(SoundModes.MasterApo);
        s.Apps.First(a => a.Key == OwnerPc.Spotify).FxRoute = SoundModes.RouteApoCable.Id;
        var plan = Plan(s, SoundModes.Full, Set(OwnerPc.Spotify), twoPairs: true, apo: apo);

        var statuses = Send(chains, plan, s, apo);
        var text = store.Files[OwnerPc.ApoFolder];
        output.WriteLine(text);
        Assert.Equal(1, Count(text, $"Device: {ApoIds.DeviceKeyOf(OwnerPc.Speaker)}")); // the master's chain once, on the speakers only
        Assert.Equal(1, Count(text, $"Device: {ApoIds.DeviceKeyOf(OwnerPc.CableA)}"));  // the program's on its own cable
        Assert.Equal(2, Count(text, "Device: "));
        Assert.Equal(EngineState.Running, statuses[MixerService.MasterKey].State);

        var bus = Plan(Settings(SoundModes.MasterBus), SoundModes.Full, None, twoPairs: true, apo: apo, speakerDefault: false);
        Assert.Empty(bus.ToApo);
        Send(chains, bus, s, apo);
        Assert.Contains(OwnerPc.ApoFolder, store.Removed); // leaving Equalizer APO takes OmniControl's include out
        Assert.Empty(store.Files);
    }

    [Fact]
    public void ApoWriter_EqualizerApoNotOnTheSpeakers_WritesNothing_AndSaysNotApplied()
    {
        var store = new FakeStore();
        var chains = new ApoChains(_ => { }, store);
        var s = Settings(SoundModes.MasterApo);
        var apo = ApoState.None with { };
        var plan = Plan(s, SoundModes.Full, None, apo: apo);
        var status = Send(chains, plan, s, apo)[MixerService.MasterKey];
        Assert.Equal(StatusKind.ApoNotApplied, status.Kind);
        Assert.Equal("Equalizer APO not applied", status.Brief);
        Assert.Empty(store.Files);
    }

    // ---- Windows' default device ----

    [Fact]
    public void DefaultDevice_GoesBack_WhenTheMasterLeavesTheBus_OnlyWhileTheCableIsStillTheDefault()
    {
        var bus = new BusPlan(OwnerPc.CableInput, new MasterDevice.Choice(OwnerPc.Speaker, false));
        Assert.Equal(BusDefault.Step.Take, BusDefault.StepFor(bus, true, true, OwnerPc.CableInput));
        Assert.Equal(BusDefault.Step.Keep, BusDefault.StepFor(bus, true, false, OwnerPc.CableInput)); // restarting on its own cable
        var apo = Plan(Settings(SoundModes.MasterApo), SoundModes.Full, None, speakerDefault: false);
        Assert.Equal(BusDefault.Step.Give, BusDefault.StepFor(apo.Bus, false, false, OwnerPc.CableInput));
        Assert.True(BusDefault.PutsBack(OwnerPc.CableInput, OwnerPc.CableInput));
        Assert.False(BusDefault.PutsBack(OwnerPc.Speaker, OwnerPc.CableInput)); // the user chose a device meanwhile: kept
        Assert.False(BusDefault.PutsBack(OwnerPc.CableInput, string.Empty));    // OmniControl never made it the default
    }
}
