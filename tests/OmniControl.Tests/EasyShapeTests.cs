using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Fx;

namespace OmniControl.Tests;

/// <summary>
/// The Easy sliders against an effect shaped in the full effects window: when the five sliders still describe it (<see cref="EasyEq.Matches"/>),
/// when they are greyed (<see cref="EasyEq.IsCustom"/>), the closest five-slider sound (<see cref="EasyEq.BestFit"/>), and that the
/// Easy chart is drawn by the same function as the FX window's.
/// </summary>
public class EasyShapeTests
{
    private static FxSlot EqSlot(IEnumerable<EqBand>? bands = null)
    {
        var slot = FxCatalog.Create(FxCatalog.EqKind);
        slot.Bands = (bands ?? EasyEq.Bands(EasyEq.Flat())).ToList();
        return slot;
    }

    [Fact]
    public void TheFlatLayout_Matches()
    {
        Assert.True(EasyEq.Matches(EasyEq.Bands(EasyEq.Flat())));
        Assert.True(EasyEq.Describes(EqSlot()));
    }

    [Fact]
    public void ChangedGains_InsideTheReach_StillMatch_AndTheSlidersFollowThem()
    {
        var bands = EasyEq.Bands(new[] { -6, 4, 0, 2, 0 });
        Assert.True(EasyEq.Matches(bands));
        Assert.Equal(new[] { -6, 4, 0, 2, 0 }, EasyEq.GainsOf(bands));
        bands[2].Gain = 7; // the FX window edits a gain: the Easy slider follows
        Assert.Equal(new[] { -6, 4, 7, 2, 0 }, EasyEq.GainsOf(bands));
    }

    [Fact]
    public void AFrequencyThatMoved_DoesNotMatch_AFineMoveDoes()
    {
        var bands = EasyEq.Bands(EasyEq.Flat());
        bands[1].Freq = 300 * 1.005;
        Assert.True(EasyEq.Matches(bands));
        bands[1].Freq = 504.6;
        Assert.False(EasyEq.Matches(bands));
    }

    [Fact]
    public void AnExtraBand_AMissingBand_AndAChangedType_DoNotMatch()
    {
        var extra = EasyEq.Bands(EasyEq.Flat());
        extra.Add(new EqBand { Id = 6, Freq = 5000, Gain = 0 });
        Assert.False(EasyEq.Matches(extra));

        var fewer = EasyEq.Bands(EasyEq.Flat());
        fewer.RemoveAt(2);
        Assert.False(EasyEq.Matches(fewer));

        var typed = EasyEq.Bands(EasyEq.Flat());
        typed[1].Type = EqType.HighShelf;
        Assert.False(EasyEq.Matches(typed));
    }

    [Fact]
    public void AGainBeyondTheReach_AndASwitchedOffBand_DoNotMatch()
    {
        var loud = EasyEq.Bands(EasyEq.Flat());
        loud[1].Gain = EasyEq.MaxGainDb + 5;
        Assert.False(EasyEq.Matches(loud));

        var off = EasyEq.Bands(EasyEq.Flat());
        off[3].Enabled = false;
        Assert.False(EasyEq.Matches(off));
        Assert.False(EasyEq.Matches(null));
    }

    [Fact]
    public void AnOverallGainOrAWetMix_AreAShapeTheSlidersCannotDescribe()
    {
        var gain = EqSlot();
        gain.Values[0] = 3;
        Assert.False(EasyEq.Describes(gain));
        Assert.True(EasyEq.IsCustom(gain));

        var wet = EqSlot();
        wet.Wet = 50;
        Assert.False(EasyEq.Describes(wet));
        Assert.False(EasyEq.IsCustom(wet), "a flat effect has no shape to lose, whatever its wet mix");
    }

    [Fact]
    public void TheOwnersEffect_IsCustom_AndBestFitSamplesItsRealResponse()
    {
        // Five bands as the FX window showed them: low shelf 100 Hz -6 dB, bell 504.6 Hz +17 dB, the rest near 0.
        var owner = EqSlot(new[]
        {
            new EqBand { Id = 1, Type = EqType.LowShelf, Freq = 100, Gain = -6, Q = Eq.ShelfQ },
            new EqBand { Id = 2, Type = EqType.Peak, Freq = 504.6, Gain = 17, Q = Eq.DefaultQ },
            new EqBand { Id = 3, Type = EqType.Peak, Freq = 1500, Gain = 0 },
            new EqBand { Id = 4, Type = EqType.Peak, Freq = 4000, Gain = 0 },
            new EqBand { Id = 5, Type = EqType.HighShelf, Freq = 10000, Gain = 0, Q = Eq.ShelfQ },
        });
        Assert.True(EasyEq.IsCustom(owner));
        Assert.True(EasyEq.HasCustom(new List<FxSlot> { owner }));

        var fit = EasyEq.BestFit(owner);
        var expected = EqMath.CurveDb(owner.Bands!, 0, EasyEq.Parts.Select(p => p.Freq).ToList());
        for (var i = 0; i < fit.Length; i++)
            Assert.Equal(Math.Clamp(Math.Round(expected[i]), -EasyEq.MaxGainDb, EasyEq.MaxGainDb), fit[i]);
        Assert.True(fit[1] > 0 || fit[2] > 0, "the hump around 500 Hz shows up in the nearby sliders");
        Assert.All(fit, g => Assert.InRange(g, -EasyEq.MaxGainDb, EasyEq.MaxGainDb));
        // The disabled equaliser's sliders read flat, an enabled one's read the best fit.
        owner.Enabled = false;
        Assert.True(EasyEq.IsFlat(EasyEq.Read(new List<FxSlot> { owner })));
        owner.Enabled = true;
        Assert.Equal(fit, EasyEq.Read(new List<FxSlot> { owner }));
    }

    [Fact]
    public void BestFit_IncludesTheOverallGain()
    {
        var slot = EqSlot();
        slot.Values[0] = 4;
        Assert.Equal(new[] { 4, 4, 4, 4, 4 }, EasyEq.BestFit(slot));
    }

    [Fact]
    public void Write_ReplacesACustomShapeCompletely_SoTheSlidersDescribeItAgain()
    {
        var slot = EqSlot();
        slot.Bands![1].Freq = 504.6;
        slot.Bands[1].Gain = 17;
        slot.Values[0] = 3;
        slot.Wet = 60;
        var chain = new List<FxSlot> { slot };
        Assert.True(EasyEq.HasCustom(chain));
        EasyEq.Write(chain, EasyEq.BestFit(slot));
        Assert.False(EasyEq.HasCustom(chain));
        Assert.True(EasyEq.Describes(slot));
        Assert.Equal(FxSlot.DefaultWet, slot.Wet);
    }

    [Fact]
    public void ADefaultEqualiser_HasNoShapeToLose()
    {
        var chain = new List<FxSlot> { FxCatalog.Create(FxCatalog.EqKind) };
        Assert.False(EasyEq.HasCustom(chain));
        Assert.False(EasyEq.HasCustom(new List<FxSlot>()));
    }

    [Fact]
    public void MasterSound_KeepsACustomShape_AndOnlySwitchesItOn()
    {
        var mixer = new FakeMixer();
        var custom = EqSlot();
        custom.Bands![1].Freq = 504.6;
        custom.Bands[1].Gain = 17;
        custom.Enabled = false;
        mixer.Chain.Add(custom);
        EasyApply.MasterSound(mixer);
        Assert.True(custom.Enabled);
        Assert.Equal(504.6, custom.Bands[1].Freq);
        Assert.Equal(17, custom.Bands[1].Gain);
        Assert.True(mixer.On);
    }

    [Fact]
    public void TheEasyChart_IsTheSharedResponseOfTheEffect()
    {
        // The Easy drawer draws a FxAnalysisViewModel fed the row's chain, exactly as the FX window's header does (same class, same call).
        var owner = EqSlot();
        owner.Bands![0].Gain = -6;
        owner.Bands[1].Freq = 504.6;
        owner.Bands[1].Gain = 17;
        owner.Values[0] = 2;
        var chain = new List<FxSlot> { owner };
        using var chart = new FxAnalysisViewModel(() => RowLoad.None);
        chart.Show(chain, FxEngines.Program, true);
        var shared = ChainResponse.Of(chain, FxEngines.Program, FxAnalysisViewModel.Freqs);
        Assert.Equal(shared.Db, chart.Curve);
        Assert.Equal(shared.PeakDb, chart.PeakDb);
        Assert.True(chart.PeakDb > 12, "the +17 dB hump is in the chart");
        Assert.True(chart.ScaleDb > FxAnalysisViewModel.DefaultScaleDb, "the scale widens for it as the FX window's does");

        owner.Bands[1].Gain = 0; // an edit in the FX window: showing again follows it
        chart.Show(chain, FxEngines.Program, true);
        Assert.Equal(ChainResponse.Of(chain, FxEngines.Program, FxAnalysisViewModel.Freqs).Db, chart.Curve);
    }

    private sealed class FakeMixer : IEasyMixer
    {
        public List<FxSlot> Chain { get; } = new();
        public bool On { get; private set; }
        public List<FxSlot> FxChain(string? key) => Chain;
        public bool IsFxOn(string? key) => On;
        public void SetFxOn(string? key, bool on) => On = on;
        public void SaveFx(string? key) { }
        public string InputKey(string deviceId, string name) => throw new NotSupportedException();
        public EffectsRoute FxRouteOf(string key) => throw new NotSupportedException();
        public void SetFxRoute(string key, EffectsRoute route) => throw new NotSupportedException();
        public IReadOnlyList<CombinedDevice> Combined => throw new NotSupportedException();
        public void AddCombined(CombinedDevice device) => throw new NotSupportedException();
        public EffectsRoute MasterMethod => throw new NotSupportedException();
        public void SetMasterMethod(EffectsRoute route) => throw new NotSupportedException();
        public void SetMasterPlaysOn(IEnumerable<string> targets) => throw new NotSupportedException();
    }
}
