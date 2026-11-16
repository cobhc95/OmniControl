using System.Collections.ObjectModel;
using OmniControl.App.Audio;
using OmniControl.App.Pages.Volume.Easy;
using OmniControl.App.Pages.Volume.Easy.Flow;

namespace OmniControl.Tests;

/// <summary>
/// The Easy welcome with several goals (docs/mockups/easy-cards-multiselect.html): the five cards (no "Just the volume"), the tick rules and
/// Next gating, the "What you will need" box for every kind of selection, and the setups run one after another with the engine chosen once.
/// Fakes only; nothing touches Windows.
/// </summary>
public class EasyMultiSelectTests
{
    private const string Join = EasyGoals.JoinMicrophones, Speakers = EasyGoals.TwoSpeakers;

    private static EasyCheck Line(IReadOnlyList<EasyCheck> lines, string id) => lines.Single(l => l.Id == id);

    private static EasyFacts Cables(int ready, bool installed = true) => new() { CablesReady = ready, CableInstalled = installed };

    // ---- five cards ----

    [Fact]
    public void ThereAreFiveGoals_NoneIsJustTheVolume_AndEachHasASetup()
    {
        Assert.Equal(5, EasyGoals.All.Count);
        Assert.DoesNotContain(EasyGoals.All, g => g.Id == "volume" || g.Title == "Just the volume");
        Assert.Equal(EasyGoals.All.Select(g => g.Id), EasyFlows.All.Select(f => f.GoalId));
        Assert.All(EasyGoals.All, g => Assert.False(string.IsNullOrWhiteSpace(g.Short)));
    }

    // ---- ticking ----

    [Fact]
    public void Selection_NextIsOffUntilAGoalIsTicked_AndTogglingUnticks()
    {
        var selection = new EasySelection();
        Assert.False(selection.CanStart);
        Assert.True(selection.Toggle(EasyGoals.Microphone));
        Assert.True(selection.CanStart);
        Assert.False(selection.Toggle(EasyGoals.Microphone));
        Assert.False(selection.CanStart);
        selection.Toggle(Speakers);
        selection.Clear();
        Assert.False(selection.CanStart);
    }

    [Fact]
    public void Selection_RunsInTheOrderOfTheCards_NotTheOrderTicked()
    {
        var selection = new EasySelection();
        selection.Toggle(Speakers);
        selection.Toggle(EasyGoals.Better);
        selection.Toggle(Join);
        Assert.Equal(new[] { EasyGoals.Better, Join, Speakers }, selection.Goals.Select(g => g.Id));
    }

    // ---- What you will need ----

    [Fact]
    public void Needs_NothingTicked_SaysToPickAGoal()
    {
        var lines = EasyNeeds.Of(Array.Empty<string>(), new EasyFacts());
        var only = Assert.Single(lines);
        Assert.True(only.Note);
        Assert.Equal(EasyNeeds.PickAGoal, only.Title);
    }

    [Fact]
    public void Needs_GoalsOneAndTwo_AreApoOrACable_WithApoStateAndNoCableLine()
    {
        var ids = new[] { EasyGoals.Better, EasyGoals.PerApp };
        var lines = EasyNeeds.Of(ids, new EasyFacts { ApoInstalled = false, CablesReady = 0, CableInstalled = false });
        Assert.Equal(new[] { "way", "apo" }, lines.Select(l => l.Id));
        Assert.Equal("Equalizer APO, or a virtual cable", Line(lines, "way").Title);
        Assert.Contains("you choose on the next step", Line(lines, "way").Subtitle);
        Assert.Equal(CheckState.Todo, Line(lines, "way").State);
        Assert.Equal("not installed", Line(lines, "apo").Live);
        Assert.Equal(EasyFixes.InstallApo, Line(lines, "apo").Fix!.Id);

        var installed = EasyNeeds.Of(ids, new EasyFacts { ApoInstalled = true });
        Assert.Equal(CheckState.Done, Line(installed, "apo").State);
        Assert.Null(Line(installed, "apo").Fix);
        Assert.Equal("installed", Line(installed, "apo").Live);
        Assert.Equal(CheckState.Done, Line(installed, "way").State);
    }

    [Fact]
    public void Needs_ACableAloneIsEnoughForTheFirstTwo()
    {
        var lines = EasyNeeds.Of(new[] { EasyGoals.Better }, new EasyFacts { ApoInstalled = false, CablesReady = 1 });
        Assert.Equal(CheckState.Done, Line(lines, "way").State);
        Assert.Equal(CheckState.Todo, Line(lines, "apo").State);
    }

    [Fact]
    public void Needs_JoinMicrophonesAlone_OneCable_ApoNotNeeded()
    {
        var lines = EasyNeeds.Of(new[] { Join }, Cables(0, installed: false));
        Assert.Equal(new[] { "cables", "apo-not-needed", "shortage" }, lines.Select(l => l.Id));
        var cable = Line(lines, "cables");
        Assert.Equal("1 virtual cable", cable.Title);
        Assert.Equal("to join the microphones", cable.Subtitle);
        Assert.Equal("0 installed", cable.Live);
        Assert.Equal(CheckState.Todo, cable.State);
        Assert.Equal(EasyFixes.InstallCable, cable.Fix!.Id);
        Assert.Equal(EasyNeeds.GetCablesLabel, cable.Fix.Label);
        Assert.Equal("Equalizer APO is not needed", Line(lines, "apo-not-needed").Title);
        Assert.DoesNotContain(lines, l => l.Id == "full");

        var enough = EasyNeeds.Of(new[] { Join }, Cables(1));
        Assert.Equal(new[] { "cables", "apo-not-needed" }, enough.Select(l => l.Id));
        Assert.Equal(CheckState.Done, Line(enough, "cables").State);
    }

    [Fact]
    public void Needs_TwoSpeakers_NeedOneCable_AndNothingElse()
    {
        var lines = EasyNeeds.Of(new[] { Speakers }, Cables(1));
        Assert.Equal(new[] { "cables", "apo-not-needed" }, lines.Select(l => l.Id));
        Assert.Equal("for the speakers", Line(lines, "cables").Subtitle);
    }

    [Fact]
    public void Needs_FourAndFive_AreTwoPairs_WithAShortageNoteWhenThereIsOne()
    {
        var one = EasyNeeds.Of(new[] { Join, Speakers }, Cables(1));
        var cable = Line(one, "cables");
        Assert.Equal("2 virtual cables", cable.Title);
        Assert.Equal("one to join the microphones, one for the speakers", cable.Subtitle);
        Assert.Equal("1 ready", cable.Live);
        Assert.Equal(CheckState.Todo, cable.State);
        Assert.Equal(EasyFixes.MoreCables, cable.Fix!.Id);
        var note = Line(one, "shortage");
        Assert.True(note.Note);
        Assert.Contains("With your 1 cable you can do some of these now", note.Title);
        Assert.Contains("Next stays on", note.Title);

        var two = EasyNeeds.Of(new[] { Join, Speakers }, Cables(2));
        Assert.Equal(CheckState.Done, Line(two, "cables").State);
        Assert.Null(Line(two, "cables").Fix);
        Assert.DoesNotContain(two, l => l.Id == "shortage");
    }

    [Fact]
    public void Needs_NoCableAtAll_SaysSo_AndInstalledButSwitchedOffIsAskedForMoreCables()
    {
        var none = EasyNeeds.Of(new[] { Join, Speakers }, Cables(0, installed: false));
        Assert.Equal(EasyFixes.InstallCable, Line(none, "cables").Fix!.Id);
        Assert.Contains("None of these can finish", Line(none, "shortage").Title);

        var off = EasyNeeds.Of(new[] { Join }, Cables(0, installed: true));
        Assert.Equal("0 allowed", Line(off, "cables").Live);
        Assert.Equal(EasyFixes.MoreCables, Line(off, "cables").Fix!.Id);
    }

    [Fact]
    public void Needs_Mixed_ShowsBothApoAndTheCable_AndNotesTheMaster()
    {
        var lines = EasyNeeds.Of(new[] { EasyGoals.Better, Join }, new EasyFacts { ApoInstalled = false, CablesReady = 1 });
        Assert.Equal(new[] { "apo", "cables", "mixed" }, lines.Select(l => l.Id));
        Assert.Equal(CheckState.Todo, Line(lines, "apo").State);
        Assert.Equal(CheckState.Done, Line(lines, "cables").State);
        Assert.Contains("Equalizer APO", Line(lines, "mixed").Title);
        Assert.DoesNotContain(lines, l => l.Id == "apo-not-needed");
    }

    [Fact]
    public void Needs_IsOneFunctionOfTheTicks_NotOfTheirOrder_AndWaitsWhileLooking()
    {
        var facts = Cables(1);
        var a = EasyNeeds.Of(new[] { Join, EasyGoals.Better }, facts);
        var b = EasyNeeds.Of(new[] { EasyGoals.Better, Join }, facts);
        Assert.Equal(a.Select(l => (l.Id, l.Title, l.State, l.Live)), b.Select(l => (l.Id, l.Title, l.State, l.Live)));
        Assert.Equal(EasyNeeds.Looking, Assert.Single(EasyNeeds.Of(new[] { Join }, new EasyFacts { Looking = true })).Title);
    }

    [Fact]
    public void NeedsOnScreen_UpdateInPlace_WhenAnInstallFinishes()
    {
        var items = new ObservableCollection<EasyCheckItem>();
        var ids = new[] { EasyGoals.Better };
        EasyCheckItem.Sync(items, EasyNeeds.Of(ids, new EasyFacts { ApoInstalled = false, CablesReady = 0 }), _ => Task.CompletedTask);
        var apo = items.Single(i => i.Id == "apo");
        Assert.True(apo.HasFix);
        EasyCheckItem.Sync(items, EasyNeeds.Of(ids, new EasyFacts { ApoInstalled = true }), _ => Task.CompletedTask);
        Assert.Same(apo, items.Single(i => i.Id == "apo"));
        Assert.False(apo.HasFix);
        Assert.True(apo.IsDone);
        Assert.Equal("installed", apo.Live);
    }

    // ---- the setups one after another ----

    private sealed class Actions : IEasyActions
    {
        public Task<string?> RunAsync(EasyFix fix, EasyAnswers answers, EasyFacts facts) => Task.FromResult<string?>(null);
    }

    private sealed class Run
    {
        public EasyFacts Facts { get; set; }
        public List<EasyFlowPlace> Places { get; } = new();
        public List<EasyAnswers> Answers { get; } = new();
        public EasySummary? Summary { get; private set; }
        public EasySequence Sequence { get; }
        public EasyFlowViewModel Flow => Sequence.Current!;

        public Run(IReadOnlyList<string> goalIds, EasyFacts? facts = null)
        {
            Facts = facts ?? new EasyFacts();
            var services = new EasyFlowServices(() => Facts, new Actions(), _ => Task.FromResult(true));
            Sequence = new EasySequence(goalIds.Select(id => EasyGoals.All.Single(g => g.Id == id)).ToList(),
                (goal, answers, place) =>
                {
                    Places.Add(place);
                    Answers.Add(answers);
                    return new EasyFlowViewModel(goal, EasyFlows.Of(goal.Id)!, answers, services,
                        (kind, changed) => new EasyQuestionViewModel(kind, answers, changed, null, null), () => { }, place.Finish, null, place);
                }, _ => { }, summary => Summary = summary);
            Sequence.Start();
        }

        /// <summary>Presses Next until the flow reaches the Engine screen, picks a way there and leaves it.</summary>
        public void ChooseEngine(EasyEngine engine)
        {
            while (!Flow.IsEngine) Flow.NextCommand.Execute(null);
            Flow.EngineCards.Single(c => c.Engine == engine).PickCommand.Execute(null);
            Flow.NextCommand.Execute(null);
        }
    }

    private static EasyFacts TwoMics => new() { Microphones = new[] { new EasyOption("a", "A"), new EasyOption("b", "B") } };

    [Fact]
    public void Sequence_RunsTheGoalsInOrder_WithTheirPlaceAndName()
    {
        var run = new Run(new[] { EasyGoals.Better, EasyGoals.Microphone, Join });
        Assert.Equal(EasyGoals.Better, run.Sequence.CurrentGoal!.Id);
        Assert.Equal("Make everything sound better (goal 1 of 3)", run.Flow.Strip[0].Title);
        Assert.True(run.Flow.IsSequence);
        Assert.True(run.Flow.HasNextGoal);
        run.Flow.SkipGoalCommand.Execute(null);
        Assert.Equal(EasyGoals.Microphone, run.Sequence.CurrentGoal!.Id);
        Assert.Equal("Improve my microphone (goal 2 of 3)", run.Flow.Strip[0].Title);
        run.Flow.NextGoalCommand.Execute(null);
        Assert.Equal(Join, run.Sequence.CurrentGoal!.Id);
        Assert.Equal(new[] { 1, 2, 3 }, run.Places.Select(p => p.Number));
    }

    [Fact]
    public void ASingleGoal_BehavesAsBefore_StripGoalEngineStepAndNoSkip()
    {
        var run = new Run(new[] { Join }, TwoMics);
        Assert.Equal(new[] { "Goal", "Microphones", "Engine", "Setup", "Done" }, run.Flow.Strip.Select(s => s.Title));
        Assert.False(run.Flow.IsSequence);
        Assert.False(run.Flow.HasNextGoal);
    }

    [Fact]
    public void TheEngineIsAskedOnce_ALaterGoalReusesACompatibleChoice()
    {
        var run = new Run(new[] { EasyGoals.Better, EasyGoals.PerApp });
        Assert.Contains("Engine", run.Flow.Strip.Select(s => s.Title));
        run.ChooseEngine(EasyEngine.Cable);
        Assert.Equal(EasyEngine.Cable, run.Flow.SettledEngine);
        run.Flow.NextGoalCommand.Execute(null);
        Assert.DoesNotContain("Engine", run.Flow.Strip.Select(s => s.Title));
        Assert.Equal(EasyEngine.Cable, run.Answers[1].Engine);
    }

    [Fact]
    public void AGoalThatAcceptedTheDefaultWay_AlsoCarriesIt()
    {
        var run = new Run(new[] { EasyGoals.Better, EasyGoals.PerApp });
        while (!run.Flow.IsEngine) run.Flow.NextCommand.Execute(null);
        run.Flow.NextCommand.Execute(null);
        run.Flow.NextGoalCommand.Execute(null);
        Assert.Equal(EasyEngine.Apo, run.Answers[1].Engine);
        Assert.DoesNotContain("Engine", run.Flow.Strip.Select(s => s.Title));
    }

    [Fact]
    public void TheCarriedWay_IsReusedOnlyByAGoalThatAllowsIt()
    {
        var join = EasyGoals.All.Single(g => g.Id == Join);
        Assert.False(EasySequence.Reuses(join, EasyEngine.Apo));
        Assert.True(EasySequence.Reuses(join, EasyEngine.Cable));
        Assert.False(EasySequence.Reuses(join, null));
        Assert.True(EasySequence.Reuses(EasyGoals.All.Single(g => g.Id == EasyGoals.Better), EasyEngine.Apo));
        Assert.False(EasySequence.Reuses(EasyGoals.All.Single(g => g.Id == EasyGoals.Microphone), EasyEngine.Apo));
    }

    [Fact]
    public void GoalsWithNothingToChoose_NeverShowTheEngineScreen_InARun()
    {
        var run = new Run(new[] { Join, Speakers }, TwoMics);
        Assert.DoesNotContain("Engine", run.Flow.Strip.Select(s => s.Title));
        run.Flow.NextGoalCommand.Execute(null);
        Assert.DoesNotContain("Engine", run.Flow.Strip.Select(s => s.Title));
    }

    [Fact]
    public void ASkippedGoal_AppliesNothing_AndTheSummaryNamesWhatWasAndWasNotSetUp()
    {
        var run = new Run(new[] { EasyGoals.Better, EasyGoals.Microphone });
        Assert.Equal("Continue with the next goal", run.Flow.SkipLabel);
        run.Flow.SkipGoalCommand.Execute(null);
        Assert.Equal("Finish without this goal", run.Flow.SkipLabel);
        Assert.Null(run.Summary);
        run.Flow.GoToVolumeCommand.Execute(null);
        Assert.Equal(new[] { EasyGoals.Microphone }, run.Summary!.Completed.Select(r => r.Goal.Id));
        Assert.Equal(new[] { EasyGoals.Better }, run.Summary.Skipped.Select(g => g.Id));
        Assert.Equal("Set up: Microphone. Not set up: Sound.", run.Summary.Text);
    }

    [Fact]
    public void FinishingEveryGoal_SaysSetUpWithTheShortNamesInOrder()
    {
        var run = new Run(new[] { EasyGoals.Better, EasyGoals.Microphone });
        run.Flow.NextGoalCommand.Execute(null);
        run.Flow.GoToVolumeCommand.Execute(null);
        Assert.Equal("Set up: Sound, Microphone.", run.Summary!.Text);
        Assert.Empty(run.Summary.Skipped);
    }

    [Fact]
    public void SkippingTheLastGoal_Finishes_AndASingleGoalIsNotASequence()
    {
        var run = new Run(new[] { EasyGoals.Better, EasyGoals.Microphone });
        run.Flow.SkipGoalCommand.Execute(null);
        run.Flow.SkipGoalCommand.Execute(null);
        Assert.Equal("Nothing was set up.", run.Summary!.Text);
        Assert.False(new Run(new[] { EasyGoals.Better }).Flow.IsSequence);
    }

    [Fact]
    public void AGoalThatCannotBeFinished_SaysWhy_InARunOnly()
    {
        var facts = new EasyFacts { CableForSpeakers = new EasyCableFact(null, null, StatusKind.CablesOff, null, false), Speakers = new[] { new EasyOption("a", "A"), new EasyOption("b", "B") } };
        var run = new Run(new[] { Speakers, EasyGoals.Better }, facts);
        run.Answers[0].Devices.AddRange(new[] { "a", "b" });
        run.Flow.Reevaluate();
        while (!run.Flow.IsSetup) run.Flow.NextCommand.Execute(null);
        Assert.True(run.Flow.HasStuckNote);
        Assert.Contains(EffectsRules.CablesOff, run.Flow.StuckNote);
        Assert.False(run.Flow.CanNext);
        run.Flow.SkipGoalCommand.Execute(null); // "Continue with the next goal": nothing was applied for this one
        Assert.Equal(EasyGoals.Better, run.Sequence.CurrentGoal!.Id);

        var single = new Run(new[] { Speakers }, facts);
        single.Answers[0].Devices.AddRange(new[] { "a", "b" });
        single.Flow.Reevaluate();
        while (!single.Flow.IsSetup) single.Flow.NextCommand.Execute(null);
        Assert.False(single.Flow.HasStuckNote);
    }

    [Fact]
    public void Disposing_StopsTheRun_AndShowsNoFlow()
    {
        var shown = new List<EasyFlowViewModel?>();
        var services = new EasyFlowServices(() => new EasyFacts(), new Actions(), _ => Task.FromResult(true));
        var sequence = new EasySequence(new[] { EasyGoals.All[0] },
            (goal, answers, place) => new EasyFlowViewModel(goal, EasyFlows.Of(goal.Id)!, answers, services,
                (kind, changed) => new EasyQuestionViewModel(kind, answers, changed, null, null), () => { }, () => { }, null, place),
            shown.Add, _ => { });
        sequence.Start();
        sequence.Dispose();
        Assert.Null(sequence.Current);
        Assert.Null(shown[^1]);
    }
}
