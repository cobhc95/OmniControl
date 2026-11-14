using OmniControl.App.Audio;
using OmniControl.App.Pages.Volume;

namespace OmniControl.Tests;

/// <summary>
/// The numbered setups of the help table: the live number (<see cref="SoundModes.SetupNumber"/>) for every row, each row's "Works"
/// from the live facts, what choosing a setup sets, the Full control setup remembered and restored (with the fallback to setup 1),
/// and the table's highlight.
/// </summary>
public class SetupTests
{
    /// <summary>Full control on a PC with one allowed cable pair, Equalizer APO on the speakers, master effects, nothing on a cable yet.</summary>
    private static readonly HelpFacts OnePair = new(true, RunMethod.Bus, 1, StatusKind.Other, null, true, false, true, CableUse.None);

    private static HelpCombination Row(int number) => SoundModes.HelpCombinations.Single(r => r.Number == number);

    [Theory]
    [InlineData(false, RunMethod.Apo, CableUse.None, 8)]
    [InlineData(true, RunMethod.Bus, CableUse.None, 1)]
    [InlineData(true, RunMethod.Apo, CableUse.Program, 2)]
    [InlineData(true, RunMethod.Apo, CableUse.Microphone, 3)]
    [InlineData(true, RunMethod.Apo, CableUse.Speaker, 4)]
    [InlineData(true, RunMethod.Apo, CableUse.None, 5)]
    [InlineData(true, RunMethod.Bus, CableUse.Microphone, 6)]
    [InlineData(true, RunMethod.Bus, CableUse.Program, 6)]
    [InlineData(true, RunMethod.Apo, CableUse.ApoProgram, 7)]
    public void SetupNumber_ForEveryRow(bool full, RunMethod master, CableUse uses, int expected) =>
        Assert.Equal(expected, SoundModes.SetupNumber(OnePair with { Full = full, Master = master, Uses = uses }));

    [Fact]
    public void TheTable_HasTheEightRows_InOrder_WithTheMockUpsWords()
    {
        Assert.Equal(Enumerable.Range(1, 8), SoundModes.HelpCombinations.Select(r => r.Number));
        Assert.Equal("Cable bus (OmniControl)", Row(1).MasterRunsOn);
        Assert.Equal("Equalizer APO on the speakers", Row(2).MasterRunsOn);
        Assert.Equal("Kept free for another app", Row(5).CableUsedFor);
        Assert.Equal("APO only, no VST3; one speaker", Row(7).MasterEffects);
        Assert.Equal(4, SoundModes.HelpCombinations.Select(r => r.Group).Distinct().Count());
    }

    [Fact]
    public void Works_IsLive_AndNamesWhatIsMissing()
    {
        Assert.True(SoundModes.WorksOf(Row(1), OnePair).Available);
        Assert.True(SoundModes.WorksOf(Row(2), OnePair).Available);
        Assert.Equal("Needs a second cable pair", SoundModes.WorksOf(Row(6), OnePair).Text);
        Assert.Equal("Combine microphones first", SoundModes.WorksOf(Row(3), OnePair).Text);
        Assert.True(SoundModes.WorksOf(Row(3), OnePair with { CombinedMicrophone = true }).Available);
        Assert.Equal("Combine speakers first", SoundModes.WorksOf(Row(4), OnePair).Text);

        var noApo = OnePair with { SpeakersApoWhy = "Equalizer APO is not on this device" };
        Assert.Equal("Equalizer APO is not on this device", SoundModes.WorksOf(Row(2), noApo).Text);
        Assert.Equal("Equalizer APO is not on this device", SoundModes.WorksOf(Row(8), noApo).Text);
        Assert.True(SoundModes.WorksOf(Row(1), noApo).Available);
        Assert.True(SoundModes.WorksOf(Row(5), noApo with { MasterHasEffects = false }).Available); // no master effects: nothing needs installing

        var noCable = OnePair with { Pairs = 0, NoCable = StatusKind.NeedsCable };
        Assert.Equal("Needs VB-CABLE", SoundModes.WorksOf(Row(1), noCable).Text);
        Assert.True(SoundModes.WorksOf(Row(8), noCable).Available);
        Assert.True(SoundModes.WorksOf(Row(1), noCable with { NoCable = StatusKind.CablesOff, FreedPairs = 1 }).Available); // setup 5's cable comes back

        var twoPairs = OnePair with { Pairs = 2 };
        Assert.True(SoundModes.WorksOf(Row(6), twoPairs).Available);
        Assert.Equal("Equalizer APO is not on a cable", SoundModes.WorksOf(Row(7), twoPairs).Text);
        Assert.True(SoundModes.WorksOf(Row(7), twoPairs with { ApoOnACable = true }).Available);
    }

    [Fact]
    public void ChoosingASetup_SetsTheModeAndTheMastersMethod_InOneStep()
    {
        Assert.Equal(new SetupChoice(SoundModes.Full, SoundModes.MasterAutomatic, false), SoundModes.SetupOf(1));
        Assert.Equal(new SetupChoice(SoundModes.Full, SoundModes.MasterApo, false), SoundModes.SetupOf(2));
        Assert.Equal(new SetupChoice(SoundModes.Full, SoundModes.MasterApo, true), SoundModes.SetupOf(5));
        Assert.Equal(new SetupChoice(SoundModes.Full, SoundModes.MasterAutomatic, false), SoundModes.SetupOf(6));
        Assert.Equal(new SetupChoice(SoundModes.Full, SoundModes.MasterApo, false), SoundModes.SetupOf(7));
        Assert.Equal(SoundModes.Fast, SoundModes.SetupOf(8).Mode);
    }

    [Fact]
    public void Memory_RestoresTheLastSetup_OrFallsBackToSetup1_WithOneLine()
    {
        Assert.Equal((1, (string?)null), SoundModes.RestoreSetup(0, OnePair)); // first time: setup 1
        Assert.Equal((2, (string?)null), SoundModes.RestoreSetup(2, OnePair));
        var (number, note) = SoundModes.RestoreSetup(2, OnePair with { SpeakersApoWhy = "Equalizer APO is not on this device" });
        Assert.Equal(1, number);
        Assert.Equal("Setup 2 can't run now (Equalizer APO is not on this device), so Full control uses setup 1.", note);
        Assert.Equal(1, SoundModes.RestoreSetup(6, OnePair).Number); // the second cable pair was removed
        Assert.Equal(1, SoundModes.RestoreSetup(8, OnePair).Number); // never Fast's own number

        Assert.Equal(2, SoundModes.SetupToRemember(5, cablesKeptFree: false)); // an unused free cable is not "kept free"
        Assert.Equal(5, SoundModes.SetupToRemember(5, cablesKeptFree: true));
        Assert.Equal(3, SoundModes.SetupToRemember(3, cablesKeptFree: false));
    }

    [Fact]
    public void Table_HighlightsTheLiveSetup_GreysWhatCantRun_AndChoosesOnlyWhatWorks()
    {
        var chosen = new List<int>();
        var table = new SetupTable(chosen.Add);
        table.Sync(OnePair with { Master = RunMethod.Apo, Uses = CableUse.Program });
        var rows = table.Items.OfType<SetupRow>().ToList();
        Assert.Equal(4, table.Items.OfType<SetupGroupRow>().Count());
        Assert.Equal(new[] { 2 }, rows.Where(r => r.IsCurrent).Select(r => r.Number));
        var six = rows.Single(r => r.Number == 6);
        Assert.False(six.IsAvailable);
        Assert.Equal("Setup 6: Needs a second cable pair.", six.Tip);
        six.ChooseCommand.Execute(null);
        rows.Single(r => r.Number == 2).ChooseCommand.Execute(null); // the setup now: nothing to do
        rows.Single(r => r.Number == 1).ChooseCommand.Execute(null);
        Assert.Equal(new[] { 1 }, chosen);

        table.Sync(OnePair with { Full = false });
        Assert.Equal(new[] { 8 }, rows.Where(r => r.IsCurrent).Select(r => r.Number));
    }
}
