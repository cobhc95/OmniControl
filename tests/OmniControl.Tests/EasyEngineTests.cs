using OmniControl.App.Features.Volume.Pages.Easy;
using OmniControl.App.Features.Volume.Pages.Easy.Flow;

namespace OmniControl.Tests;

/// The Easy guided setups' Engine step: which way each goal allows, the two cards' live state, the
/// pre-selection, when Next waits, what the choice does to the checklists, and the remembered choice. Fakes only; nothing touches Windows.
public class EasyEngineTests
{
    private static EasyFacts Neither => new() { ApoInstalled = false, CablesReady = 0, CableInstalled = false };

    private static EasyRule Rule(string goalId) => new(EasyGoals.All.Single(g => g.Id == goalId).Engine);

    private readonly record struct EasyRule(EasyEngineRule? Rule);

    // ---- the goals decide which ways are offered ----

    [Fact]
    public void Goals_OneAndTwoOfferBothWays_ThreeToFiveOnlyTheCable()
    {
        foreach (var id in new[] { EasyGoals.Better, EasyGoals.PerApp })
        {
            var rule = Rule(id).Rule!;
            Assert.True(rule.Allows(EasyEngine.Apo));
            Assert.True(rule.Allows(EasyEngine.Cable));
        }
        var microphone = Rule(EasyGoals.Microphone).Rule!;
        Assert.Equal("Microphone effects go through a virtual cable", microphone.WhyNot(EasyEngine.Apo));
        Assert.True(microphone.Allows(EasyEngine.Cable));
        var join = Rule(EasyGoals.JoinMicrophones).Rule!;
        Assert.False(join.Allows(EasyEngine.Apo));
        Assert.Equal("Joining microphones needs a virtual cable", join.WhyNot(EasyEngine.Apo));
        Assert.True(join.Allows(EasyEngine.Cable));
        var speakers = Rule(EasyGoals.TwoSpeakers).Rule!;
        Assert.Equal("Several speakers need a virtual cable", speakers.WhyNot(EasyEngine.Apo));
        Assert.True(speakers.Allows(EasyEngine.Cable));
    }

    [Fact]
    public void TheEngineStep_SitsBeforeSetup_ForEveryGoalThatHasARule_AndOnlyThere()
    {
        foreach (var goal in EasyGoals.All)
        {
            var titles = EasyFlows.StepsOf(goal, EasyFlows.Of(goal.Id)!).Select(s => s.Title).ToList();
            Assert.Equal(goal.Engine is not null, titles.Contains("Engine"));
            Assert.Equal("Setup", titles[^2]);
            if (goal.Engine is not null) Assert.Equal("Engine", titles[^3]);
        }
        Assert.Equal(new[] { "Goal", "Microphones", "Engine", "Setup", "Done" }, new EasyRig(EasyGoals.JoinMicrophones).Flow.Strip.Select(s => s.Title));
    }

    [Fact]
    public void CardsForJoiningMicrophones_GreyTheApoCardWithItsReason_AndDoNotOfferItsInstaller()
    {
        var rig = new EasyRig(EasyGoals.JoinMicrophones, Neither with { Microphones = new[] { new EasyOption("a", "A"), new EasyOption("b", "B") } });
        rig.Answers.Devices.AddRange(new[] { "a", "b" });
        rig.Flow.Reevaluate();
        rig.AtEngine();
        var apo = rig.Card(EasyEngine.Apo);
        Assert.False(apo.IsAvailable);
        Assert.False(apo.IsSelected);
        Assert.False(apo.CanGet);
        Assert.Equal("Joining microphones needs a virtual cable", apo.Facts);
        Assert.False(apo.PickCommand.CanExecute(null));
        Assert.True(rig.Card(EasyEngine.Cable).IsSelected);
    }

    // ---- live state of the cards ----

    [Fact]
    public void Cards_ShowInstalledOrNot_AndTheGetButtonsGreyOnceTheyAreThere()
    {
        var notThere = EasyEngines.Cards(Rule(EasyGoals.Better).Rule!, Neither);
        Assert.Equal(("Not installed", false, true), (notThere[0].Tag, notThere[0].TagIsGood, notThere[0].CanGet));
        Assert.Equal(("No virtual cable installed", false, true), (notThere[1].Tag, notThere[1].TagIsGood, notThere[1].CanGet));
        Assert.Equal(new[] { EasyFixes.InstallApo, EasyFixes.InstallCable }, notThere.Select(c => c.Get!.Id));

        var there = EasyEngines.Cards(Rule(EasyGoals.Better).Rule!, new EasyFacts { CablesReady = 2 });
        Assert.Equal(("Installed", true, false), (there[0].Tag, there[0].TagIsGood, there[0].CanGet));
        Assert.Equal(("2 cables ready", true, false), (there[1].Tag, there[1].TagIsGood, there[1].CanGet));
        Assert.Equal("1 cable ready", EasyEngines.Cards(Rule(EasyGoals.Better).Rule!, new EasyFacts())[1].Tag);

        var off = EasyEngines.Cards(Rule(EasyGoals.Better).Rule!, new EasyFacts { CablesReady = 0 })[1];
        Assert.Equal((EffectsRules.CablesOff, false, false), (off.Tag, off.TagIsGood, off.CanGet));
    }

    [Fact]
    public void WhenAnInstallFinishes_TheTagTurnsGoodAndTheButtonGreysByItself()
    {
        var rig = new EasyRig(EasyGoals.Better, Neither).AtEngine();
        var apo = rig.Card(EasyEngine.Apo);
        Assert.Equal("Not installed", apo.Tag);
        Assert.True(apo.GetCommand.CanExecute(null));
        Assert.False(rig.Flow.CanNext);

        rig.Facts = rig.Facts with { ApoInstalled = true };
        rig.Flow.Reevaluate();
        Assert.Equal("Installed", apo.Tag);
        Assert.True(apo.TagIsGood);
        Assert.False(apo.GetCommand.CanExecute(null));
        Assert.True(rig.Flow.CanNext);
    }

    [Fact]
    public async Task TheGetButton_RunsTheExistingInstallerStep_OnlyWhenPressed()
    {
        var rig = new EasyRig(EasyGoals.Better, Neither).AtEngine();
        Assert.Empty(rig.Actions.Ran);
        await rig.Card(EasyEngine.Apo).GetCommand.ExecuteAsync(null);
        await rig.Card(EasyEngine.Cable).GetCommand.ExecuteAsync(null);
        Assert.Equal(new[] { EasyFixes.InstallApo, EasyFixes.InstallCable }, rig.Actions.Ran);
    }

    // ---- the pre-selection ----

    [Fact]
    public void PreSelection_BothInstalledGivesApo_OnlyACableGivesTheCable_NothingGivesApo_AGoalThatRestrictsWins()
    {
        var both = Rule(EasyGoals.Better).Rule!;
        Assert.Equal(EasyEngine.Apo, EasyEngines.Default(both, new EasyFacts()));
        Assert.Equal(EasyEngine.Cable, EasyEngines.Default(both, new EasyFacts { ApoInstalled = false }));
        Assert.Equal(EasyEngine.Apo, EasyEngines.Default(both, new EasyFacts { CablesReady = 0 }));
        Assert.Equal(EasyEngine.Apo, EasyEngines.Default(both, Neither));
        Assert.Equal(EasyEngine.Cable, EasyEngines.Default(Rule(EasyGoals.TwoSpeakers).Rule!, new EasyFacts()));
        Assert.Equal(EasyEngine.Cable, EasyEngines.Resolve(EasyEngine.Apo, Rule(EasyGoals.JoinMicrophones).Rule!, new EasyFacts())); // a stale pick the goal refuses
    }

    [Fact]
    public void TheStep_PreSelectsOnce_AndAnInstallLaterDoesNotMoveThePick()
    {
        var rig = new EasyRig(EasyGoals.Better, new EasyFacts { ApoInstalled = false }).AtEngine();
        Assert.True(rig.Card(EasyEngine.Cable).IsSelected);
        Assert.Empty(rig.Chosen); // a pre-selection is not a choice, so it is not remembered
        rig.Facts = rig.Facts with { ApoInstalled = true };
        rig.Flow.Reevaluate();
        Assert.True(rig.Card(EasyEngine.Cable).IsSelected);
    }

    // ---- Next ----

    [Fact]
    public void Next_IsOffWhileTheChosenWayIsNotThere_WithOneLineWhy_AndEitherWayCanStillBePicked()
    {
        var rig = new EasyRig(EasyGoals.Better, new EasyFacts { ApoInstalled = false, CablesReady = 0, CableInstalled = true }).AtEngine();
        Assert.False(rig.Flow.CanNext);
        Assert.Equal("Install Equalizer APO first.", rig.Flow.NextTip);
        Assert.Equal(rig.Flow.NextTip, rig.Flow.EngineProblem);
        Assert.True(rig.Flow.HasEngineProblem);

        Assert.True(rig.Card(EasyEngine.Cable).PickCommand.CanExecute(null));
        rig.Card(EasyEngine.Cable).PickCommand.Execute(null);
        Assert.False(rig.Flow.CanNext);
        Assert.StartsWith(EffectsRules.CablesOff, rig.Flow.NextTip);

        rig.Facts = rig.Facts with { CablesReady = 1 };
        rig.Flow.Reevaluate();
        Assert.True(rig.Flow.CanNext);
        Assert.False(rig.Flow.HasEngineProblem);
        Assert.Equal("Install a virtual cable first.", EasyEngines.NextWhy(EasyEngine.Cable, Neither));
    }

    // ---- what the choice does to the checklists ----

    [Fact]
    public void Better_ApoWayChecksTheSpeakers_TheCableWayChecksTheCableBus()
    {
        var f = new EasyFacts { SpeakersApoWhy = "Equalizer APO is not on this device", MasterStoredGains = EasyEq.Presets[1].Gains };
        var apo = EasyChecks.Better(new EasyAnswers { Engine = EasyEngine.Apo }, f);
        Assert.Contains(apo, l => l.Id == "speakers");
        Assert.DoesNotContain(apo, l => l.Id == "engine");

        var cable = EasyChecks.Better(new EasyAnswers { Engine = EasyEngine.Cable }, f);
        Assert.DoesNotContain(cable, l => l.Id is "apo" or "speakers");
        var engine = cable.Single(l => l.Id == "engine");
        Assert.Equal(CheckState.Todo, engine.State);
        Assert.Equal(EasyFixes.UseOwnEngine, engine.Fix!.Id);
        Assert.Null(engine.Fix.Confirm); // a method that is only changed back: nothing to confirm
        Assert.Equal(CheckState.Blocked, cable.Single(l => l.Id == "apply").State);
    }

    [Fact]
    public void Better_CableWayWithApoAsTheMasterMethod_OffersTheCableBus_AndWhenOnTheBusIsDone()
    {
        var inFull = EasyChecks.Better(new EasyAnswers { Engine = EasyEngine.Cable }, new EasyFacts { MasterOnBus = false });
        var engine = inFull.Single(l => l.Id == "engine");
        Assert.Equal(EasyFixes.UseOwnEngine, engine.Fix!.Id);

        var onBus = EasyChecks.Better(new EasyAnswers { Engine = EasyEngine.Cable }, new EasyFacts { MasterOnBus = true });
        Assert.True(onBus.Single(l => l.Id == "engine").IsDone);
    }

    [Fact]
    public void Better_CableWithNoAllowedCable_IsBlockedWithTheReason_AndTheApoWayOnTheBusAsksForTheMethodChange()
    {
        var blocked = EasyChecks.Better(new EasyAnswers { Engine = EasyEngine.Cable }, new EasyFacts { CablesReady = 0, CableInstalled = false });
        Assert.Equal(CheckState.Blocked, blocked.Single(l => l.Id == "engine").State);

        var apoOnBus = EasyChecks.Better(new EasyAnswers { Engine = EasyEngine.Apo }, new EasyFacts { MasterOnBus = true });
        var apoMaster = apoOnBus.Single(l => l.Id == "apo-master");
        Assert.Equal(EasyFixes.FreeCable, apoMaster.Fix!.Id);
        Assert.NotNull(apoMaster.Fix.Confirm);
    }

    [Fact]
    public void PerApp_TheChoiceDecidesTheMasterLine_AndTheProgramsAreCheckedEitherWay()
    {
        var a = new EasyAnswers { Engine = EasyEngine.Cable };
        a.Programs.Add("spotify");
        var cable = EasyChecks.PerApp(a, new EasyFacts());
        Assert.Equal(new[] { "engine", "works", "own" }, cable.Select(l => l.Id));
        Assert.Equal(CheckState.Blocked, cable.Single(l => l.Id == "own").State); // waits for the engine line

        a = new EasyAnswers { Engine = EasyEngine.Apo };
        a.Programs.Add("spotify");
        Assert.Equal(new[] { "works", "own" }, EasyChecks.PerApp(a, new EasyFacts()).Select(l => l.Id));
    }

    [Fact]
    public void Microphone_AlwaysTheCableWay_ACableThenApply()
    {
        var a = new EasyAnswers { Engine = EasyEngine.Cable, MicrophoneId = "m1" };
        var lines = EasyChecks.Microphone(a, new EasyFacts());
        Assert.Equal(new[] { "cable", "apply" }, lines.Select(l => l.Id));
        Assert.Equal(CheckState.Done, lines[0].State);
        Assert.Equal(CheckState.Todo, lines[1].State);
        Assert.False(EasyGoals.All.Single(g => g.Id == EasyGoals.Microphone).Engine!.Allows(EasyEngine.Apo));
    }

    // ---- remembered ----

    [Fact]
    public void ThePick_IsReportedForMemory_AndAGoalOpenedAgainStartsOnIt()
    {
        var first = new EasyRig(EasyGoals.Better).AtEngine();
        first.Card(EasyEngine.Cable).PickCommand.Execute(null);
        Assert.Equal(new[] { EasyEngine.Cable }, first.Chosen);
        Assert.True(first.Card(EasyEngine.Cable).IsSelected);
        Assert.False(first.Card(EasyEngine.Apo).IsSelected);

        var again = new EasyRig(EasyGoals.Better, remembered: first.Chosen[^1]).AtEngine();
        Assert.True(again.Card(EasyEngine.Cable).IsSelected);
        Assert.Empty(again.Chosen);
    }

    [Fact]
    public void BackFromSetup_ReturnsToTheEngineStepWithTheChoiceKept()
    {
        var rig = new EasyRig(EasyGoals.Better).AtEngine();
        rig.Card(EasyEngine.Cable).PickCommand.Execute(null);
        rig.Flow.NextCommand.Execute(null);
        Assert.True(rig.Flow.IsSetup);
        rig.Flow.BackCommand.Execute(null);
        Assert.True(rig.Flow.IsEngine);
        Assert.True(rig.Card(EasyEngine.Cable).IsSelected);
    }
}
