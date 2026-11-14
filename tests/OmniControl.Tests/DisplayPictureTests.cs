using System.Text.Json;
using OmniControl.App.Display;
using OmniControl.App.Pages.Displays;
using OmniControl.Core.Engine;
using OmniControl.Core.Models;

namespace OmniControl.Tests;

/// <summary>
/// The Displays page's software picture and monitor features, without touching any display: the one gamma-ramp computation, the colour
/// matrix, the warm-light schedule's maths, the service that layers them (over a fake ramp), the saved presets, and what a monitor's
/// capabilities become (fake capability strings and reads).
/// </summary>
public class DisplayPictureTests
{
    // ---- the ramp: one pure function, neutral is the original ----

    private static ushort[] Ramp(SoftwareColor color) => RampMath.Build(color, RampMath.Identity());

    private static ushort At(ushort[] ramp, int channel, int index) => ramp[channel * 256 + index];

    [Fact]
    public void NeutralPicture_IsExactlyTheDisplaysOwnRamp()
    {
        Assert.Equal(RampMath.Identity(), Ramp(SoftwareColor.Neutral));

        // A calibrated display's own curve is kept, not replaced by a straight line.
        var calibrated = RampMath.Identity().Select(v => (ushort)(Math.Pow(v / 65535.0, 1.1) * 65535)).ToArray();
        Assert.Equal(calibrated, RampMath.Build(SoftwareColor.Neutral, calibrated));
    }

    [Fact]
    public void EachSetting_MovesTheRampTheWayItsNameSays()
    {
        var neutral = Ramp(SoftwareColor.Neutral);

        Assert.True(At(Ramp(SoftwareColor.Neutral with { Brightness = 60 }), 1, 200) < At(neutral, 1, 200));
        Assert.True(At(Ramp(SoftwareColor.Neutral with { Gamma = 30 }), 1, 100) > At(neutral, 1, 100));     // midtones lifted
        Assert.True(At(Ramp(SoftwareColor.Neutral with { Gamma = -30 }), 1, 100) < At(neutral, 1, 100));    // midtones darkened
        Assert.Equal(At(neutral, 1, 255), At(Ramp(SoftwareColor.Neutral with { Gamma = 30 }), 1, 255));     // the ends stay
        Assert.True(At(Ramp(SoftwareColor.Neutral with { Black = 20 }), 1, 0) > 0);                          // black lifted
        Assert.True(At(Ramp(SoftwareColor.Neutral with { White = 80 }), 1, 255) < At(neutral, 1, 255));      // white limited
        Assert.True(At(Ramp(SoftwareColor.Neutral with { Contrast = 150 }), 1, 40) < At(neutral, 1, 40));
    }

    [Fact]
    public void ChannelGain_ChangesOnlyItsChannel()
    {
        var neutral = Ramp(SoftwareColor.Neutral);
        var red = Ramp(SoftwareColor.Neutral with { Red = 50 });
        Assert.True(At(red, 0, 255) < At(neutral, 0, 255));
        Assert.Equal(At(neutral, 1, 255), At(red, 1, 255));
        Assert.Equal(At(neutral, 2, 255), At(red, 2, 255));
    }

    [Fact]
    public void WarmTemperature_DimsBlueMost_AndNoChannelVanishes()
    {
        var warm = Ramp(SoftwareColor.Neutral with { Temperature = SoftwareColor.TemperatureMin });
        Assert.True(At(warm, 2, 255) < At(warm, 1, 255));
        Assert.True(At(warm, 1, 255) < At(warm, 0, 255));
        Assert.True(At(warm, 2, 255) > 65535 * 0.15);
    }

    [Fact]
    public void EverySettingTogether_StillMakesARisingRampWithinRange()
    {
        var all = new SoftwareColor(50, 150, 3000, 20, 40, 60, 50, 30, 70);
        var ramp = Ramp(all);
        for (var channel = 0; channel < 3; channel++)
            for (var i = 1; i < 256; i++)
                Assert.True(At(ramp, channel, i) >= At(ramp, channel, i - 1), $"channel {channel} falls at {i}");
    }

    [Fact]
    public void OutOfRangeValues_AreLimitedBeforeTheyReachTheRamp()
    {
        var wild = new SoftwareColor(-5, 999, 1, 0, 500, 100, 900, -3, 0);
        Assert.Equal(new SoftwareColor(40, 150, 3000, 20, 100, 100, 50, 0, 70), wild.Clamped());
        Assert.Equal(Ramp(wild.Clamped()), Ramp(wild));
    }

    // ---- the whole-desktop colour matrix ----

    private static float[] Through(float[] matrix, float r, float g, float b)
    {
        var input = new[] { r, g, b };
        var output = new float[3];
        for (var column = 0; column < 3; column++)
        {
            output[column] = matrix[4 * 5 + column];
            for (var row = 0; row < 3; row++) output[column] += input[row] * matrix[row * 5 + column];
        }
        return output;
    }

    [Fact]
    public void NeutralColorMatrix_ChangesNothing()
    {
        Assert.True(ColorMatrix.IsNeutral(100, false));
        Assert.Equal(new[] { 0.2f, 0.5f, 0.9f }, Through(ColorMatrix.Build(100, false), 0.2f, 0.5f, 0.9f).Select(v => (float)Math.Round(v, 4)).ToArray());
    }

    [Fact]
    public void Grayscale_GivesEqualChannels_AndInvertTurnsWhiteToBlack()
    {
        var gray = Through(ColorMatrix.Build(0, false), 0.9f, 0.2f, 0.4f);
        Assert.Equal(gray[0], gray[1], 4);
        Assert.Equal(gray[1], gray[2], 4);

        var black = Through(ColorMatrix.Build(100, true), 1, 1, 1);
        Assert.All(black, v => Assert.Equal(0, v, 4));
        var white = Through(ColorMatrix.Build(100, true), 0, 0, 0);
        Assert.All(white, v => Assert.Equal(1, v, 4));

        // Both together: a gray stays gray, inverted.
        var both = Through(ColorMatrix.Build(0, true), 0.9f, 0.2f, 0.4f);
        Assert.Equal(both[0], both[2], 4);
        Assert.Equal(1 - gray[0], both[0], 4);
    }

    // ---- the warm-light schedule ----

    [Theory]
    [InlineData(0, 1)]       // midnight is inside the window
    [InlineData(20 * 60, 0)] // before it
    [InlineData(21 * 60, 0)] // the window opens: nothing yet
    [InlineData(21 * 60 + 15, 0.5)]
    [InlineData(22 * 60, 1)]
    [InlineData(3 * 60, 1)]  // after midnight, still inside
    [InlineData(6 * 60 + 45, 0.5)]
    [InlineData(7 * 60, 0)]  // it closes
    [InlineData(12 * 60, 0)]
    public void Factor_EasesInAndOutAcrossMidnight(double minute, double expected) =>
        Assert.Equal(expected, WarmSchedule.Factor(minute, 21 * 60, 7 * 60), 3);

    [Fact]
    public void ShortWindow_EasesWithinItself_AndEqualTimesMeanNoWindow()
    {
        Assert.Equal(1, WarmSchedule.Factor(10 * 60 + 5, 10 * 60, 10 * 60 + 10), 3);
        Assert.Equal(0, WarmSchedule.Factor(5 * 60, 5 * 60, 5 * 60));
    }

    [Fact]
    public void Kelvin_WarmsTowardsTheTarget_ButNeverCools()
    {
        Assert.Equal(6500, WarmSchedule.Kelvin(6500, 0, 3400));
        Assert.Equal(3400, WarmSchedule.Kelvin(6500, 1, 3400));
        Assert.Equal(4950, WarmSchedule.Kelvin(6500, 0.5, 3400));
        Assert.Equal(3000, WarmSchedule.Kelvin(3000, 1, 3400)); // already warmer than the target
    }

    [Fact]
    public void StateText_SaysWhatTheScheduleIsDoing()
    {
        Assert.Equal("Off", WarmSchedule.StateText(false, 22 * 60, 21 * 60, 7 * 60));
        Assert.Equal("Active until 07:00", WarmSchedule.StateText(true, 22 * 60, 21 * 60, 7 * 60));
        Assert.Equal("Starts at 21:00", WarmSchedule.StateText(true, 12 * 60, 21 * 60, 7 * 60));
        Assert.Equal("21:30", WarmSchedule.Clock(21 * 60 + 30));
        Assert.Equal(48, WarmSchedule.Times().Count());
    }

    // ---- the service that layers the picture, over a fake ramp ----

    private sealed class FakeRamp : IGammaDisplay
    {
        public ushort[]? Original { get; private set; }
        public bool DriverRefuses { get; set; }
        public bool Refuse { get; set; }
        public List<SoftwareColor> Applied { get; } = new();
        public OperationResult Capture() { Original = RampMath.Identity(); return OperationResult.Ok(); }

        public OperationResult Apply(SoftwareColor color)
        {
            if (Refuse) { DriverRefuses = true; return OperationResult.Fail(GammaDisplay.DriverRefusal); }
            DriverRefuses = false;
            Applied.Add(color);
            return OperationResult.Ok();
        }
    }

    private static (SoftwareDisplayService Service, Dictionary<string, FakeRamp> Ramps) FakeService()
    {
        var ramps = new Dictionary<string, FakeRamp>();
        var service = new SoftwareDisplayService(name => ramps[name] = new FakeRamp(), keepRecovery: false);
        return (service, ramps);
    }

    [Fact]
    public void Service_ChangesADisplay_AndResetGivesItsOwnPictureBack()
    {
        var (service, ramps) = FakeService();
        service.Track("D1");
        Assert.Empty(ramps); // tracking a display does not touch it

        Assert.True(service.Set("D1", SoftwareColor.Neutral with { Gamma = 20 }).Success);
        Assert.Equal(20, ramps["D1"].Applied.Single().Gamma);

        service.Reset("D1");
        Assert.True(ramps["D1"].Applied.Last().IsNeutral);
        Assert.Empty(service.Changed);
    }

    [Fact]
    public void Schedule_WarmsEveryTrackedDisplay_AndHandsBackWhenItEnds()
    {
        var (service, ramps) = FakeService();
        service.Track("D1");
        service.Track("D2");
        service.Set("D2", SoftwareColor.Neutral with { Temperature = 4000 }); // already warmer than the target

        service.SetWarmth(1, 5000);
        Assert.Equal(5000, ramps["D1"].Applied.Last().Temperature);
        Assert.Equal(4000, ramps["D2"].Applied.Last().Temperature);
        Assert.Equal(5000, service.Effective("D1").Temperature);

        service.SetWarmth(0, 5000);
        Assert.Equal(new[] { "D2" }, service.Changed); // D1 is neutral again and released
        Assert.True(ramps["D1"].Applied.Last().IsNeutral);
    }

    [Fact]
    public void RefusingDriver_IsRemembered_AndNotAskedAgainUntilRefresh()
    {
        var (service, ramps) = FakeService();
        service.Track("D1");
        service.Set("D1", SoftwareColor.Neutral with { Brightness = 80 });
        ramps["D1"].Refuse = true;

        var result = service.Set("D1", SoftwareColor.Neutral with { Brightness = 70 });
        Assert.False(result.Success);
        Assert.True(service.IsRefused("D1"));

        var calls = ramps["D1"].Applied.Count;
        ramps["D1"].Refuse = false;
        Assert.False(service.Set("D1", SoftwareColor.Neutral with { Brightness = 60 }).Success);
        Assert.Equal(calls, ramps["D1"].Applied.Count);

        service.ClearRefusals();
        Assert.True(service.Set("D1", SoftwareColor.Neutral with { Brightness = 60 }).Success);
    }

    [Fact]
    public void Dispose_PutsEveryDisplayBack_EvenWhileTheScheduleIsWarming()
    {
        var (service, ramps) = FakeService();
        service.Track("D1");
        service.SetWarmth(1, 4000);
        service.Set("D1", SoftwareColor.Neutral with { Red = 60 });

        service.Dispose();
        Assert.True(ramps["D1"].Applied.Last().IsNeutral);
        Assert.Empty(service.Changed);
    }

    // ---- saved settings: schedule and presets ----

    private static DisplayPreset Preset(string name, int gamma = 0) =>
        new() { Name = name, Any = new PictureValues { Gamma = gamma }, Displays = { ["D1"] = new PictureValues { Gamma = gamma, Red = 80 } }, Saturation = 80, Invert = true };

    [Fact]
    public void Presets_SaveReplaceRenameDelete_AndSaveEachTime()
    {
        var saves = 0;
        var store = new DisplaySettingsStore(null, _ => saves++);

        store.Save(Preset("Evening"));
        store.Save(Preset("Day", 5));
        store.Save(Preset("evening", 9)); // the same name in another case replaces it where it stands
        Assert.Equal(new[] { "evening", "Day" }, store.Presets.Select(p => p.Name));

        Assert.Equal("A preset of this name exists.", store.Rename("Day", "EVENING"));
        Assert.NotNull(store.Rename("Day", "a/b"));
        Assert.Null(store.Rename("Day", "Morning"));
        Assert.Equal(80, store.Find("morning")!.For("D1").Red);
        Assert.Equal(100, store.Find("morning")!.For("D9").Red); // a display the preset has none for gets the picture kept for any

        store.Delete("EVENING");
        Assert.Equal(new[] { "Morning" }, store.Presets.Select(p => p.Name));
        Assert.Equal(5, saves);
    }

    [Fact]
    public void Settings_SurviveASaveAndLoad_AndADamagedPresetIsDropped()
    {
        DisplaySettings? written = null;
        var store = new DisplaySettingsStore(null, s => written = s);
        store.WarmLight.Enabled = true;
        store.WarmLight.Start = 22 * 60;
        store.Save(Preset("Evening", 12));

        var back = JsonSerializer.Deserialize<DisplaySettings>(JsonSerializer.Serialize(written))!;
        back.Presets.Add(new DisplayPreset { Name = "a:b" }); // a hand-edited name Windows could not keep
        var loaded = new DisplaySettingsStore(back, _ => { });

        Assert.True(loaded.WarmLight.Enabled);
        Assert.Equal(22 * 60, loaded.WarmLight.Start);
        Assert.Equal(7 * 60, loaded.WarmLight.End);
        var preset = Assert.Single(loaded.Presets);
        Assert.Equal("Evening", preset.Name);
        Assert.Equal(12, preset.For("D1").Gamma);
        Assert.True(preset.Invert);
        Assert.Equal(SoftwareColor.KelvinOfWarmth(0), preset.Any.ToColor().Temperature);
    }

    // ---- a monitor's capabilities become cards ----

    private const string Capabilities =
        "(prot(monitor)type(lcd)model(Test)cmds(01 02 03 07 0C E3 F3)vcp(10 12 14(01 05 0B) 16 18 1A 60(0F 11 12) 6C 6E 70 72 87 DC(00 03 05) D6(01 04 05))mccs_ver(2.2))";

    private static (MonitorCapabilities Capabilities, List<byte> Reads) Probe(string? capabilities, params byte[] answering)
    {
        var reads = new List<byte>();
        VcpValue? Read(byte code)
        {
            reads.Add(code);
            // The current value of a choice is one its list names, as a real monitor's is.
            var current = code switch { 0x14 => 5u, 0x60 => 0x11u, 0xDC => 3u, _ => 40u };
            return answering.Contains(code) ? new VcpValue(current, 100, 1, false) : null;
        }
        return (MonitorProbe.Run(Read, () => capabilities), reads);
    }

    [Fact]
    public void ListedFeatures_BecomeCards_AndTheRestAreNotEvenAsked()
    {
        var (found, reads) = Probe(Capabilities, 0x10, 0x12, 0x14, 0x16, 0x18, 0x1A, 0x60, 0x6C, 0x6E, 0x70, 0x72, 0x87, 0xDC, 0xD6, 0x90);

        foreach (var code in new byte[] { 0x10, 0x12, 0x16, 0x18, 0x1A, 0x6C, 0x6E, 0x70, 0x72, 0x87 }) Assert.NotNull(found.Level(code));
        Assert.False(found.Has(0x90));          // it answers, but the monitor did not list it: not offered
        Assert.DoesNotContain((byte)0x90, reads);
        Assert.DoesNotContain((byte)0x8A, reads);
        Assert.Equal(new[] { "sRGB", "6500 K", "User 1" }, found.ChoicesOf(0x14).Select(c => c.Name));
        Assert.Equal(new[] { "Standard", "Movie", "Games" }, found.ChoicesOf(0xDC).Select(c => c.Name));
        Assert.Equal(new[] { "DisplayPort 1", "HDMI 1", "HDMI 2" }, found.ChoicesOf(0x60).Select(c => c.Name));
    }

    [Fact]
    public void WithoutAList_EveryKnownFeatureIsAsked_AndTheOnesAnsweredAreOffered()
    {
        var (found, reads) = Probe(null, 0x10, 0x12, 0x14, 0x90, 0x5B);
        Assert.NotNull(found.Level(0x90));
        Assert.NotNull(found.Level(0x5B));
        Assert.True(VcpFeatures.All.All(f => reads.Contains(f.Code)));
        Assert.Empty(found.ChoicesOf(0x14)); // no list of values, so a choice card could only be greyed out
    }

    [Fact]
    public void MonitorWithDdcOff_IsFoundQuickly()
    {
        var (found, reads) = Probe(Capabilities);
        Assert.False(found.Answers);
        Assert.Equal(3, reads.Count);
    }

    [Fact]
    public void CurrentValue_IsAlwaysAChoice_EvenWhenTheListForgotIt()
    {
        var (found, _) = Probe("(vcp(10 14(01 02)))", 0x10, 0x14);
        Assert.Contains(found.ChoicesOf(0x14), c => c.Value == 5);
    }

    [Fact]
    public void ControlList_HasOneEntryPerFeature_WithUniqueKeysAndNames()
    {
        Assert.Equal(VcpFeatures.Levels.Count(), DisplayControls.Hardware.Count);
        Assert.Equal(VcpFeatures.Choices.Count(), DisplayControls.HardwareChoices.Count);
        Assert.Equal(VcpFeatures.All.Count, VcpFeatures.All.Select(f => f.Code).Distinct().Count());
        Assert.Equal(VcpFeatures.All.Count, VcpFeatures.All.Select(f => f.Key).Distinct().Count());

        var osdNames = DisplayControls.All.Select(s => s.Osd).ToList();
        Assert.Equal(osdNames.Count, osdNames.Distinct().Count());

        var softwareKeys = DisplayControls.Software.Select(s => DisplayControls.SoftwareKey(s.Spec)).Append(DisplayControls.SoftwareKey(DisplayControls.SoftwareSaturation)).ToList();
        Assert.Equal(softwareKeys.Count, softwareKeys.Distinct().Count());
        Assert.DoesNotContain(DisplayControls.Hardware.Select(h => h.Spec.Key).Concat(softwareKeys), key => key.Contains(':') || key == "volume");
    }

    [Fact]
    public void PictureValues_RoundTripThroughASoftwareColor()
    {
        var values = new PictureValues { Brightness = 70, Warmth = -50, Red = 90, Gamma = 10, Black = 5, White = 95 };
        var color = values.ToColor();
        Assert.Equal(SoftwareColor.KelvinOfWarmth(-50), color.Temperature);
        Assert.Equal((70, 90, 10, 5, 95), (color.Brightness, color.Red, color.Gamma, color.Black, color.White));
    }
}
