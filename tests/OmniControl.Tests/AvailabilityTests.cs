using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Pages.Volume;
using OmniControl.Core.Engine;

namespace OmniControl.Tests;

/// <summary>
/// The owner's rule for the Volume page: only what works is selectable, the rest is greyed out with the reason. Every pure rule behind a
/// greyed control (<c>SoundModes.Availability.cs</c> and the Method menus, the setup table, the gates): the sound modes, combining
/// devices, device choices, cable switches, the Test and the hotkeys.
/// </summary>
public class AvailabilityTests
{
    private static readonly IReadOnlyList<CableMark> NoMarks = Array.Empty<CableMark>();

    /// <summary>The owner's PC with VB-CABLE allowed (one pair).</summary>
    private static AudioEffectsSupport OnePair => OwnerPc.Support;

    /// <summary>VB-CABLE installed but switched off in the Virtual cables list.</summary>
    private static AudioEffectsSupport Switched => OwnerPc.Support with
    {
        Cables = Array.Empty<VirtualCable>(),
        Found = VirtualCables.Scan(OwnerPc.Playback, OwnerPc.Recording, NoMarks),
    };

    private static AudioEffectsSupport NoCable => OwnerPc.Support with { Cables = Array.Empty<VirtualCable>(), Found = Array.Empty<FoundCable>() };

    // ---- the sound modes ----

    [Fact]
    public void FullControl_IsUnavailable_WithoutAnAllowedCable_AndSaysWhy()
    {
        Assert.Null(SoundModes.ModeUnavailable(SoundModes.Full, OnePair));
        Assert.Equal("Needs VB-CABLE", SoundModes.ModeUnavailable(SoundModes.Full, NoCable));
        Assert.Equal("Cables switched off", SoundModes.ModeUnavailable(SoundModes.Full, Switched));
        Assert.Null(SoundModes.ModeUnavailable(SoundModes.Full, null)); // not looked at yet: nothing is greyed
    }

    [Fact]
    public void FastMode_CanAlwaysBeChosen_BecauseProgramsCanBeRecorded()
    {
        Assert.Null(SoundModes.ModeUnavailable(SoundModes.Fast, NoCable));
        Assert.Null(SoundModes.ModeUnavailable(SoundModes.Fast, Switched));
    }

    [Fact]
    public void ModeNotAvailable_KeepsNamesCapitalised()
    {
        Assert.Equal("Full control is not available: needs VB-CABLE", SoundModes.ModeNotAvailable(SoundModes.Full, "Needs VB-CABLE"));
        Assert.Equal("Full control is not available: cables switched off", SoundModes.ModeNotAvailable(SoundModes.Full, "Cables switched off"));
    }

    [Fact]
    public void SwitchSoundModeHotkey_IsUnavailable_WhenTheNextModeIs()
    {
        Assert.NotNull(SoundModes.NextModeUnavailable(SoundModes.Fast, NoCable)); // next = Full
        Assert.Null(SoundModes.NextModeUnavailable(SoundModes.Full, NoCable)); // next = Fast
        Assert.Null(SoundModes.NextModeUnavailable(SoundModes.Fast, OnePair));
    }

    // ---- combining ----

    [Fact]
    public void CombineSpeakers_NeedsFullControl_ThenACable_ThenAFreeOne()
    {
        var fast = SoundModes.CombineGate(false, SoundModes.Fast, OnePair, freeCable: true, busCableName: null);
        Assert.False(fast.Allowed);
        Assert.Equal(SoundModes.Full, fast.SwitchTo);
        Assert.Contains("Full control", fast.Reason);

        Assert.Equal("Needs VB-CABLE", SoundModes.CombineGate(false, SoundModes.Full, NoCable, false, null).Note);
        Assert.Equal("Cables switched off", SoundModes.CombineGate(false, SoundModes.Full, Switched, false, null).Note);

        var noFree = SoundModes.CombineGate(false, SoundModes.Full, OnePair, freeCable: false, busCableName: null);
        Assert.False(noFree.Allowed);
        Assert.Equal("No free cable", noFree.Note);
        Assert.Null(noFree.SwitchTo);

        Assert.True(SoundModes.CombineGate(false, SoundModes.Full, OnePair, freeCable: true, busCableName: null).Allowed);
    }

    [Fact]
    public void CombineSpeakers_NamesTheMasterBus_WhenItHoldsTheOnlyCable()
    {
        var gate = SoundModes.CombineGate(false, SoundModes.Full, OnePair, false, "CABLE Input");
        Assert.False(gate.Allowed);
        Assert.Contains("master bus uses CABLE Input", gate.Reason);
    }

    [Fact]
    public void CombineMicrophones_WorkInBothModes_NeedAFreeCableAppsCanRecordFrom()
    {
        Assert.True(SoundModes.CombineGate(true, SoundModes.Fast, OnePair, true, null).Allowed);
        Assert.True(SoundModes.CombineGate(true, SoundModes.Full, OnePair, true, null).Allowed);
        Assert.Equal("Needs VB-CABLE", SoundModes.CombineGate(true, SoundModes.Fast, NoCable, false, null).Note);

        var playbackOnly = OnePair with { Cables = OnePair.Cables.Select(c => c with { RecordingId = null, RecordingName = null }).ToArray() };
        Assert.Equal(SoundModes.NoRecordableCable, SoundModes.CombineGate(true, SoundModes.Full, playbackOnly, false, null).Note);
    }

    [Fact]
    public void CombineMicrophones_WhileTheMasterBusHoldsTheCable_OffersSwitchToFast()
    {
        var gate = SoundModes.CombineGate(true, SoundModes.Full, OnePair, false, "CABLE Input");
        Assert.False(gate.Allowed);
        Assert.Equal(SoundModes.Fast, gate.SwitchTo);
        Assert.Equal(SoundModes.BusHoldsCable("CABLE Input"), gate.Reason);
    }

    [Fact]
    public void CombineGate_IsOpen_BeforeTheFirstLook()
    {
        Assert.True(SoundModes.CombineGate(false, SoundModes.Fast, null, false, null).Allowed);
        Assert.True(SoundModes.CombineGate(true, SoundModes.Full, null, false, null).Allowed);
    }

    // ---- a combined speaker as a "plays on" choice ----

    [Fact]
    public void CombinedChoice_IsDimmed_WhileItHasNoCable()
    {
        var gate = SoundModes.CombinedChoiceGate(Gate.Open, StatusKind.NeedsAnotherCable, "Living room");
        Assert.False(gate.Allowed);
        Assert.Equal("Living room has no cable yet (Needs another cable)", gate.Reason);
        Assert.True(SoundModes.CombinedChoiceGate(Gate.Open, StatusKind.Running, "Living room").Allowed);
        var fast = SoundModes.CombineGate(false, SoundModes.Fast, OnePair, true, null);
        Assert.Same(fast, SoundModes.CombinedChoiceGate(fast, StatusKind.NeedsAnotherCable, "Living room")); // the mode's own reason comes first
    }

    [Fact]
    public void Cables_AreOfferedAsDevices_OnlyInFullControl()
    {
        Assert.True(SoundModes.CablesOfferedAsDevices(SoundModes.Full));
        Assert.False(SoundModes.CablesOfferedAsDevices(SoundModes.Fast));
    }

    // ---- the Method menus ----

    [Fact]
    public void VirtualCable_SaysSwitchedOff_NotNotInstalled_WhenEveryCableIsSwitchedOff()
    {
        var offer = SoundModes.OfferOf(SoundModes.RouteCable,
            new RouteFacts(new CableOffer(0, false, false, StatusKind.CablesOff), null, RunMethod.None));
        Assert.False(offer.Available);
        Assert.Equal("Not available: every cable is switched off", offer.Description);
    }

    [Fact]
    public void EqualizerApoOnACable_NamesTheMissingCable_WhenThereIsNone()
    {
        Assert.Equal("Needs VB-CABLE", SoundModes.ApoCableMissing(0, false, false, StatusKind.NeedsCable));
        Assert.Equal("Cables switched off", SoundModes.ApoCableMissing(0, false, false, StatusKind.CablesOff));
        Assert.Equal(SoundModes.NeedsSecondCable, SoundModes.ApoCableMissing(1, false, false, StatusKind.Other));
        Assert.Equal(SoundModes.ApoNotOnCable, SoundModes.ApoCableMissing(2, false, false));
        Assert.Null(SoundModes.ApoCableMissing(2, true, true));
    }

    // ---- the setup table ----

    private static readonly HelpFacts Pair = new(true, RunMethod.Bus, 1, StatusKind.Other, null, true, false, true, CableUse.None);

    [Fact]
    public void FastRow_StaysChoosable_EvenWhenTheMasterLacksEqualizerApo_OtherRowsDoNot()
    {
        var noApo = Pair with { SpeakersApoWhy = "Equalizer APO is not on this device" };
        var fast = SoundModes.WorksOf(SoundModes.HelpCombinations.Single(r => r.Number == SoundModes.FastSetup), noApo);
        Assert.False(fast.Available);
        Assert.True(fast.CanChoose);

        var two = SoundModes.WorksOf(SoundModes.HelpCombinations.Single(r => r.Number == 2), noApo);
        Assert.False(two.Available);
        Assert.False(two.CanChoose);
        Assert.Equal("Equalizer APO is not on this device", two.Text);

        var works = SoundModes.WorksOf(SoundModes.HelpCombinations.Single(r => r.Number == 1), Pair);
        Assert.True(works.Available);
        Assert.True(works.CanChoose);
    }

    [Fact]
    public void SetupRow_ClickDoesNothing_WhenItCannotBeChosen_AndItsTooltipSaysWhy()
    {
        var chosen = new List<int>();
        var row = new SetupRow(SoundModes.HelpCombinations.Single(r => r.Number == 2), chosen.Add);
        row.Sync(new HelpWorks("Equalizer APO is not on this device", false, false), current: false);
        row.ChooseCommand.Execute(null);
        Assert.Empty(chosen);
        Assert.Equal("Setup 2: Equalizer APO is not on this device.", row.Tip);

        row.Sync(new HelpWorks("Available", true, true), current: false);
        row.ChooseCommand.Execute(null);
        Assert.Equal(new[] { 2 }, chosen);
    }

    // ---- the gate's Switch link ----

    [Fact]
    public void ModeGate_OffersNoSwitch_ToAModeThatCannotBeChosen_AndSaysSo()
    {
        var gate = new ModeGate(_ => { }, mode => mode == SoundModes.Full ? "Full control is not available: needs VB-CABLE" : null);
        gate.Sync(new Gate("Needs Full control mode: combined speakers run through a virtual cable", SoundModes.Full));
        Assert.False(gate.IsAvailable);
        Assert.False(gate.CanSwitch);
        Assert.Equal("Needs Full control mode: combined speakers run through a virtual cable. Full control is not available: needs VB-CABLE", gate.Reason);

        gate.Sync(new Gate(SoundModes.BusHoldsCable("CABLE Input"), SoundModes.Fast));
        Assert.True(gate.CanSwitch);
        Assert.Equal("Switch to Fast", gate.SwitchLabel);

        gate.Sync(Gate.Open);
        Assert.True(gate.IsAvailable);
        Assert.False(gate.CanSwitch);
    }

    // ---- cable switches, the Test ----

    [Fact]
    public void CableSwitch_SaysWhatStops_WhenSomethingUsesTheCable()
    {
        Assert.Equal("On: OmniControl may use this cable. Off: it never does.", SoundModes.CableSwitchTip(null));
        Assert.Contains("Spotify stops", SoundModes.CableSwitchTip("Spotify"));
        Assert.Equal("CABLE switched off: the master bus stops until another cable is free", SoundModes.CableSwitchedOffStops("CABLE", "the master bus"));
    }

    [Fact]
    public void Test_NeedsTheProgramToBePlaying()
    {
        Assert.Null(SoundModes.TestUnavailable("Spotify", true));
        Assert.Equal("Play sound in Spotify first, then press Test", SoundModes.TestUnavailable("Spotify", false));
    }

    // ---- a program on a combined speaker whose cable is gone ----

    [Fact]
    public void Planner_SaysSo_WhenAProgramPlaysOnACombinedSpeakerThatHasNoCable()
    {
        var s = OwnerPc.Settings(); // Full control, one pair that the master bus takes: a combined speaker gets no cable
        var combo = new CombinedDevice { Name = "Living room", Kind = CombinedKind.Speakers, Members = { OwnerPc.Speaker } };
        s.Combined.Add(combo);
        s.Apps.Single(a => a.Key == OwnerPc.Claude).PlaysOn.Add(combo.Key);
        var plan = RoutePlanner.Make(new PlanInput(SoundModes.Full, OwnerPc.Support, true, s, new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
            OwnerPc.Playback, OwnerPc.Recording, new HashSet<string>()));

        var status = plan.Blocked[OwnerPc.Claude];
        Assert.Equal(StatusKind.NeedsAnotherCable, status.Kind);
        Assert.Contains("Living room has no cable", status.Text);
        Assert.Contains("Playing on the master instead", status.Text);
    }
}
