using System.Text.Json;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Apo;
using Xunit.Abstractions;

namespace OmniControl.Tests;

/// <summary>
/// Virtual cables of any maker: detection and pairing (<see cref="VirtualCables.Scan"/>), the user's switches and marked pairs
/// (<see cref="VirtualCables.Usable"/>, as <see cref="EffectsSupportService"/> applies them), and the planner giving out only allowed cables:
/// the master bus the first, programs the others, "No free cable" when none is left.
/// </summary>
public class VirtualCableTests(ITestOutputHelper output)
{
    private const string HiFiIn = "{0.0.0.00000000}.{hifi-in}", HiFiOut = "{0.0.1.00000000}.{hifi-out}";
    private const string LineIn = "{0.0.0.00000000}.{vac-line1-in}", LineOut = "{0.0.1.00000000}.{vac-line1-out}";
    private const string VmIn = "{0.0.0.00000000}.{voicemeeter-in}";
    private const string AcmeIn = "{0.0.0.00000000}.{acme-in}", AcmeOut = "{0.0.1.00000000}.{acme-out}";

    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    /// <summary>The owner's PC plus a VB-Audio Hi-Fi Cable and Virtual Audio Cable's "Line 1" (three makers' cables).</summary>
    private static readonly AudioDevice[] ThreeMakersPlayback = OwnerPc.Playback.Concat(new[]
    {
        new AudioDevice(HiFiIn, "Hi-Fi Cable Input (VB-Audio Hi-Fi Cable)", false),
        new AudioDevice(LineIn, "Line 1 (Virtual Audio Cable)", false),
    }).ToArray();

    private static readonly AudioDevice[] ThreeMakersRecording = OwnerPc.Recording.Concat(new[]
    {
        new AudioDevice(HiFiOut, "Hi-Fi Cable Output (VB-Audio Hi-Fi Cable)", false),
        new AudioDevice(LineOut, "Line 1 (Virtual Audio Cable)", false),
    }).ToArray();

    private static AudioEffectsSupport Support(IReadOnlyList<AudioDevice> playback, IReadOnlyList<AudioDevice> recording,
        IReadOnlyList<CableMark>? marks = null, IReadOnlySet<string>? forbidden = null)
    {
        var found = VirtualCables.Scan(playback, recording, marks ?? Array.Empty<CableMark>());
        return new AudioEffectsSupport(true, VirtualCables.Usable(found, forbidden ?? None), ApoState.None, OwnerPc.CableInput) { Found = found };
    }

    private RoutePlan Plan(AudioEffectsSupport support, IReadOnlyList<AudioDevice> playback, IReadOnlyList<AudioDevice> recording,
        MixerSettings s, params string[] sounding)
    {
        var plan = RoutePlanner.Make(new PlanInput(support, true, s, sounding.ToHashSet(), sounding.ToHashSet(), None,
            playback, recording, None));
        output.WriteLine(PlanPrinter.Print(plan));
        return plan;
    }

    // ---- detection ----

    /// <summary>The owner's PC: one VB-CABLE pair, its two inputs ("CABLE Input", "CABLE In 16ch") one cable, identified by "CABLE Output".</summary>
    [Fact]
    public void OwnerPc_OneVbCablePair_BothInputsJoinIt()
    {
        var found = VirtualCables.Scan(OwnerPc.Playback, OwnerPc.Recording, Array.Empty<CableMark>());
        var cable = Assert.Single(found);
        Assert.True(cable.IsPair);
        Assert.Equal(OwnerPc.CableOutput, cable.Id);
        Assert.Equal("CABLE", cable.Name);
        Assert.Equal("VB-Audio", cable.Maker);
        Assert.Equal(new[] { OwnerPc.Cable16, OwnerPc.CableInput }.Order(), cable.Inputs.Select(c => c.PlaybackId).Order());
        Assert.Equal(1, EffectsRules.AllowedPairs(Support(OwnerPc.Playback, OwnerPc.Recording)));
    }

    /// <summary>Three makers' cables are three pairs; the real speaker, Stereo Mix and the microphone are never taken.</summary>
    [Fact]
    public void ThreeMakers_ThreePairs_RealDevicesNeverTaken()
    {
        var found = VirtualCables.Scan(ThreeMakersPlayback, ThreeMakersRecording, Array.Empty<CableMark>());
        Assert.Equal(new[] { "CABLE", "Hi-Fi Cable", "Line 1" }, found.Where(f => f.IsPair).Select(f => f.Name));
        Assert.Equal(new[] { "VB-Audio", "VB-Audio", "Eugene Muzychenko" }, found.Select(f => f.Maker));
        var ends = found.SelectMany(f => f.Inputs.Select(c => c.PlaybackId).Append(f.Id)).ToList();
        Assert.DoesNotContain(OwnerPc.Speaker, ends);
        Assert.DoesNotContain(OwnerPc.StereoMix, ends);
        Assert.DoesNotContain(OwnerPc.MicArray, ends);
        Assert.Equal(3, EffectsRules.AllowedPairs(Support(ThreeMakersPlayback, ThreeMakersRecording)));
    }

    /// <summary>Voicemeeter's input alone: listed as a mixer with one end, never a usable cable.</summary>
    [Fact]
    public void VoicemeeterInputOnly_ListedNotUsed()
    {
        var playback = OwnerPc.Playback.Append(new AudioDevice(VmIn, "Voicemeeter Input (VB-Audio Voicemeeter VAIO)", false)).ToArray();
        var support = Support(playback, OwnerPc.Recording);
        var vm = Assert.Single(support.Found, f => f.Kind == CableKind.Mixer);
        Assert.Equal(CableEnds.InputOnly, vm.Ends);
        Assert.False(vm.IsPair);
        Assert.False(VirtualCables.IsCable("Voicemeeter Input (VB-Audio Voicemeeter VAIO)"));
        Assert.DoesNotContain(support.Cables, c => c.PlaybackId == VmIn);
        var plan = Plan(support, playback, OwnerPc.Recording, OwnerPc.Settings(), OwnerPc.Spotify);
        Assert.DoesNotContain(VmIn, plan.CablesInUse);
        Assert.Equal(OwnerPc.CableInput, plan.Runs[MixerService.MasterKey].Cable);
    }

    /// <summary>A VB-CABLE whose output Windows does not show: "Input only", not used (a cable needs both ends).</summary>
    [Fact]
    public void CableWithoutItsOutput_InputOnly_NotUsed()
    {
        var support = Support(OwnerPc.Playback, OwnerPc.Recording.Where(r => r.Id != OwnerPc.CableOutput).ToArray());
        Assert.Empty(support.Cables);
        Assert.All(support.Found, f => Assert.Equal(CableEnds.InputOnly, f.Ends));
        Assert.Equal(EffectsRules.NeedsCable, EffectsRules.CablesMissing(support));
    }

    /// <summary>A pair the user marked by hand is a cable like any other: listed, usable, given out; unknown devices are never guessed.</summary>
    [Fact]
    public void UserMarkedPair_IsUsedLikeAnyCable()
    {
        var playback = new[] { new AudioDevice(OwnerPc.Speaker, "Speaker (Realtek(R) Audio)", true), new AudioDevice(AcmeIn, "Line (Acme Loopback)", false) };
        var recording = new[] { new AudioDevice(OwnerPc.MicArray, "Microphone Array (AMD Audio Device)", false), new AudioDevice(AcmeOut, "Line (Acme Loopback)", false) };
        Assert.Empty(Support(playback, recording).Found); // not recognised: not guessed

        var mark = new CableMark { Name = "Acme", PlaybackId = AcmeIn, RecordingId = AcmeOut };
        var support = Support(playback, recording, new[] { mark });
        var acme = Assert.Single(support.Found);
        Assert.True(acme.IsPair && acme.Marked);
        Assert.Equal(AcmeOut, acme.Id);
        var plan = Plan(support, playback, recording, OwnerPc.Settings(), OwnerPc.Spotify);
        Assert.Equal(AcmeIn, plan.Runs[MixerService.MasterKey].Cable);
        Assert.Equal(OwnerPc.Speaker, Assert.Single(plan.Runs[MixerService.MasterKey].Outputs));
    }

    /// <summary>A mark of a recognised cable's devices is that cable (not listed twice).</summary>
    [Fact]
    public void MarkOfARecognisedCable_IsNotListedTwice()
    {
        var mark = new CableMark { Name = "Mine", PlaybackId = OwnerPc.CableInput, RecordingId = OwnerPc.CableOutput };
        Assert.Single(VirtualCables.Scan(OwnerPc.Playback, OwnerPc.Recording, new[] { mark }));
    }

    // ---- the planner gives out allowed cables only ----

    /// <summary>Automatic: free cables first; the bus gets the first (by name), the programs the others, each its own pair.</summary>
    [Fact]
    public void ThreeMakers_BusGetsTheFirst_ProgramsTheOthers()
    {
        var plan = Plan(Support(ThreeMakersPlayback, ThreeMakersRecording), ThreeMakersPlayback, ThreeMakersRecording, OwnerPc.Settings(),
            OwnerPc.Spotify, OwnerPc.Helium);
        Assert.Equal(OwnerPc.CableInput, plan.Runs[MixerService.MasterKey].Cable);
        var programs = new[] { plan.Runs[OwnerPc.Helium], plan.Runs[OwnerPc.Spotify] };
        Assert.All(programs, r => Assert.Equal(RunKind.Cable, r.Kind));
        Assert.Equal(new[] { HiFiIn, LineIn }.Order(), programs.Select(r => r.Cable!).Order());
        Assert.Equal(3, plan.CablePairs);
        Assert.False(plan.CableFree);
    }

    /// <summary>A cable switched off is never given out, not even to the bus; the next allowed one takes its place.</summary>
    [Fact]
    public void ForbiddenCable_IsNeverUsed()
    {
        var support = Support(ThreeMakersPlayback, ThreeMakersRecording, forbidden: new HashSet<string> { OwnerPc.CableOutput });
        var plan = Plan(support, ThreeMakersPlayback, ThreeMakersRecording, OwnerPc.Settings(), OwnerPc.Spotify, OwnerPc.Helium);
        Assert.Equal(HiFiIn, plan.Runs[MixerService.MasterKey].Cable);
        Assert.DoesNotContain(OwnerPc.CableInput, plan.CablesInUse);
        Assert.DoesNotContain(OwnerPc.Cable16, plan.CablesInUse);
        Assert.DoesNotContain(plan.Runs.Values, r => r.Cable == OwnerPc.CableInput || r.Cable == OwnerPc.Cable16);
        Assert.Equal(2, plan.CablePairs);
    }

    /// <summary>Every allowed cable in use: a program set to Virtual cable says "No free cable" (and records nothing).</summary>
    [Fact]
    public void NoFreeCable_WhenEveryAllowedCableIsInUse()
    {
        var s = OwnerPc.Settings();
        s.Apps.First(a => a.Key == OwnerPc.Spotify).FxRoute = EffectsRules.RouteCable.Id;
        var support = Support(ThreeMakersPlayback, ThreeMakersRecording, forbidden: new HashSet<string> { HiFiOut, LineOut });
        var plan = Plan(support, ThreeMakersPlayback, ThreeMakersRecording, s, OwnerPc.Spotify);
        Assert.Equal(OwnerPc.CableInput, plan.Runs[MixerService.MasterKey].Cable);
        Assert.False(plan.Runs.ContainsKey(OwnerPc.Spotify));
        Assert.Equal(StatusKind.NoFreeCable, plan.Blocked[OwnerPc.Spotify].Kind);
        Assert.Equal("No free cable", EffectsRules.Brief(plan.Blocked[OwnerPc.Spotify].Kind));
    }

    /// <summary>Every cable switched off: nothing takes one and the status says so (not "Needs VB-CABLE": it is installed).</summary>
    [Fact]
    public void EveryCableSwitchedOff_SaysSo()
    {
        var support = Support(OwnerPc.Playback, OwnerPc.Recording, forbidden: new HashSet<string> { OwnerPc.CableOutput });
        var plan = Plan(support, OwnerPc.Playback, OwnerPc.Recording, OwnerPc.Settings(), OwnerPc.Spotify);
        Assert.Empty(plan.CablesInUse);
        Assert.Equal(StatusKind.CablesOff, plan.Blocked[MixerService.MasterKey].Kind);
        Assert.Equal(EffectsRules.CablesOff, EffectsRules.CablesMissing(support));
        Assert.True(EffectsRules.CableInstalled(support));
    }

    /// <summary>A mixer.json written before cables could be switched off or marked loads unchanged: every cable allowed, none marked.</summary>
    [Fact]
    public void OldMixerFile_LoadsWithEveryCableAllowed()
    {
        var s = OwnerPc.Settings();
        Assert.Empty(s.ForbiddenCables);
        Assert.Empty(s.MarkedCables);
        var again = JsonSerializer.Deserialize<MixerSettings>(JsonSerializer.Serialize(s))!;
        Assert.Empty(again.MarkedCables);
    }
}
