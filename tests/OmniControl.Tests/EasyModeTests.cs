using System.Text.Json;
using OmniControl.App.Audio;
using OmniControl.App.Pages.Volume.Easy;
using OmniControl.App.Services;

namespace OmniControl.Tests;

/// <summary>The Volume page's Easy mode: which view the first start picks and how the choice is stored, the five-slider equaliser mapping, and the goal list.</summary>
public class EasyModeTests
{
    // ---- the first start and the stored choice ----

    [Fact]
    public void FirstRun_NewInstall_StartsInEasyWithTheWelcome()
    {
        var settings = new AppSettings();
        Assert.True(FirstRun.Decide(settings, earlierFilesExisted: false));
        Assert.Equal(VolumeView.Easy, settings.VolumeView);
        Assert.False(settings.EasyWelcomeDone);
    }

    [Fact]
    public void FirstRun_ExistingSettings_StayInAdvancedWithoutTheWelcome()
    {
        var settings = new AppSettings();
        Assert.True(FirstRun.Decide(settings, earlierFilesExisted: true));
        Assert.Equal(VolumeView.Advanced, settings.VolumeView);
        Assert.True(settings.EasyWelcomeDone);
    }

    [Theory]
    [InlineData(VolumeView.Easy, true)]
    [InlineData(VolumeView.Advanced, false)]
    public void FirstRun_DecidesOnlyOnce(VolumeView chosen, bool existed)
    {
        var settings = new AppSettings { VolumeView = chosen, EasyWelcomeDone = true };
        Assert.False(FirstRun.Decide(settings, existed));
        Assert.Equal(chosen, settings.VolumeView);
    }

    [Fact]
    public void FirstRun_AFileWrittenByAnOlderVersion_HasNoChoiceYet()
    {
        var older = JsonSerializer.Deserialize<AppSettings>("{\"Accent\":\"Orange\"}")!;
        Assert.Equal(VolumeView.Undecided, older.VolumeView);
        Assert.True(FirstRun.Decide(older, earlierFilesExisted: true));
        Assert.Equal(VolumeView.Advanced, older.VolumeView);
    }

    [Theory]
    [InlineData(VolumeView.Easy)]
    [InlineData(VolumeView.Advanced)]
    public void TheViewChoice_SurvivesSavingAndLoading(VolumeView chosen)
    {
        var saved = JsonSerializer.Serialize(new AppSettings { VolumeView = chosen, EasyWelcomeDone = true });
        var loaded = JsonSerializer.Deserialize<AppSettings>(saved)!;
        Assert.Equal(chosen, loaded.VolumeView);
        Assert.True(loaded.EasyWelcomeDone);
    }

    // ---- the five sliders and the equaliser ----

    [Fact]
    public void FlatGains_MakeFlatBands_AndReadBackFlat()
    {
        var bands = EasyEq.Bands(EasyEq.Flat());
        Assert.Equal(EasyEq.Parts.Count, bands.Count);
        Assert.All(bands, b => Assert.Equal(0, b.Gain));
        Assert.True(EasyEq.IsLayout(bands));
        Assert.True(EasyEq.IsFlat(EasyEq.GainsOf(bands)));
    }

    [Theory]
    [InlineData(new[] { 0, 0, 0, 0, 0 })]
    [InlineData(new[] { 6, 4, 0, -3, -5 })]
    [InlineData(new[] { -12, 12, -12, 12, -12 })]
    [InlineData(new[] { 1, -1, 2, -2, 3 })]
    public void Gains_RoundTripThroughBands(int[] gains) => Assert.Equal(gains, EasyEq.GainsOf(EasyEq.Bands(gains)));

    [Fact]
    public void Bands_HoldTheSlidersReach()
    {
        var bands = EasyEq.Bands(new[] { 40, -40, 0, 0, 0 });
        Assert.Equal(EasyEq.MaxGainDb, bands[0].Gain);
        Assert.Equal(-EasyEq.MaxGainDb, bands[1].Gain);
    }

    [Fact]
    public void BandsOfAnotherLayout_AreReadAsTheirResponseAtTheFiveFrequencies()
    {
        // The Parametric's default layout with a 6 dB low shelf at 100 Hz: bass is up, treble is not.
        var bands = Eq.Default();
        bands[0].Gain = 6;
        Assert.False(EasyEq.IsLayout(bands));
        var gains = EasyEq.GainsOf(bands);
        Assert.InRange(gains[0], 2, 4); // a shelf is at half its gain at its own frequency
        Assert.Equal(0, gains[4]);
    }

    [Fact]
    public void Presets_AreNormalThenTheSharedShapes_AllInsideTheReach()
    {
        Assert.Equal(new[] { "Normal", "Bass boost", "Vocal", "Night", "Loud" }, EasyEq.Presets.Select(p => p.Name));
        Assert.True(EasyEq.IsFlat(EasyEq.Presets[0].Gains));
        Assert.All(EasyEq.Presets, p =>
        {
            Assert.Equal(EasyEq.Parts.Count, p.Gains.Length);
            Assert.All(p.Gains, g => Assert.InRange(g, -EasyEq.MaxGainDb, EasyEq.MaxGainDb));
        });
        var bass = EasyEq.Presets.First(p => p.Name == "Bass boost").Gains;
        Assert.True(bass[0] > 0 && bass[0] > bass[4]);
        Assert.Same(EasyEq.Presets[2], EasyEq.PresetOf(EasyEq.Presets[2].Gains));
        Assert.Null(EasyEq.PresetOf(new[] { 1, 2, 3, 4, 5 }));
    }

    [Fact]
    public void Write_MakesTheEqualiserFirstAndSwitchesItOn_AndReadFindsIt()
    {
        var chain = new List<FxSlot> { FxCatalog.Create(FxCatalog.CompKind) };
        var slot = EasyEq.Write(chain, new[] { 3, 0, 0, 0, -2 });
        Assert.Same(slot, chain[0]);
        Assert.Equal(FxCatalog.EqKind, slot.Kind);
        Assert.True(slot.Enabled);
        Assert.Equal(new[] { 3, 0, 0, 0, -2 }, EasyEq.Read(chain));
        Assert.False(EasyEq.OnlyEasyRuns(chain));
    }

    [Fact]
    public void Write_KeepsTheEqualiserThatIsThere()
    {
        var chain = new List<FxSlot>();
        var first = EasyEq.Write(chain, new[] { 1, 1, 1, 1, 1 });
        var again = EasyEq.Write(chain, EasyEq.Flat());
        Assert.Same(first, again);
        Assert.Single(chain);
        Assert.True(EasyEq.OnlyEasyRuns(chain));
        Assert.True(EasyEq.IsFlat(EasyEq.Read(chain)));
    }

    [Fact]
    public void ADisabledEqualiser_ReadsAsFlat_AndOtherBandsAreNoticed()
    {
        var chain = new List<FxSlot>();
        EasyEq.Write(chain, new[] { 5, 0, 0, 0, 0 }).Enabled = false;
        Assert.True(EasyEq.IsFlat(EasyEq.Read(chain)));

        var custom = FxCatalog.Create(FxCatalog.EqKind);
        custom.Bands![1].Gain = 4;
        Assert.True(EasyEq.HasOtherBands(new List<FxSlot> { custom }));
        EasyEq.Write(new List<FxSlot> { custom }, EasyEq.Flat());
        Assert.False(EasyEq.HasOtherBands(new List<FxSlot> { custom }));
    }

    // ---- the goals ----

    [Fact]
    public void Goals_AreTheSixCardsInOrder_EachWithAPicture()
    {
        Assert.Equal(new[] { "better", "perapp", "mic", "joinmics", "twospeakers", "volume" }, EasyGoals.All.Select(g => g.Id));
        Assert.Equal(EasyGoals.All.Count, EasyGoals.All.Select(g => g.Id).Distinct().Count());
        Assert.All(EasyGoals.All, g =>
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Title));
            Assert.False(string.IsNullOrWhiteSpace(g.Fact));
            Assert.True(g.Picture.Count >= 2);
        });
    }

    [Fact]
    public void ACard_NumbersItsPicture_WithAnArrowBeforeEveryPartButTheFirst()
    {
        var card = new GoalCard(EasyGoals.All[0], isOn: false, _ => { });
        Assert.False(card.Steps[0].ArrowBefore);
        Assert.All(card.Steps.Skip(1), s => Assert.True(s.ArrowBefore));
        Assert.True(card.Steps.Any(s => s.IsApo));
    }
}
