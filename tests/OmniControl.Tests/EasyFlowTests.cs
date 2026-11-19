using OmniControl.App.Features.Volume.Pages.Easy;
using OmniControl.App.Features.Volume.Pages.Easy.Flow;

namespace OmniControl.Tests;

/// Easy mode, phase 2 (the guided setups): every checklist worked out from made-up facts (installed or not, device covered or not, cable free
/// or not, which mode), the changes each step makes through a fake mixer, the "on" mark, and the flow's screens (Back, Next, a fix that asks
/// first, ticks that appear when the facts change). Nothing here touches Windows, the mixer's files or an installer.
public class EasyFlowTests
{
    private static EasyCheck Line(IReadOnlyList<EasyCheck> lines, string id) => lines.Single(l => l.Id == id);

    private static readonly int[] Bass = EasyEq.Presets.First(p => p.Name == "Bass boost").Gains;

    private static EasyAnswers Answers(params string[] devices)
    {
        var a = new EasyAnswers();
        a.Devices.AddRange(devices);
        return a;
    }

    private static EasyAnswers ApoWay => new() { Engine = EasyEngine.Apo };

    private static EasyAnswers CableWay => new() { Engine = EasyEngine.Cable };

    private static EasyCableFact Free => new("CABLE Input", "CABLE Output", StatusKind.Other, null, false);

    // ---- card 1: make everything sound better ----

    [Fact]
    public void Better_ApoNotInstalled_OffersTheInstallerAndWaitsOnTheRest()
    {
        var lines = EasyChecks.Better(ApoWay, new EasyFacts { ApoInstalled = false, SpeakersApoWhy = "Equalizer APO is not installed", MasterStoredGains = Bass });
        Assert.Equal(CheckState.Todo, Line(lines, "apo").State);
        Assert.Equal(EasyFixes.InstallApo, Line(lines, "apo").Fix!.Id);
        Assert.Equal(CheckState.Blocked, Line(lines, "speakers").State);
        Assert.Equal(CheckState.Blocked, Line(lines, "apply").State);
        Assert.DoesNotContain(lines, l => l.Id is "other" or "engine"); // the Engine step decides the way now, no "use another way" line
    }

    [Fact]
    public void Better_SpeakersNotSelected_OffersTheDeviceSelector_AndTicksWhenCovered()
    {
        var facts = new EasyFacts { SpeakersApoWhy = "Equalizer APO is not on this device", MasterStoredGains = Bass };
        var before = EasyChecks.Better(new EasyAnswers(), facts);
        Assert.Equal(CheckState.Done, Line(before, "apo").State);
        Assert.Equal(CheckState.Todo, Line(before, "speakers").State);
        Assert.Equal(EasyFixes.ChooseDevices, Line(before, "speakers").Fix!.Id);
        Assert.Equal(CheckState.Blocked, Line(before, "apply").State);

        var after = EasyChecks.Better(new EasyAnswers(), facts with { SpeakersApoWhy = null });
        Assert.Equal(CheckState.Done, Line(after, "speakers").State);
        Assert.Equal(CheckState.Todo, Line(after, "apply").State);
        Assert.Equal(EasyFixes.ApplyMasterSound, Line(after, "apply").Fix!.Id);
        Assert.DoesNotContain(after, l => l.Id == "other");
    }

    [Fact]
    public void Better_SoundOnAndEverythingInPlace_IsAllDone()
    {
        var lines = EasyChecks.Better(new EasyAnswers(), new EasyFacts { MasterStoredGains = Bass, MasterGains = Bass, MasterFxOn = true });
        Assert.All(lines, l => Assert.Equal(CheckState.Done, l.State));
    }

    [Fact]
    public void Better_ASoundChosenButSwitchedOff_IsToApplyAgain()
    {
        var lines = EasyChecks.Better(new EasyAnswers(), new EasyFacts { MasterStoredGains = Bass, MasterGains = EasyEq.Flat(), MasterFxOn = false });
        Assert.Equal(CheckState.Todo, Line(lines, "apply").State);
    }

    [Fact]
    public void Better_NormalSound_NeedsNothingToApply()
    {
        var lines = EasyChecks.Better(new EasyAnswers(), new EasyFacts { ApoInstalled = false, SpeakersApoWhy = "x" });
        Assert.Equal(CheckState.Done, Line(lines, "apply").State);
    }

    [Fact]
    public void Better_OnTheBus_NeedsNoEqualizerApo()
    {
        var lines = EasyChecks.Better(CableWay, new EasyFacts { MasterOnBus = true, ApoInstalled = false, SpeakersApoWhy = "x", MasterStoredGains = Bass, MasterGains = Bass, MasterFxOn = true });
        Assert.DoesNotContain(lines, l => l.Id is "apo" or "speakers" or "other");
        Assert.All(lines, l => Assert.Equal(CheckState.Done, l.State));
    }

    [Fact]
    public void Better_WithEqualizerApoAsTheMastersMethod_StillChecksTheSpeakers()
    {
        var lines = EasyChecks.Better(ApoWay, new EasyFacts { MasterOnBus = false, SpeakersApoWhy = "Equalizer APO is not on this device", MasterStoredGains = Bass });
        Assert.Contains(lines, l => l.Id == "speakers");
        Assert.DoesNotContain(lines, l => l.Id == "apo-master"); // the master is not on the bus: nothing to change
    }

    // ---- card 2: a different sound per app ----

    [Fact]
    public void PerApp_NeedsAnAppFirst_ThenOffersTheirOwnSound_AndTicksWhenTheyHaveIt()
    {
        var facts = new EasyFacts { Programs = new[] { new EasyOption("spotify", "Spotify", "playing") } };
        Assert.Equal(CheckState.Blocked, Line(EasyChecks.PerApp(new EasyAnswers(), facts), "own").State);

        var a = new EasyAnswers();
        a.Programs.Add("spotify");
        var todo = Line(EasyChecks.PerApp(a, facts), "own");
        Assert.Equal(CheckState.Todo, todo.State);
        Assert.Equal(EasyFixes.ApplyProgramSounds, todo.Fix!.Id);

        var done = Line(EasyChecks.PerApp(a, facts with { OwnEqPrograms = new HashSet<string> { "spotify" } }), "own");
        Assert.Equal(CheckState.Done, done.State);
        Assert.Contains("Spotify", done.Subtitle);
    }

    [Fact]
    public void PerApp_WhenRecordingDoesNotWork_SaysWhyAndWaits()
    {
        var a = new EasyAnswers();
        a.Programs.Add("spotify");
        var lines = EasyChecks.PerApp(a, new EasyFacts { ProgramsWhy = "Needs Windows 10 version 2004 or later" });
        Assert.Equal(CheckState.Blocked, Line(lines, "works").State);
        Assert.Contains("2004", Line(lines, "works").Subtitle);
        Assert.Equal(CheckState.Blocked, Line(lines, "own").State);
        Assert.Null(Line(lines, "own").Fix);
    }

    // ---- card 3: improve my microphone ----

    [Fact]
    public void Microphone_NoneChosenYet_WaitsOnTheChoice()
    {
        var lines = EasyChecks.Microphone(new EasyAnswers(), new EasyFacts());
        Assert.Equal(CheckState.Done, Line(lines, "cable").State);
        Assert.Equal(CheckState.Blocked, Line(lines, "apply").State);
    }

    [Fact]
    public void Microphone_NeedsACable_AndApplyingFollows()
    {
        var a = new EasyAnswers { MicrophoneId = "mic1", MicLook = "Clear voice" };
        var noCable = new EasyFacts { CableForMicrophone = new EasyCableFact(null, null, StatusKind.NeedsCable, null, false) };
        var before = EasyChecks.Microphone(a, noCable);
        Assert.Equal(EasyFixes.InstallCable, Line(before, "cable").Fix!.Id);
        Assert.Equal(CheckState.Blocked, Line(before, "apply").State);

        var after = EasyChecks.Microphone(a, new EasyFacts());
        Assert.Equal(CheckState.Todo, Line(after, "apply").State);
        Assert.Equal(EasyFixes.ApplyMicrophone, Line(after, "apply").Fix!.Id);

        var on = EasyChecks.Microphone(a, new EasyFacts { MicGains = new Dictionary<string, int[]> { ["mic1"] = a.MicGains.ToArray() } });
        Assert.Equal(CheckState.Done, Line(on, "apply").State);
        Assert.DoesNotContain(on, l => l.Id is "apo" or "mic" or "fast");
    }

    [Fact]
    public void MicLooks_AreOneListOfFiveGains_AndNormalIsFlat()
    {
        Assert.True(EasyEq.IsFlat(EasyEq.MicPresets[0].Gains));
        Assert.All(EasyEq.MicPresets, p => Assert.Equal(EasyEq.Parts.Count, p.Gains.Length));
        Assert.All(EasyEq.MicPresets, p => Assert.All(p.Gains, g => Assert.InRange(g, -EasyEq.MaxGainDb, EasyEq.MaxGainDb)));
        Assert.Equal(EasyEq.MicPresets.Count, EasyEq.MicPresets.Select(p => p.Name).Distinct().Count());
    }

    // ---- cards 4 and 5: combining ----

    [Fact]
    public void Cable_FreeIsDone_NotInstalledOffersTheInstaller_SwitchedOffHasNoFix()
    {
        var a = Answers("m1", "m2");
        Assert.Equal(CheckState.Done, Line(EasyChecks.JoinMicrophones(a, new EasyFacts()), "cable").State);

        var none = Line(EasyChecks.JoinMicrophones(a, new EasyFacts { CableForMicrophone = new(null, null, StatusKind.NeedsCable, null, false) }), "cable");
        Assert.Equal(EasyFixes.InstallCable, none.Fix!.Id);

        var off = Line(EasyChecks.JoinMicrophones(a, new EasyFacts { CableForMicrophone = new(null, null, StatusKind.CablesOff, null, false) }), "cable");
        Assert.Equal(CheckState.Blocked, off.State);
        Assert.Null(off.Fix);
    }

    [Fact]
    public void Cable_HeldByTheMasterBus_OffersToFreeItOnlyWhenThatChoiceIsAvailable_ElseMoreCables()
    {
        var a = Answers("m1", "m2");
        var canFree = Line(EasyChecks.JoinMicrophones(a, new EasyFacts { CableForMicrophone = new(null, null, StatusKind.Other, "CABLE Input", true) }), "cable");
        Assert.Equal(EasyFixes.FreeCable, canFree.Fix!.Id);
        Assert.NotNull(canFree.Fix.Confirm);
        Assert.Contains("Equalizer APO", canFree.Fix.Confirm!.Text);

        var cannot = Line(EasyChecks.JoinMicrophones(a, new EasyFacts { CableForMicrophone = new(null, null, StatusKind.Other, "CABLE Input", false) }), "cable");
        Assert.Equal(EasyFixes.MoreCables, cannot.Fix!.Id);
        Assert.Contains("second cable pair", cannot.Subtitle);

        var inUse = Line(EasyChecks.JoinMicrophones(a, new EasyFacts { CableForMicrophone = new(null, null, StatusKind.Other, null, false) }), "cable");
        Assert.Equal(EasyFixes.MoreCables, inUse.Fix!.Id);
    }

    [Fact]
    public void JoinMicrophones_CreateWaitsForTwoMicrophonesAndACable_ThenIsDoneOnceItExists()
    {
        var one = EasyChecks.JoinMicrophones(Answers("m1"), new EasyFacts());
        Assert.Equal(CheckState.Blocked, Line(one, "create").State);

        var noCable = EasyChecks.JoinMicrophones(Answers("m1", "m2"), new EasyFacts { CableForMicrophone = new(null, null, StatusKind.NeedsCable, null, false) });
        Assert.Equal(CheckState.Blocked, Line(noCable, "create").State);

        var ready = Line(EasyChecks.JoinMicrophones(Answers("m1", "m2"), new EasyFacts()), "create");
        Assert.Equal(EasyFixes.CreateMicrophone, ready.Fix!.Id);
        Assert.Equal(CheckState.Todo, ready.State);

        var made = new EasyCombinedFact("combo:1", CombinedKind.Microphones, "Combined microphone", new[] { "m2", "m1" }, "CABLE Output");
        var done = Line(EasyChecks.JoinMicrophones(Answers("m1", "m2"), new EasyFacts { Combined = new[] { made } }), "create");
        Assert.Equal(CheckState.Done, done.State);
        Assert.Contains("CABLE Output", done.Subtitle);
    }

    [Fact]
    public void TwoSpeakers_NeedACable_AndSayWhyWhenThereIsNone()
    {
        var a = Answers("s1", "s2");
        var none = EasyChecks.TwoSpeakers(a, new EasyFacts { CableForSpeakers = new EasyCableFact(null, null, StatusKind.NeedsCable, null, false) });
        Assert.Equal(EasyFixes.InstallCable, Line(none, "cable").Fix!.Id);
        Assert.Equal(CheckState.Blocked, Line(none, "create").State);
        Assert.DoesNotContain(none, l => l.Id == "full");

        var off = Line(EasyChecks.TwoSpeakers(a, new EasyFacts { CableForSpeakers = new EasyCableFact(null, null, StatusKind.CablesOff, null, false) }), "cable");
        Assert.Equal(CheckState.Blocked, off.State);
        Assert.Contains(EffectsRules.CablesOff, off.Subtitle);

        var on = EasyChecks.TwoSpeakers(a, new EasyFacts());
        Assert.Equal(CheckState.Done, Line(on, "cable").State);
        Assert.Equal(EasyFixes.CreateSpeakers, Line(on, "create").Fix!.Id);
    }

    [Fact]
    public void TwoSpeakers_ExtrasOfferThePlayOnBothButtonOnlyOnTheBus()
    {
        var a = Answers("s1", "s2");
        var made = new EasyCombinedFact("combo:2", CombinedKind.Speakers, "Both speakers", new[] { "s1", "s2" }, null);
        Assert.Empty(EasyChecks.TwoSpeakersExtras(a, new EasyFacts()));

        var bus = EasyChecks.TwoSpeakersExtras(a, new EasyFacts { Combined = new[] { made }, MasterOnBus = true }).Single();
        Assert.Equal(EasyFixes.PlayOnBoth, bus.Fix!.Id);
        Assert.NotNull(bus.Fix.Confirm);

        var noBus = EasyChecks.TwoSpeakersExtras(a, new EasyFacts { Combined = new[] { made } }).Single();
        Assert.Null(noBus.Fix);

        var already = EasyChecks.TwoSpeakersExtras(a, new EasyFacts { Combined = new[] { made }, MasterOnBus = true, MasterPlaysOn = new[] { "combo:2" } }).Single();
        Assert.Equal(CheckState.Done, already.State);
    }

    [Fact]
    public void Questions_SayWhyTheyCannotBeLeft()
    {
        var facts = new EasyFacts { Microphones = new[] { new EasyOption("m1", "A"), new EasyOption("m2", "B") }, Speakers = new[] { new EasyOption("s1", "One") } };
        Assert.NotNull(EasyChecks.Why(EasyQuestionKind.Programs, new EasyAnswers(), facts));
        Assert.NotNull(EasyChecks.Why(EasyQuestionKind.Microphone, new EasyAnswers(), facts));
        Assert.Null(EasyChecks.Why(EasyQuestionKind.Microphone, new EasyAnswers { MicrophoneId = "m1" }, facts));
        Assert.NotNull(EasyChecks.Why(EasyQuestionKind.Microphones, Answers("m1"), facts));
        Assert.Null(EasyChecks.Why(EasyQuestionKind.Microphones, Answers("m1", "m2"), facts));
        Assert.Contains("only one", EasyChecks.Why(EasyQuestionKind.Speakers, Answers("s1", "s2"), facts)!); // one speaker: nothing to continue with
        Assert.Null(EasyChecks.Why(EasyQuestionKind.Sound, new EasyAnswers(), facts));
    }

    // ---- the "on" mark ----

    [Fact]
    public void GoalsAreMarkedOn_FromTheLiveFacts()
    {
        Assert.All(EasyGoals.All, g => Assert.False(EasyGoals.IsOn(g.Id, new EasyFacts())));
        Assert.True(EasyGoals.IsOn(EasyGoals.Better, new EasyFacts { MasterFxOn = true, MasterGains = Bass }));
        Assert.False(EasyGoals.IsOn(EasyGoals.Better, new EasyFacts { MasterFxOn = false, MasterGains = Bass }));
        Assert.False(EasyGoals.IsOn(EasyGoals.Better, new EasyFacts { MasterFxOn = true }));
        Assert.True(EasyGoals.IsOn(EasyGoals.PerApp, new EasyFacts { OwnEqPrograms = new HashSet<string> { "spotify" } }));
        Assert.True(EasyGoals.IsOn(EasyGoals.Microphone, new EasyFacts { OwnEqMicrophones = new HashSet<string> { "m1" } }));
        var mic = new EasyCombinedFact("combo:1", CombinedKind.Microphones, "M", new[] { "a", "b" }, null);
        var spk = new EasyCombinedFact("combo:2", CombinedKind.Speakers, "S", new[] { "a", "b" }, null);
        Assert.True(EasyGoals.IsOn(EasyGoals.JoinMicrophones, new EasyFacts { Combined = new[] { mic } }));
        Assert.False(EasyGoals.IsOn(EasyGoals.TwoSpeakers, new EasyFacts { Combined = new[] { mic } }));
        Assert.True(EasyGoals.IsOn(EasyGoals.TwoSpeakers, new EasyFacts { Combined = new[] { spk } }));
        Assert.False(EasyGoals.IsOn("volume", new EasyFacts { Combined = new[] { mic, spk } })); // an id that is no goal is never on
    }

    // ---- what each step changes ----

    [Fact]
    public void MasterSound_WritesTheChosenSoundBackAndSwitchesItOn_AFlatOneSwitchesItOff()
    {
        var mixer = new EasyFakeMixer();
        EasyEq.Write(mixer.FxChain(null), Bass);
        EasyEq.Find(mixer.FxChain(null))!.Enabled = false;
        EasyApply.MasterSound(mixer);
        Assert.True(mixer.IsFxOn(null));
        Assert.Equal(Bass, EasyEq.Read(mixer.FxChain(null)));

        EasyEq.Write(mixer.FxChain(null), EasyEq.Flat());
        EasyApply.MasterSound(mixer);
        Assert.False(mixer.IsFxOn(null));
    }

    [Fact]
    public void ProgramSounds_GiveEachAppAFlatEqualiserSwitchedOn_KeepOneItHas_AndSetTheRouteToAutomatic()
    {
        var mixer = new EasyFakeMixer();
        EasyEq.Write(mixer.FxChain("b"), Bass);
        mixer.SetFxRoute("a", EffectsRules.RouteCapture);
        EasyApply.ProgramSounds(mixer, new[] { "a", "b" });
        Assert.True(mixer.IsFxOn("a"));
        Assert.True(EasyEq.IsFlat(EasyEq.Read(mixer.FxChain("a"))));
        Assert.True(EasyEq.OwnEqOn(mixer.FxChain("a"), mixer.IsFxOn("a")));
        Assert.Equal(EffectsRules.RouteAutomatic, mixer.FxRouteOf("a"));
        Assert.Equal(Bass, EasyEq.Read(mixer.FxChain("b")));
        Assert.True(mixer.IsFxOn("b"));
        Assert.False(mixer.IsFxOn("c")); // an app that was not ticked is not touched
    }

    [Fact]
    public void Microphone_PutsTheLookOnItsOwnKey_AndNormalSwitchesItOff()
    {
        var mixer = new EasyFakeMixer();
        var key = MixerService.InputPrefix + "mic1";
        EasyApply.Microphone(mixer, "mic1", "Headset", EasyEq.MicPresets[1].Gains);
        Assert.True(mixer.IsFxOn(key));
        Assert.Equal(EasyEq.MicPresets[1].Gains, EasyEq.Read(mixer.FxChain(key)));
        Assert.False(mixer.IsFxOn(null)); // the master is not touched
        EasyApply.Microphone(mixer, "mic1", "Headset", EasyEq.MicPresets[0].Gains);
        Assert.False(mixer.IsFxOn(key));
    }

    [Fact]
    public void Combine_MakesTheDeviceOfThatKind_AndFreeNameNumbersATakenOne()
    {
        var mixer = new EasyFakeMixer();
        var made = EasyApply.Combine(mixer, CombinedKind.Speakers, "  Both speakers ", new[] { "a", "b", "a" });
        Assert.Equal("Both speakers", made.Name);
        Assert.Equal(new[] { "a", "b" }, made.Members);
        Assert.Same(made, EasyApply.CombinedOf(mixer.Combined, CombinedKind.Speakers, new[] { "b", "a" }));
        Assert.Null(EasyApply.CombinedOf(mixer.Combined, CombinedKind.Microphones, new[] { "a", "b" }));
        Assert.Equal("Both speakers 2", EasyApply.FreeName(new[] { "both speakers" }, "Both speakers"));
        Assert.Equal("Both speakers", EasyApply.FreeName(Array.Empty<string>(), "Both speakers"));
    }

    [Fact]
    public void MasterSteps_ChangeOnlyTheMethodThatWouldStopThem()
    {
        var mixer = new EasyFakeMixer();
        mixer.SetMasterMethod(EffectsRules.MasterApo);
        EasyApply.UseOwnEngine(mixer);
        Assert.Equal(EffectsRules.MasterAutomatic, mixer.MasterMethod);

        mixer.SetMasterMethod(EffectsRules.MasterBus);
        EasyApply.UseOwnEngine(mixer);
        Assert.Equal(EffectsRules.MasterBus, mixer.MasterMethod); // a chosen method that already works is left alone

        EasyApply.FreeTheCable(mixer);
        Assert.Equal(EffectsRules.MasterApo, mixer.MasterMethod);
        EasyApply.MasterPlaysOn(mixer, "combo:9");
        Assert.Equal(new[] { "combo:9" }, mixer.PlaysOn);
    }

    [Fact]
    public async Task Actions_CarryOutEachFixIdThroughTheExistingStepsAndTheMixer()
    {
        var mixer = new EasyFakeMixer();
        var steps = new List<string>();
        var actions = new EasyActions(mixer, new EasySetupSteps(
            () => { steps.Add("install-apo"); return Task.CompletedTask; }, () => { steps.Add("device-selector"); return Task.CompletedTask; },
            () => { steps.Add("cable"); return Task.CompletedTask; }, () => steps.Add("more")));
        var facts = new EasyFacts { Microphones = new[] { new EasyOption("m1", "Headset") }, SuggestedMicrophoneName = "Combined microphone" };
        var a = new EasyAnswers { MicrophoneId = "m1", MicLook = "Warm" };
        a.Devices.AddRange(new[] { "m1", "m2" });
        a.Programs.Add("spotify");

        await actions.RunAsync(new EasyFix(EasyFixes.InstallApo, ""), a, facts);
        await actions.RunAsync(new EasyFix(EasyFixes.ChooseDevices, ""), a, facts);
        await actions.RunAsync(new EasyFix(EasyFixes.InstallCable, ""), a, facts);
        await actions.RunAsync(new EasyFix(EasyFixes.MoreCables, ""), a, facts);
        Assert.Equal(new[] { "install-apo", "device-selector", "cable", "more" }, steps);
        Assert.Equal(EffectsRules.MasterAutomatic, mixer.MasterMethod); // those four change no setting of ours

        await actions.RunAsync(new EasyFix(EasyFixes.ApplyMicrophone, ""), a, facts);
        Assert.True(mixer.IsFxOn(MixerService.InputPrefix + "m1"));
        await actions.RunAsync(new EasyFix(EasyFixes.ApplyProgramSounds, ""), a, facts);
        Assert.True(mixer.IsFxOn("spotify"));
        Assert.Equal("Combined microphone created.", await actions.RunAsync(new EasyFix(EasyFixes.CreateMicrophone, ""), a, facts));
        Assert.Equal(CombinedKind.Microphones, mixer.Combined.Single().Kind);
        await actions.RunAsync(new EasyFix(EasyFixes.FreeCable, ""), a, facts);
        Assert.Equal(EffectsRules.MasterApo, mixer.MasterMethod);
        Assert.Equal("That step is not available.", await actions.RunAsync(new EasyFix("nope", ""), a, facts));
    }

    // ---- the list of setups ----

    [Fact]
    public void EveryGoalHasASetup_EndingInSetupThenDone()
    {
        Assert.Equal(EasyGoals.All.Select(g => g.Id), EasyFlows.All.Select(f => f.GoalId));
        Assert.Null(EasyFlows.Of("volume"));
        Assert.All(EasyFlows.All, f =>
        {
            Assert.Equal(EasyStepKind.Setup, f.Steps[^2].Kind);
            Assert.Equal(EasyStepKind.Done, f.Steps[^1].Kind);
            Assert.All(f.Steps.Take(f.Steps.Count - 2), s => Assert.Equal(EasyStepKind.Question, s.Kind));
        });
    }

    // ---- the flow's screens ----

    [Fact]
    public void Flow_StripNamesTheScreens_AndTicksAsItGoes()
    {
        var rig = new EasyRig(EasyGoals.Better);
        Assert.Equal(new[] { "Goal", "Sound", "Engine", "Setup", "Done" }, rig.Flow.Strip.Select(s => s.Title));
        Assert.Equal(new[] { true, false, false, false, false }, rig.Flow.Strip.Select(s => s.IsDone));
        Assert.True(rig.Flow.Strip[1].IsCurrent);
        rig.Flow.NextCommand.Execute(null);
        Assert.True(rig.Flow.IsEngine);
        Assert.True(rig.Flow.Strip[2].IsCurrent);
        rig.Flow.NextCommand.Execute(null);
        Assert.True(rig.Flow.IsSetup);
        Assert.Equal(new[] { true, true, true, false, false }, rig.Flow.Strip.Select(s => s.IsDone));
        Assert.True(rig.Flow.Strip[3].IsCurrent);
    }

    [Fact]
    public void Flow_AQuestionBlocksNextUntilAnswered_AndSaysWhy()
    {
        var rig = new EasyRig(EasyGoals.PerApp, new EasyFacts { Programs = new[] { new EasyOption("spotify", "Spotify") } });
        Assert.False(rig.Flow.CanNext);
        Assert.False(string.IsNullOrEmpty(rig.Flow.NextTip));
        rig.Flow.NextCommand.Execute(null);
        Assert.True(rig.Flow.IsQuestion);

        var entry = rig.Flow.Question!.Choices.Shown.Single();
        rig.Flow.Question.Choices.Choose(entry);
        Assert.True(entry.IsChecked);
        Assert.True(rig.Flow.CanNext);
        rig.Flow.NextCommand.Execute(null);
        rig.Flow.NextCommand.Execute(null);
        Assert.True(rig.Flow.IsSetup);
        Assert.Contains("spotify", rig.Answers.Programs);
        Assert.Equal("Spotify", rig.Flow.Pictures.Single().Steps[0].Text); // a picture per ticked app, built from the goal's own parts
    }

    [Fact]
    public void Flow_BackWalksOneScreenAndTheFirstBackLeavesForTheCards_ChangingNothing()
    {
        var rig = new EasyRig(EasyGoals.Better, new EasyFacts { SpeakersApoWhy = "x", MasterStoredGains = Bass });
        rig.Flow.NextCommand.Execute(null);
        rig.Flow.BackCommand.Execute(null);
        Assert.True(rig.Flow.IsQuestion);
        Assert.Equal(0, rig.ToCards);
        rig.Flow.BackCommand.Execute(null);
        Assert.Equal(1, rig.ToCards);
        Assert.Empty(rig.Actions.Ran);
        Assert.Empty(rig.Asked);
    }

    [Fact]
    public void Flow_SetupCannotBeLeftUntilEveryRequiredLineIsDone()
    {
        var rig = new EasyRig(EasyGoals.Better, new EasyFacts { SpeakersApoWhy = "Equalizer APO is not on this device", MasterStoredGains = Bass });
        rig.Flow.NextCommand.Execute(null);
        rig.Flow.NextCommand.Execute(null);
        Assert.False(rig.Flow.CanNext);

        rig.Facts = rig.Facts with { SpeakersApoWhy = null, MasterFxOn = true, MasterGains = Bass }; // the user ticked the speakers in Device Selector
        rig.Flow.Reevaluate();
        Assert.All(rig.Flow.Checks.Where(c => !c.IsOptional), c => Assert.True(c.IsDone));
        Assert.True(rig.Flow.CanNext);
        rig.Flow.NextCommand.Execute(null);
        Assert.True(rig.Flow.IsDone);
        Assert.StartsWith("Everything you play now sounds like Bass boost", rig.Flow.DoneLines[0]);
    }

    [Fact]
    public async Task Flow_AFixThatAsksDoesNothingOnNo_AndRunsOnYes_ThenTheLinesTick()
    {
        var rig = new EasyRig(EasyGoals.TwoSpeakers, new EasyFacts
        {
            Speakers = new[] { new EasyOption("s1", "A"), new EasyOption("s2", "B") },
            CableForSpeakers = new EasyCableFact(null, null, StatusKind.Other, "CABLE Input", true),
        });
        rig.Answers.Devices.AddRange(new[] { "s1", "s2" });
        rig.Flow.Reevaluate();
        rig.Flow.NextCommand.Execute(null);
        rig.Flow.NextCommand.Execute(null);
        Assert.True(rig.Flow.IsSetup);
        var full = rig.Flow.Checks.Single(c => c.Id == "cable");
        Assert.True(full.HasFix);

        rig.Agree = false;
        await full.FixCommand.ExecuteAsync(null);
        Assert.Single(rig.Asked);
        Assert.Empty(rig.Actions.Ran);

        rig.Agree = true;
        rig.Actions.Effect = fix =>
        {
            rig.Facts = rig.Facts with { CableForSpeakers = new EasyCableFact("CABLE Input", "CABLE Output", StatusKind.Other, null, false) };
            return "The master now runs in Equalizer APO.";
        };
        await full.FixCommand.ExecuteAsync(null);
        Assert.Equal(new[] { EasyFixes.FreeCable }, rig.Actions.Ran);
        Assert.Equal("The master now runs in Equalizer APO.", rig.Flow.Note);
        Assert.True(rig.Flow.Checks.Single(c => c.Id == "cable").IsDone);
        Assert.Equal(CheckState.Todo, rig.Flow.Checks.Single(c => c.Id == "create").State);
    }

    [Fact]
    public async Task Flow_AFixThatIsNoLongerOffered_IsNotRun()
    {
        var rig = new EasyRig(EasyGoals.Better, new EasyFacts { SpeakersApoWhy = "x", MasterStoredGains = Bass });
        rig.Flow.NextCommand.Execute(null);
        rig.Flow.NextCommand.Execute(null);
        rig.Facts = rig.Facts with { ApoInstalled = false }; // removed again after the Engine step let it through
        rig.Flow.Reevaluate();
        var install = rig.Flow.Checks.Single(c => c.Id == "apo");
        rig.Facts = rig.Facts with { ApoInstalled = true }; // it got installed meanwhile
        await install.FixCommand.ExecuteAsync(null);
        Assert.Empty(rig.Actions.Ran);
    }

    [Fact]
    public void Flow_WhileThePcIsBeingLookedAt_NothingCanBeDone()
    {
        var rig = new EasyRig(EasyGoals.Better, new EasyFacts { Looking = true });
        rig.Flow.NextCommand.Execute(null);
        rig.Flow.NextCommand.Execute(null);
        Assert.Equal("looking", rig.Flow.Checks.Single().Id);
        Assert.False(rig.Flow.CanNext);
        Assert.False(rig.Flow.Checks.Single().HasFix);
    }

    [Fact]
    public void Flow_TheLastScreenLeadsToTheCardsOrThePage()
    {
        var rig = new EasyRig(EasyGoals.Better, new EasyFacts { MasterStoredGains = Bass, MasterGains = Bass, MasterFxOn = true });
        rig.Flow.NextCommand.Execute(null);
        rig.Flow.NextCommand.Execute(null);
        rig.Flow.NextCommand.Execute(null);
        Assert.True(rig.Flow.IsDone);
        rig.Flow.SetUpSomethingElseCommand.Execute(null);
        rig.Flow.GoToVolumeCommand.Execute(null);
        Assert.Equal((1, 1), (rig.ToCards, rig.ToPage));
    }

    [Fact]
    public void Flow_TheMicrophoneSetupHasTwoQuestions_AndTheNameKeepsTheSuggestionWhenEmpty()
    {
        var rig = new EasyRig(EasyGoals.Microphone, new EasyFacts { Microphones = new[] { new EasyOption("m1", "Headset") } });
        Assert.Equal(new[] { "Goal", "Microphone", "Look", "Engine", "Setup", "Done" }, rig.Flow.Strip.Select(s => s.Title));
        rig.Flow.Question!.Choices.Choose(rig.Flow.Question.Choices.Shown.Single());
        rig.Flow.NextCommand.Execute(null);
        Assert.True(rig.Flow.Question!.ShowsLooks);
        rig.Flow.Question.Looks.Single(l => l.Name == "Warm").PickCommand.Execute(null);
        Assert.Equal("Warm", rig.Answers.MicLook);

        var names = new EasyRig(EasyGoals.JoinMicrophones, new EasyFacts { SuggestedMicrophoneName = "Combined microphone 2" });
        Assert.Equal("Combined microphone 2", names.Flow.Question!.NamePlaceholder);
        Assert.Equal(string.Empty, names.Answers.Name);
    }
}
