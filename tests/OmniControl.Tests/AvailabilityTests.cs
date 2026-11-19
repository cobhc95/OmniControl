using OmniControl.App.Features.Volume.Pages;

namespace OmniControl.Tests;

/// The Volume page's rule: only what works is selectable, the rest is greyed out with the reason. Every pure rule behind a
/// greyed control (<c>EffectsRules.Availability.cs</c> and the Method menus, the setup table, the gates): combining
/// devices, device choices, cable switches, the Test and the hotkeys.
public class AvailabilityTests
{
    private static readonly IReadOnlyList<CableMark> NoMarks = Array.Empty<CableMark>();

    /// The reference PC with VB-CABLE allowed (one pair).
    private static AudioEffectsSupport OnePair => OwnerPc.Support;

    /// VB-CABLE installed but switched off in the Virtual cables list.
    private static AudioEffectsSupport Switched => OwnerPc.Support with
    {
        Cables = Array.Empty<VirtualCable>(),
        Found = VirtualCables.Scan(OwnerPc.Playback, OwnerPc.Recording, NoMarks),
    };

    private static AudioEffectsSupport NoCable => OwnerPc.Support with { Cables = Array.Empty<VirtualCable>(), Found = Array.Empty<FoundCable>() };

    // ---- combining ----

    [Fact]
    public void CombineSpeakers_NeedACable_ThenAFreeOne()
    {
        Assert.Equal("Needs VB-CABLE", EffectsRules.CombineGate(false, NoCable, false, null).Note);
        Assert.Equal("Cables switched off", EffectsRules.CombineGate(false, Switched, false, null).Note);

        var noFree = EffectsRules.CombineGate(false, OnePair, freeCable: false, busCableName: null);
        Assert.False(noFree.Allowed);
        Assert.Equal("No free cable", noFree.Note);

        Assert.True(EffectsRules.CombineGate(false, OnePair, freeCable: true, busCableName: null).Allowed);
    }

    [Fact]
    public void CombineSpeakers_NamesTheMasterBus_WhenItHoldsTheOnlyCable()
    {
        var gate = EffectsRules.CombineGate(false, OnePair, false, "CABLE Input");
        Assert.False(gate.Allowed);
        Assert.Contains("master bus uses CABLE Input", gate.Reason);
    }

    [Fact]
    public void CombineMicrophones_NeedAFreeCableAppsCanRecordFrom()
    {
        Assert.True(EffectsRules.CombineGate(true, OnePair, true, null).Allowed);
        Assert.Equal("Needs VB-CABLE", EffectsRules.CombineGate(true, NoCable, false, null).Note);

        var playbackOnly = OnePair with { Cables = OnePair.Cables.Select(c => c with { RecordingId = null, RecordingName = null }).ToArray() };
        Assert.Equal(EffectsRules.NoRecordableCable, EffectsRules.CombineGate(true, playbackOnly, false, null).Note);
    }

    [Fact]
    public void CombineMicrophones_WhileTheMasterBusHoldsTheCable_SaysSo()
    {
        var gate = EffectsRules.CombineGate(true, OnePair, false, "CABLE Input");
        Assert.False(gate.Allowed);
        Assert.Equal(EffectsRules.BusHoldsCable("CABLE Input"), gate.Reason);
    }

    [Fact]
    public void CombineGate_IsOpen_BeforeTheFirstLook()
    {
        Assert.True(EffectsRules.CombineGate(false, null, false, null).Allowed);
        Assert.True(EffectsRules.CombineGate(true, null, false, null).Allowed);
    }

    // ---- a combined speaker as a "plays on" choice ----

    [Fact]
    public void CombinedChoice_IsDimmed_WhileItHasNoCable()
    {
        var gate = EffectsRules.CombinedChoiceGate(Gate.Open, StatusKind.NeedsAnotherCable, "Living room");
        Assert.False(gate.Allowed);
        Assert.Equal("Living room has no cable yet (Needs another cable)", gate.Reason);
        Assert.True(EffectsRules.CombinedChoiceGate(Gate.Open, StatusKind.Running, "Living room").Allowed);
        var blocked = EffectsRules.CombineGate(false, OnePair, false, null);
        Assert.Same(blocked, EffectsRules.CombinedChoiceGate(blocked, StatusKind.NeedsAnotherCable, "Living room")); // the gate's own reason comes first
    }

    // ---- the Method menus ----

    [Fact]
    public void VirtualCable_SaysSwitchedOff_NotNotInstalled_WhenEveryCableIsSwitchedOff()
    {
        var offer = EffectsRules.OfferOf(EffectsRules.RouteCable,
            new RouteFacts(new CableOffer(0, false, false, StatusKind.CablesOff), null, RunMethod.None));
        Assert.False(offer.Available);
        Assert.Equal("Not available: every cable is switched off", offer.Description);
    }

    [Fact]
    public void EqualizerApoOnACable_NamesTheMissingCable_WhenThereIsNone()
    {
        Assert.Equal("Needs VB-CABLE", EffectsRules.ApoCableMissing(0, false, false, StatusKind.NeedsCable));
        Assert.Equal("Cables switched off", EffectsRules.ApoCableMissing(0, false, false, StatusKind.CablesOff));
        Assert.Equal(EffectsRules.NeedsSecondCable, EffectsRules.ApoCableMissing(1, false, false, StatusKind.Other));
        Assert.Equal(EffectsRules.ApoNotOnCable, EffectsRules.ApoCableMissing(2, false, false));
        Assert.Null(EffectsRules.ApoCableMissing(2, true, true));
    }

    // ---- the gate ----

    [Fact]
    public void ControlGate_FollowsTheGate_AndOpensAgain()
    {
        var gate = new ControlGate();
        gate.Sync(new Gate(EffectsRules.BusHoldsCable("CABLE Input"), "No free cable"));
        Assert.False(gate.IsAvailable);
        Assert.Equal(EffectsRules.BusHoldsCable("CABLE Input"), gate.Reason);
        Assert.Equal("No free cable", gate.Note);

        gate.Sync(Gate.Open);
        Assert.True(gate.IsAvailable);
        Assert.Null(gate.Note);
    }

    // ---- cable switches, the Test ----

    [Fact]
    public void CableSwitch_SaysWhatStops_WhenSomethingUsesTheCable()
    {
        Assert.Equal("On: OmniControl may use this cable. Off: it never does.", EffectsRules.CableSwitchTip(null));
        Assert.Contains("Spotify stops", EffectsRules.CableSwitchTip("Spotify"));
        Assert.Equal("CABLE switched off: the master bus stops until another cable is free", EffectsRules.CableSwitchedOffStops("CABLE", "the master bus"));
    }

    [Fact]
    public void Test_NeedsTheProgramToBePlaying()
    {
        Assert.Null(EffectsRules.TestUnavailable("Spotify", true));
        Assert.Equal("Play sound in Spotify first, then press Test", EffectsRules.TestUnavailable("Spotify", false));
    }

    // ---- a program on a combined speaker whose cable is gone ----

    [Fact]
    public void Planner_SaysSo_WhenAProgramPlaysOnACombinedSpeakerThatHasNoCable()
    {
        var s = OwnerPc.Settings(); // one pair that the master bus takes: a combined speaker gets no cable
        var combo = new CombinedDevice { Name = "Living room", Kind = CombinedKind.Speakers, Members = { OwnerPc.Speaker } };
        s.Combined.Add(combo);
        s.Apps.Single(a => a.Key == OwnerPc.Chat).PlaysOn.Add(combo.Key);
        var plan = RoutePlanner.Make(new PlanInput(OwnerPc.Support, true, s, new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
            OwnerPc.Playback, OwnerPc.Recording, new HashSet<string>()));

        var status = plan.Blocked[OwnerPc.Chat];
        Assert.Equal(StatusKind.NeedsAnotherCable, status.Kind);
        Assert.Contains("Living room has no cable", status.Text);
        Assert.Contains("Playing on the master instead", status.Text);
    }
}
